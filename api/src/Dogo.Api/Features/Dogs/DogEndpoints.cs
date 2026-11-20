using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Dogo.Api.Common;
using Dogo.Api.Features.Walks;
using Dogo.Api.Infrastructure.Persistence;
using Dogo.Api.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Dogo.Api.Features.Dogs;

public static class DogEndpoints
{
    public const int DefaultListLimit = 50;
    public const int MaxListLimit = 100;

    public static IEndpointRouteBuilder MapDogEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("").WithTags("Dogs");

        group.MapGet("/me/dogs", ListMyDogs)
            .WithName("listMyDogs")
            .WithSummary("List the dogs the signed-in user is an owner of, each with all its humans.")
            .ProducesValidationProblem();

        group.MapPost("/dogs", CreateDog)
            .WithName("createDog")
            .WithSummary("Create a dog; the signed-in user becomes its owner (20 dogs at most per human, shared dogs included).")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/dogs/{id:guid}", GetDog)
            .WithName("getDog")
            .WithSummary("Get a dog with its humans.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/dogs/{id:guid}", UpdateDog)
            .WithName("updateDog")
            .WithSummary("Update a dog (any of its owners). The size cannot change while the dog is registered to an upcoming walk that does not allow the new size.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/dogs/{id:guid}", DeleteDog)
            .WithName("deleteDog")
            .WithSummary("Delete a dog. Reserved to its last owner: 409 dog.has_other_owners while another owner remains.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/dogs/{id:guid}/members/me", LeaveDog)
            .WithName("leaveDog")
            .WithSummary("Stop being one of the dog's humans. 409 dog.last_owner for its last owner, who can only delete it. Registrations already made are kept.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return routes;
    }

    private static async Task<Ok<List<DogResponse>>> ListMyDogs(
        ClaimsPrincipal principal,
        DogoDbContext db,
        CancellationToken cancellationToken,
        [Range(1, MaxListLimit)] int limit = DefaultListLimit)
    {
        var userId = principal.GetUserId();
        var dogs = await db.Dogs.AsNoTracking()
            .WithMembers()
            .OwnedBy(userId)
            .OrderForDisplay()
            .Take(limit)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(dogs.Select(DogResponse.From).ToList());
    }

    private static async Task<Results<Created<DogResponse>, ProblemHttpResult>> CreateDog(
        CreateDogRequest request, ClaimsPrincipal principal, DogoDbContext db, CancellationToken cancellationToken)
    {
        // The account row is locked so that concurrent creations and invitation acceptances cannot
        // pass the limit together (ADR 0037, 0042).
        var ownerId = principal.GetUserId();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockUserAsync(db, ownerId, cancellationToken);
        if (await HasReachedLimitAsync(db, ownerId, cancellationToken))
        {
            return LimitReached();
        }

        var owner = await db.Users.SingleAsync(u => u.Id == ownerId, cancellationToken);
        var dog = new Dog
        {
            Name = request.Name.Trim(),
            Size = request.Size,
            BirthDate = request.BirthDate,
            Breed = NullIfBlank(request.Breed),
            Sociability = request.Sociability,
            Reactivity = request.Reactivity,
            LeashHabit = request.LeashHabit,
            Description = request.Description?.Trim() ?? "",
        };
        dog.Members.Add(new DogMember { Dog = dog, DogId = dog.Id, User = owner, UserId = owner.Id, Role = DogMemberRole.Owner });
        db.Dogs.Add(dog);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Created($"/api/v1/dogs/{dog.Id}", DogResponse.From(dog));
    }

    private static async Task<Results<Ok<DogResponse>, ProblemHttpResult>> GetDog(
        Guid id, DogoDbContext db, CancellationToken cancellationToken)
    {
        var dog = await db.Dogs.AsNoTracking().WithMembers().SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        return dog is null ? DogNotFound() : TypedResults.Ok(DogResponse.From(dog));
    }

    private static async Task<Results<Ok<DogResponse>, ProblemHttpResult>> UpdateDog(
        Guid id,
        UpdateDogRequest request,
        ClaimsPrincipal principal,
        DogoDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        // The dog row is locked: a registration of this dog running at the same time (which
        // share-locks it) either finishes before the check below, or reads the new size.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var (dog, problem) = await FindOwnedDogAsync(db, id, principal, cancellationToken, forUpdate: true);
        if (dog is null)
        {
            return problem!;
        }

        // A dog cannot take a size that one of the walks it is registered to does not allow.
        var now = timeProvider.GetUtcNow();
        if (request.Size is { } newSize && newSize != dog.Size
            && await db.Registrations.AnyAsync(
                r => r.DogId == id
                    && (r.Status == RegistrationStatus.Pending || r.Status == RegistrationStatus.Accepted)
                    && r.Walk.Status == WalkStatus.Scheduled
                    && r.Walk.StartsAt > now
                    && !r.Walk.AllowedSizes.Contains(newSize),
                cancellationToken))
        {
            return Problems.Conflict(ErrorCodes.DogSizeInUse, "The dog is registered to an upcoming walk that does not allow this size.");
        }

        if (request.Name is not null)
        {
            dog.Name = request.Name.Trim();
        }

        if (request.Size is { } size)
        {
            dog.Size = size;
        }

        if (request.BirthDateSpecified)
        {
            dog.BirthDate = request.BirthDate;
        }

        if (request.BreedSpecified)
        {
            dog.Breed = NullIfBlank(request.Breed);
        }

        if (request.Sociability is { } sociability)
        {
            dog.Sociability = sociability;
        }

        if (request.Reactivity is { } reactivity)
        {
            dog.Reactivity = reactivity;
        }

        if (request.LeashHabit is { } leashHabit)
        {
            dog.LeashHabit = leashHabit;
        }

        if (request.Description is not null)
        {
            dog.Description = request.Description.Trim();
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Ok(DogResponse.From(dog));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteDog(
        Guid id,
        ClaimsPrincipal principal,
        DogoDbContext db,
        IPhotoStorage photos,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        // The dog row is locked first (ADR 0037): a registration of this dog running at the same
        // time share-locks it, so it either commits before the update below, which then cancels
        // it, or waits and finds no dog. An acceptance racing the update only accepts a
        // registration still pending, and the update below cancels an accepted one. The lock also
        // serialises the deletion with owners leaving or joining (ADR 0042).
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var (dog, problem) = await FindOwnedDogAsync(db, id, principal, cancellationToken, forUpdate: true);
        if (dog is null)
        {
            return problem!;
        }

        // Spec §4.1: only the last owner deletes the dog; the others can only leave it.
        if (OwnerCount(dog) > 1)
        {
            return Problems.Conflict(ErrorCodes.DogHasOtherOwners, "Another owner remains: leave the dog instead of deleting it.");
        }

        await DeleteLockedDogsAsync(db, [id], timeProvider.GetUtcNow(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (dog.PhotoFileName is not null)
        {
            await photos.DeleteAsync(dog.PhotoFileName, cancellationToken);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> LeaveDog(
        Guid id, ClaimsPrincipal principal, DogoDbContext db, CancellationToken cancellationToken)
    {
        // The dog row is locked (ADR 0042): two owners leaving at the same time run one after the
        // other, and the second one sees that it is now the last owner.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var (dog, problem) = await FindOwnedDogAsync(db, id, principal, cancellationToken, forUpdate: true);
        if (dog is null)
        {
            return problem!;
        }

        if (OwnerCount(dog) <= 1)
        {
            return Problems.Conflict(ErrorCodes.DogLastOwner, "The last owner cannot leave the dog; it can only be deleted.");
        }

        // Registrations already made by this human for the dog are kept (spec §4.1).
        var userId = principal.GetUserId();
        await db.DogMembers.Where(m => m.DogId == id && m.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Deletes dogs whose rows the caller has locked, with what spec §4.1 says: their upcoming
    /// registrations (whoever made them) are cancelled, which frees their places; registrations of
    /// past walks are kept as history (the foreign key sets their dog to null); links and
    /// invitations go by cascade. Photo files are the caller's business, after the commit.
    /// </summary>
    internal static async Task DeleteLockedDogsAsync(DogoDbContext db, IReadOnlyCollection<Guid> dogIds, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (dogIds.Count == 0)
        {
            return;
        }

        await db.Registrations
            .Where(r => r.DogId != null && dogIds.Contains(r.DogId.Value)
                && (r.Status == RegistrationStatus.Pending || r.Status == RegistrationStatus.Accepted)
                && r.Walk.StartsAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RegistrationStatus.Cancelled), cancellationToken);
        await db.Dogs.Where(d => dogIds.Contains(d.Id)).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Loads a dog, with its humans, for modification by the signed-in user: 404 <c>dog.not_found</c>
    /// when it does not exist, 403 <c>dog.forbidden</c> when the user is not one of its owners (spec §4.1).
    /// </summary>
    /// <param name="forUpdate">Locks the dog row (<c>SELECT … FOR UPDATE</c>) until the current transaction ends.</param>
    internal static async Task<(Dog? Dog, ProblemHttpResult? Problem)> FindOwnedDogAsync(
        DogoDbContext db, Guid id, ClaimsPrincipal principal, CancellationToken cancellationToken, bool forUpdate = false)
    {
        var dog = forUpdate
            ? await LockDogAsync(db, id, cancellationToken)
            : await db.Dogs.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (dog is null)
        {
            return (null, DogNotFound());
        }

        // Read after the lock: these are the humans committed when the lock was obtained.
        await db.Entry(dog).Collection(d => d.Members).Query().Include(m => m.User).LoadAsync(cancellationToken);
        var userId = principal.GetUserId();
        if (!dog.Members.Any(m => m.UserId == userId && m.Role == DogMemberRole.Owner))
        {
            return (null, Forbidden());
        }

        return (dog, null);
    }

    /// <summary>Loads a dog and locks its row (<c>SELECT … FOR UPDATE</c>) until the current transaction ends.</summary>
    internal static async Task<Dog?> LockDogAsync(DogoDbContext db, Guid id, CancellationToken cancellationToken) =>
        (await db.Dogs.FromSql($"SELECT * FROM dogs WHERE id = {id} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault();

    /// <summary>
    /// Locks the account row until the current transaction ends: creating a dog and accepting an
    /// invitation take it so that the limit of dogs per human holds (ADR 0037, 0042). Taken before
    /// any dog lock (lock order: account, dogs, walks).
    /// </summary>
    internal static Task LockUserAsync(DogoDbContext db, Guid userId, CancellationToken cancellationToken) =>
        db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM users WHERE id = {userId} FOR UPDATE").ToListAsync(cancellationToken);

    /// <summary>Whether the user already has <see cref="Dog.MaxPerOwner"/> dogs, counted on their links (shared dogs included).</summary>
    internal static async Task<bool> HasReachedLimitAsync(DogoDbContext db, Guid userId, CancellationToken cancellationToken) =>
        await db.DogMembers.CountAsync(m => m.UserId == userId, cancellationToken) >= Dog.MaxPerOwner;

    internal static ProblemHttpResult LimitReached() =>
        Problems.Conflict(ErrorCodes.DogLimitReached, $"A human cannot have more than {Dog.MaxPerOwner} dogs.");

    internal static ProblemHttpResult Forbidden() =>
        Problems.Forbidden(ErrorCodes.DogForbidden, "Only an owner of this dog can do this.");

    internal static ProblemHttpResult DogNotFound() => Problems.NotFound(ErrorCodes.DogNotFound, "The dog does not exist.");

    private static int OwnerCount(Dog dog) => dog.Members.Count(m => m.Role == DogMemberRole.Owner);

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
