using Dogo.Api.Common;
using Dogo.Api.Features.Organizations;
using Dogo.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Dogo.Api.Features.Walks;

/// <summary>
/// A registration request after the dogs and the walk have been locked: who registers which dogs.
/// <paramref name="OwnDogs"/> are the requested dogs that exist and belong to the user.
/// </summary>
internal sealed record RegistrationAttempt(Walk Walk, Guid UserId, IReadOnlyList<Guid> DogIds, IReadOnlyList<Dogs.Dog> OwnDogs, DateTimeOffset Now)
{
    /// <summary>The organizer's own dogs are accepted immediately, even on an <c>Approval</c> walk (spec §4.3).</summary>
    public RegistrationStatus InitialStatus =>
        Walk.Access == WalkAccess.Open || Walk.OrganizerId == UserId ? RegistrationStatus.Accepted : RegistrationStatus.Pending;
}

/// <summary>
/// The rules of spec §4.3 checked before creating registrations, in the order of its table; the
/// first failure is returned. Runs while the walk row is locked, so counts cannot change underneath.
/// </summary>
internal static class RegistrationRules
{
    public static async Task<ProblemHttpResult?> CheckAsync(
        DogoDbContext db, RegistrationAttempt attempt, CancellationToken cancellationToken)
    {
        var walk = attempt.Walk;

        if (!walk.IsOpen(attempt.Now))
        {
            return NotOpen();
        }

        // Spec §4.6: only an approved organisation carries walks. A walk of an organisation that was
        // closed or rejected afterwards is frozen: it stays as it is but takes no new registration.
        if (walk.OrganizationId is { } carrierId && !await IsOrganizationApprovedAsync(db, carrierId, cancellationToken))
        {
            return NotOpen();
        }

        // Someone else's dog is reported like a missing one (spec: 404).
        var dogs = attempt.OwnDogs;
        if (dogs.Count != attempt.DogIds.Count)
        {
            return Problems.NotFound(ErrorCodes.DogNotFound, "A dog does not exist or does not belong to you.");
        }

        if (dogs.Any(d => !walk.AllowedSizes.Contains(d.Size)))
        {
            return SizeNotAllowed();
        }

        var alreadyRegistered = await db.Registrations.AnyAsync(
            r => r.WalkId == walk.Id
                && r.DogId != null && attempt.DogIds.Contains(r.DogId.Value)
                && (r.Status == RegistrationStatus.Pending || r.Status == RegistrationStatus.Accepted),
            cancellationToken);
        if (alreadyRegistered)
        {
            return Duplicate();
        }

        // Only accepted dogs take a place. A pending request does not, but it is refused when
        // the walk could not take these dogs right now; acceptance checks again.
        // Spec §4.7: a user blocked by the organizer gets exactly the answer of a full walk, at the
        // same place in the order of the checks, so that the block cannot be told apart. Both
        // queries always run, so that the response time does not depend on the block either.
        var accepted = await db.CountAcceptedAsync(walk.Id, cancellationToken);
        var blocked = await db.Blocks.AnyAsync(b => b.BlockerId == walk.OrganizerId && b.BlockedId == attempt.UserId, cancellationToken);
        if (accepted + dogs.Count > walk.MaxDogs || blocked)
        {
            return Full();
        }

        // Spec §4.6: a professional walk is reserved to accepted members of its organisation
        // (any role). The organizer, an owner or educator, registers their own dogs freely.
        if (walk.OrganizationId is { } organizationId
            && walk.OrganizerId != attempt.UserId
            && !await IsAcceptedMemberAsync(db, organizationId, attempt.UserId, cancellationToken))
        {
            return MembersOnly();
        }

        return null;
    }

    /// <summary>
    /// Loads dogs and share-locks their rows (<c>FOR SHARE</c>) until the transaction ends: their
    /// size cannot change, nor can they be deleted or lose an owner, until the registration is
    /// written (ADR 0037, 0042). With <paramref name="ownerId"/>, only the dogs that user is an owner
    /// of (any of their owners registers them). Always called <b>before</b>
    /// <see cref="WalkQueries.LockWalkAsync"/>: deleting a dog locks the dog and then touches the
    /// walk rows (foreign key checks), so the opposite order could deadlock.
    /// </summary>
    public static Task<List<Dogs.Dog>> LockDogsAsync(DogoDbContext db, Guid[] dogIds, Guid? ownerId, CancellationToken cancellationToken) =>
        ownerId is { } owner
            ? db.Dogs.FromSql(
                    $"""
                    SELECT d.* FROM dogs d
                    WHERE d.id = ANY({dogIds})
                      AND EXISTS (SELECT 1 FROM dog_members m WHERE m.dog_id = d.id AND m.user_id = {owner} AND m.role = 'Owner')
                    FOR SHARE OF d
                    """)
                .AsNoTracking().ToListAsync(cancellationToken)
            : db.Dogs.FromSql($"SELECT * FROM dogs WHERE id = ANY({dogIds}) FOR SHARE").AsNoTracking().ToListAsync(cancellationToken);

    /// <summary>
    /// Whether the organisation is <c>Approved</c>. Its row is share-locked until the transaction
    /// ends, so that a concurrent closure or rejection waits for this registration, or is seen by it.
    /// </summary>
    public static async Task<bool> IsOrganizationApprovedAsync(DogoDbContext db, Guid organizationId, CancellationToken cancellationToken)
    {
        var statuses = await db.Database
            .SqlQuery<string>($"SELECT status AS \"Value\" FROM organizations WHERE id = {organizationId} FOR SHARE")
            .ToListAsync(cancellationToken);
        return statuses is [nameof(OrganizationStatus.Approved)];
    }

    /// <summary>
    /// Whether the user is an accepted member of the organisation (any role). The membership row is
    /// share-locked until the transaction ends: a member leaving at the same time waits for this
    /// registration and then cancels it, or has already left and is not seen.
    /// </summary>
    public static async Task<bool> IsAcceptedMemberAsync(DogoDbContext db, Guid organizationId, Guid userId, CancellationToken cancellationToken)
    {
        var memberships = await db.Database
            .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM memberships WHERE organization_id = {organizationId} AND user_id = {userId} AND status = 'Accepted' FOR SHARE")
            .ToListAsync(cancellationToken);
        return memberships.Count > 0;
    }

    public static ProblemHttpResult MembersOnly() =>
        Problems.Forbidden(ErrorCodes.WalkMembersOnly, "This walk is reserved to the members of its organisation.");

    public static ProblemHttpResult NotOpen() =>
        Problems.Conflict(ErrorCodes.WalkNotOpen, "The walk is cancelled or has already started.");

    public static ProblemHttpResult SizeNotAllowed() =>
        Problems.Conflict(ErrorCodes.WalkSizeNotAllowed, "The size of a dog is not allowed on this walk.");

    public static ProblemHttpResult Duplicate() =>
        Problems.Conflict(ErrorCodes.RegistrationDuplicate, "A dog is already registered to this walk.");

    public static ProblemHttpResult Full() =>
        Problems.Conflict(ErrorCodes.WalkFull, "There are not enough places left on this walk.");
}
