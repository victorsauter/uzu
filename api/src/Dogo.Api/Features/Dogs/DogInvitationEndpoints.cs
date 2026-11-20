using System.Security.Claims;
using Dogo.Api.Common;
using Dogo.Api.Features.Users;
using Dogo.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Dogo.Api.Features.Dogs;

/// <summary>
/// Invitations to become one of a dog's humans (spec §4.1, ADR 0042). Owners create, list and revoke
/// them; the invited person, signed in, looks at one by its token and accepts it.
/// </summary>
public static class DogInvitationEndpoints
{
    public static IEndpointRouteBuilder MapDogInvitationEndpoints(this IEndpointRouteBuilder routes)
    {
        var dogs = routes.MapGroup("/dogs/{id:guid}/invitations").WithTags("Dogs");

        dogs.MapPost("", CreateInvitation)
            .WithName("createDogInvitation")
            .WithSummary("Create an invitation to the dog (any of its owners). Returns the token and the link uzu://invitation/{token}, once; valid 7 days, single use. Only the Owner role is accepted.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        dogs.MapGet("", ListInvitations)
            .WithName("listDogInvitations")
            .WithSummary("Pending invitations of the dog (owners only), oldest first, without their tokens.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        dogs.MapDelete("/{invitationId:guid}", RevokeInvitation)
            .WithName("revokeDogInvitation")
            .WithSummary("Revoke an invitation of the dog that has not been used (owners only). Idempotent.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var tokens = routes.MapGroup("/dog-invitations/{token}").WithTags("Dogs");

        tokens.MapGet("", GetInvitation)
            .WithName("getDogInvitation")
            .WithSummary("Look at an invitation before accepting it: the dog, who invited, the role offered. Gives no right on the dog.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status410Gone);

        tokens.MapPost("/accept", AcceptInvitation)
            .WithName("acceptDogInvitation")
            .WithSummary("Accept an invitation: the signed-in user becomes one of the dog's humans. Returns the dog.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status410Gone);

        return routes;
    }

    private static async Task<Results<Created<CreatedDogInvitationResponse>, ProblemHttpResult>> CreateInvitation(
        Guid id,
        CreateDogInvitationRequest request,
        ClaimsPrincipal principal,
        DogoDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        // Trusted and Educator are planned for phase 2 (ADR 0042): not accepted yet.
        if (request.Role != DogMemberRole.Owner)
        {
            return Problems.Validation("role", "Only the Owner role can be offered.");
        }

        var (dog, problem) = await DogEndpoints.FindOwnedDogAsync(db, id, principal, cancellationToken);
        if (dog is null)
        {
            return problem!;
        }

        var token = DogInvitation.NewToken();
        var invitation = new DogInvitation
        {
            DogId = dog.Id,
            CreatedById = principal.GetUserId(),
            Role = request.Role,
            TokenHash = DogInvitation.Hash(token),
            ExpiresAt = TruncateToMicroseconds(timeProvider.GetUtcNow() + DogInvitation.Lifetime),
        };
        db.DogInvitations.Add(invitation);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created(
            (string?)null,
            new CreatedDogInvitationResponse(
                invitation.Id, dog.Id, invitation.Role, token, DogInvitation.LinkPrefix + token, invitation.ExpiresAt, invitation.CreatedAt));
    }

    private static async Task<Results<Ok<List<DogInvitationResponse>>, ProblemHttpResult>> ListInvitations(
        Guid id, ClaimsPrincipal principal, DogoDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var (dog, problem) = await DogEndpoints.FindOwnedDogAsync(db, id, principal, cancellationToken);
        if (dog is null)
        {
            return problem!;
        }

        var now = timeProvider.GetUtcNow();
        var invitations = await db.DogInvitations.AsNoTracking()
            .Include(i => i.CreatedBy)
            .Where(i => i.DogId == id && i.UsedAt == null && i.RevokedAt == null && i.ExpiresAt > now)
            .OrderBy(i => i.CreatedAt).ThenBy(i => i.Id)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(invitations.Select(DogInvitationResponse.From).ToList());
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RevokeInvitation(
        Guid id, Guid invitationId, ClaimsPrincipal principal, DogoDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var (dog, problem) = await DogEndpoints.FindOwnedDogAsync(db, id, principal, cancellationToken);
        if (dog is null)
        {
            return problem!;
        }

        // Conditional update: an acceptance running at the same time either used the invitation
        // first (409 below) or finds it revoked.
        var now = timeProvider.GetUtcNow();
        var revoked = await db.DogInvitations
            .Where(i => i.Id == invitationId && i.DogId == id && i.UsedAt == null && i.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.RevokedAt, now), cancellationToken);
        if (revoked == 1)
        {
            return TypedResults.NoContent();
        }

        var current = await db.DogInvitations.AsNoTracking()
            .Where(i => i.Id == invitationId && i.DogId == id)
            .Select(i => new { i.UsedAt })
            .SingleOrDefaultAsync(cancellationToken);
        return current switch
        {
            null => NotFound(),
            { UsedAt: not null } => AlreadyUsed(),
            _ => TypedResults.NoContent(),
        };
    }

    private static async Task<Results<Ok<DogInvitationPreviewResponse>, ProblemHttpResult>> GetInvitation(
        string token, DogoDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var invitation = await FindByTokenAsync(db, token, cancellationToken);
        if (CheckUsable(invitation, timeProvider.GetUtcNow()) is { } problem)
        {
            return problem;
        }

        var dog = await db.Dogs.AsNoTracking().WithMembers().SingleAsync(d => d.Id == invitation!.DogId, cancellationToken);
        var invitedBy = invitation!.CreatedById is { } createdById
            ? await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == createdById, cancellationToken)
            : null;
        return TypedResults.Ok(new DogInvitationPreviewResponse(
            DogResponse.From(dog),
            invitedBy is null ? null : UserSummaryResponse.From(invitedBy),
            invitation.Role,
            invitation.ExpiresAt));
    }

    private static async Task<Results<Ok<DogResponse>, ProblemHttpResult>> AcceptInvitation(
        string token, ClaimsPrincipal principal, DogoDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var userId = principal.GetUserId();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Lock order (ADR 0037, 0042): the account (limit of dogs), then the dog ("at least one
        // owner" and concurrent acceptances), then the invitation itself. Deleting the dog locks
        // the dog before its invitations go by cascade, so the invitation is never locked first.
        await DogEndpoints.LockUserAsync(db, userId, cancellationToken);
        var found = await FindByTokenAsync(db, token, cancellationToken);
        if (found is null || found.RevokedAt is not null)
        {
            return NotFound();
        }

        var dog = await DogEndpoints.LockDogAsync(db, found.DogId, cancellationToken);
        var invitation = dog is null
            ? null
            : (await db.DogInvitations.FromSql($"SELECT * FROM dog_invitations WHERE id = {found.Id} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault();
        var now = timeProvider.GetUtcNow();
        if (CheckUsable(invitation, now) is { } problem)
        {
            return problem;
        }

        // Spec §4.1, in the order of its table, after the three checks of the invitation itself.
        if (await db.DogMembers.AnyAsync(m => m.DogId == dog!.Id && m.UserId == userId, cancellationToken))
        {
            return Problems.Conflict(ErrorCodes.DogAlreadyOwner, "You are already one of this dog's humans.");
        }

        if (await DogEndpoints.HasReachedLimitAsync(db, userId, cancellationToken))
        {
            return DogEndpoints.LimitReached();
        }

        db.DogMembers.Add(new DogMember { DogId = dog!.Id, UserId = userId, Role = invitation!.Role });
        invitation.UsedAt = now;
        invitation.UsedById = userId;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var accepted = await db.Dogs.AsNoTracking().WithMembers().SingleAsync(d => d.Id == dog.Id, cancellationToken);
        return TypedResults.Ok(DogResponse.From(accepted));
    }

    /// <summary>The invitation whose token this is, read without tracking; null for a malformed or unknown token.</summary>
    private static async Task<DogInvitation?> FindByTokenAsync(DogoDbContext db, string token, CancellationToken cancellationToken)
    {
        if (!DogInvitation.IsWellFormed(token))
        {
            return null;
        }

        var hash = DogInvitation.Hash(token);
        return await db.DogInvitations.AsNoTracking().SingleOrDefaultAsync(i => i.TokenHash == hash, cancellationToken);
    }

    /// <summary>The first three rules of spec §4.1, in order: exists and not revoked, not used, not expired.</summary>
    private static ProblemHttpResult? CheckUsable(DogInvitation? invitation, DateTimeOffset now) => invitation switch
    {
        null or { RevokedAt: not null } => NotFound(),
        { UsedAt: not null } => AlreadyUsed(),
        _ when invitation.ExpiresAt <= now => Problems.Create(StatusCodes.Status410Gone, ErrorCodes.InvitationExpired, "The invitation has expired."),
        _ => null,
    };

    private static ProblemHttpResult NotFound() => Problems.NotFound(ErrorCodes.InvitationNotFound, "The invitation does not exist or was revoked.");

    private static ProblemHttpResult AlreadyUsed() => Problems.Conflict(ErrorCodes.InvitationAlreadyUsed, "The invitation has already been used.");

    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMicrosecond)).ToUniversalTime();
}
