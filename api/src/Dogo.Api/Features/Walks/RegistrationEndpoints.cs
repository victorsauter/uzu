using System.Security.Claims;
using Dogo.Api.Common;
using Dogo.Api.Features.Dogs;
using Dogo.Api.Features.Notifications;
using Dogo.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Dogo.Api.Features.Walks;

public static class RegistrationEndpoints
{
    public static IEndpointRouteBuilder MapRegistrationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/walks/{id:guid}/registrations").WithTags("Registrations");

        group.MapPost("", CreateRegistrations)
            .WithName("createRegistrations")
            .WithSummary("Register one or more dogs the signed-in user is an owner of to a walk; all or none are registered. The signed-in user is the human who comes with them.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{registrationId:guid}", CancelRegistration)
            .WithName("cancelRegistration")
            .WithSummary("Cancel a registration before the walk starts: its author or any owner of the dog. When another owner cancels, the author is notified. Idempotent.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{registrationId:guid}/accept", AcceptRegistration)
            .WithName("acceptRegistration")
            .WithSummary("Accept a pending registration (organizer only). Size, capacity, membership and the organisation's status are checked again.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{registrationId:guid}/decline", DeclineRegistration)
            .WithName("declineRegistration")
            .WithSummary("Decline a pending registration or remove an accepted participant (organizer only).")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return routes;
    }

    private static async Task<Results<Created<List<RegistrationResponse>>, ProblemHttpResult>> CreateRegistrations(
        Guid id,
        CreateRegistrationsRequest request,
        ClaimsPrincipal principal,
        DogoDbContext db,
        TimeProvider timeProvider,
        NotificationQueue notifications,
        CancellationToken cancellationToken)
    {
        var userId = principal.GetUserId();

        // The walk row lock serialises registrations to the same walk: capacity and duplicate
        // checks below see every registration committed before them.
        // The dogs are share-locked first, then the walk (lock order: dogs, then walk; ADR 0037).
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var ownDogs = await RegistrationRules.LockDogsAsync(db, [.. request.DogIds], userId, cancellationToken);
        var walk = await db.LockWalkAsync(id, cancellationToken);
        if (walk is null)
        {
            return WalkEndpoints.WalkNotFound();
        }

        var attempt = new RegistrationAttempt(walk, userId, request.DogIds, ownDogs, timeProvider.GetUtcNow());
        var problem = await RegistrationRules.CheckAsync(db, attempt, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        var status = attempt.InitialStatus;
        var registrations = request.DogIds
            .Select(dogId => new Registration { WalkId = walk.Id, DogId = dogId, UserId = userId, Status = status })
            .ToList();
        db.Registrations.AddRange(registrations);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: Registration.ActiveDogIndexName })
        {
            // Not expected while the walk is locked; the partial unique index is the last line of defence.
            return RegistrationRules.Duplicate();
        }

        await transaction.CommitAsync(cancellationToken);

        // Spec §4.4: the organizer is told (one notification for all the dogs; never for their own dogs).
        var type = status == RegistrationStatus.Pending ? NotificationType.RegistrationRequested : NotificationType.RegistrationCreated;
        notifications.Enqueue(new NotificationRequest(type, NotificationSubject.ForWalk(walk), userId, walk.OrganizerRecipients()));
        return TypedResults.Created((string?)null, registrations.Select(RegistrationResponse.From).ToList());
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> CancelRegistration(
        Guid id,
        Guid registrationId,
        ClaimsPrincipal principal,
        DogoDbContext db,
        TimeProvider timeProvider,
        NotificationQueue notifications,
        CancellationToken cancellationToken)
    {
        var registration = await db.Registrations.Include(r => r.Walk).Include(r => r.Dog)
            .SingleOrDefaultAsync(r => r.Id == registrationId && r.WalkId == id, cancellationToken);
        if (registration is null)
        {
            return RegistrationNotFound();
        }

        // Spec §4.3: its author, or any owner of the dog (ADR 0042).
        var userId = principal.GetUserId();
        var byAuthor = registration.UserId == userId;
        if (!byAuthor && (registration.DogId is not { } dogId || !await db.IsOwnerAsync(dogId, userId, cancellationToken)))
        {
            return Problems.Forbidden(ErrorCodes.RegistrationForbidden, "Only its author or an owner of the dog can cancel this registration.");
        }

        if (registration.Status == RegistrationStatus.Cancelled)
        {
            return TypedResults.NoContent();
        }

        if (registration.Walk.HasStarted(timeProvider.GetUtcNow()))
        {
            return RegistrationRules.NotOpen();
        }

        if (!registration.IsActive)
        {
            return NotActive();
        }

        // Frees the place at once (only accepted registrations count). The status condition makes
        // the write safe against a concurrent decision of the organizer (ADR 0037): a registration
        // declined meanwhile stays declined.
        if (await TransitionAsync(db, registrationId, RegistrationStatus.Cancelled, [RegistrationStatus.Pending, RegistrationStatus.Accepted], cancellationToken) == 0)
        {
            return await CurrentStatusAsync(db, registrationId, cancellationToken) == RegistrationStatus.Cancelled
                ? TypedResults.NoContent()
                : NotActive();
        }

        // Spec §4.4: cancelled by another owner of the dog, the author is told, so as not to come to
        // the walk believing the dog registered. The dog's name is in the text.
        if (!byAuthor)
        {
            notifications.Enqueue(new NotificationRequest(
                NotificationType.RegistrationCancelledByOwner,
                NotificationSubject.ForWalk(registration.Walk),
                userId,
                [registration.UserId],
                registration.Dog?.Name));
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<RegistrationResponse>, ProblemHttpResult>> AcceptRegistration(
        Guid id,
        Guid registrationId,
        ClaimsPrincipal principal,
        DogoDbContext db,
        TimeProvider timeProvider,
        NotificationQueue notifications,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // The dog is share-locked before the walk, as at registration (ADR 0037): its size cannot
        // change and it cannot be deleted until the decision is written.
        var dogIds = await db.Registrations.AsNoTracking()
            .Where(r => r.Id == registrationId && r.WalkId == id && r.DogId != null)
            .Select(r => r.DogId!.Value)
            .ToArrayAsync(cancellationToken);
        var dog = (await RegistrationRules.LockDogsAsync(db, dogIds, null, cancellationToken)).SingleOrDefault();

        var (walk, registration, problem) = await LockForDecisionAsync(db, id, registrationId, principal.GetUserId(), cancellationToken);
        if (registration is null)
        {
            return problem!;
        }

        if (registration.Status == RegistrationStatus.Accepted)
        {
            return TypedResults.Ok(RegistrationResponse.From(registration));
        }

        if (!walk!.IsOpen(timeProvider.GetUtcNow()))
        {
            return RegistrationRules.NotOpen();
        }

        if (registration.Status != RegistrationStatus.Pending)
        {
            return NotActive();
        }

        // The rules of the request are replayed, except the block and the duplicate (spec §4.3):
        // the organisation may have been closed or rejected since (spec §4.6).
        if (walk.OrganizationId is { } organizationId && !await RegistrationRules.IsOrganizationApprovedAsync(db, organizationId, cancellationToken))
        {
            return RegistrationRules.NotOpen();
        }

        // The allowed sizes may have changed since the request.
        if (dog is null)
        {
            // Deleted meanwhile: its upcoming registrations are cancelled (spec §4.1).
            return NotActive();
        }

        if (!walk.AllowedSizes.Contains(dog.Size))
        {
            return RegistrationRules.SizeNotAllowed();
        }

        if (await db.CountAcceptedAsync(walk.Id, cancellationToken) >= walk.MaxDogs)
        {
            return RegistrationRules.Full();
        }

        // The author may have left the organisation, or been removed from it, since the request.
        if (walk.OrganizationId is { } memberOrganizationId
            && registration.UserId != walk.OrganizerId
            && !await RegistrationRules.IsAcceptedMemberAsync(db, memberOrganizationId, registration.UserId, cancellationToken))
        {
            return RegistrationRules.MembersOnly();
        }

        // Only a registration still pending is accepted: its author may have cancelled it, or
        // deleted the dog, since it was read (ADR 0037). Those writes do not take the walk lock.
        if (await TransitionAsync(db, registrationId, RegistrationStatus.Accepted, [RegistrationStatus.Pending], cancellationToken) == 0)
        {
            return await CurrentStatusAsync(db, registrationId, cancellationToken) == RegistrationStatus.Accepted
                ? TypedResults.Ok(RegistrationResponse.From(registration))
                : NotActive();
        }

        registration.Status = RegistrationStatus.Accepted;
        await transaction.CommitAsync(cancellationToken);

        // Spec §4.4: the author of the registration is told.
        notifications.Enqueue(new NotificationRequest(
            NotificationType.RegistrationAccepted, NotificationSubject.ForWalk(walk), walk.OrganizerId, [registration.UserId]));
        return TypedResults.Ok(RegistrationResponse.From(registration));
    }

    private static async Task<Results<Ok<RegistrationResponse>, ProblemHttpResult>> DeclineRegistration(
        Guid id,
        Guid registrationId,
        ClaimsPrincipal principal,
        DogoDbContext db,
        TimeProvider timeProvider,
        NotificationQueue notifications,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var (walk, registration, problem) = await LockForDecisionAsync(db, id, registrationId, principal.GetUserId(), cancellationToken);
        if (registration is null)
        {
            return problem!;
        }

        if (registration.Status == RegistrationStatus.Declined)
        {
            return TypedResults.Ok(RegistrationResponse.From(registration));
        }

        if (walk!.HasStarted(timeProvider.GetUtcNow()))
        {
            return RegistrationRules.NotOpen();
        }

        if (!registration.IsActive)
        {
            return NotActive();
        }

        // Declining a pending request or removing an accepted participant (spec §4.3). A registration
        // cancelled by its author meanwhile stays cancelled (ADR 0037).
        if (await TransitionAsync(db, registrationId, RegistrationStatus.Declined, [RegistrationStatus.Pending, RegistrationStatus.Accepted], cancellationToken) == 0)
        {
            return NotActive();
        }

        registration.Status = RegistrationStatus.Declined;
        await transaction.CommitAsync(cancellationToken);

        // Spec §4.4: the author of the registration is told.
        notifications.Enqueue(new NotificationRequest(
            NotificationType.RegistrationDeclined, NotificationSubject.ForWalk(walk), walk.OrganizerId, [registration.UserId]));
        return TypedResults.Ok(RegistrationResponse.From(registration));
    }

    /// <summary>
    /// Locks the walk and loads one of its registrations for a decision by the organizer:
    /// 404 when either does not exist, 403 <c>walk.forbidden</c> for anyone but the organizer.
    /// </summary>
    private static async Task<(Walk? Walk, Registration? Registration, ProblemHttpResult? Problem)> LockForDecisionAsync(
        DogoDbContext db, Guid walkId, Guid registrationId, Guid userId, CancellationToken cancellationToken)
    {
        var (walk, problem) = await WalkEndpoints.LockOrganizedWalkAsync(db, walkId, userId, cancellationToken);
        if (walk is null)
        {
            return (null, null, problem);
        }

        var registration = await db.Registrations.SingleOrDefaultAsync(r => r.Id == registrationId && r.WalkId == walkId, cancellationToken);
        return registration is null ? (walk, null, RegistrationNotFound()) : (walk, registration, null);
    }

    /// <summary>
    /// Moves a registration to <paramref name="target"/> only if its current status is one of
    /// <paramref name="from"/>, in one conditional <c>UPDATE</c>; returns the number of rows changed.
    /// Under <c>READ COMMITTED</c>, PostgreSQL waits for a concurrent writer of the row and evaluates
    /// the condition again on the version it committed, so no status is ever overwritten blindly.
    /// </summary>
    private static Task<int> TransitionAsync(
        DogoDbContext db, Guid registrationId, RegistrationStatus target, RegistrationStatus[] from, CancellationToken cancellationToken) =>
        db.Registrations
            .Where(r => r.Id == registrationId && from.Contains(r.Status))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, target), cancellationToken);

    private static Task<RegistrationStatus> CurrentStatusAsync(DogoDbContext db, Guid registrationId, CancellationToken cancellationToken) =>
        db.Registrations.Where(r => r.Id == registrationId).Select(r => r.Status).SingleAsync(cancellationToken);

    private static ProblemHttpResult RegistrationNotFound() =>
        Problems.NotFound(ErrorCodes.RegistrationNotFound, "The registration does not exist on this walk.");

    private static ProblemHttpResult NotActive() =>
        Problems.Conflict(ErrorCodes.RegistrationNotActive, "The registration is no longer pending or accepted.");
}
