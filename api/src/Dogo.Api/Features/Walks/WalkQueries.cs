using Dogo.Api.Features.Dogs;
using Dogo.Api.Features.Users;
using Dogo.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Dogo.Api.Features.Walks;

/// <summary>A walk with what every walk response shows next to it, as seen by one viewer.</summary>
/// <param name="Organizer">Null once the organizer's account has been deleted.</param>
/// <param name="SeriesActive">The walk belongs to a weekly series that has not been stopped.</param>
/// <param name="OrganizationName">Name of the organisation of a professional walk.</param>
/// <param name="HasBlockedParticipant">The viewer blocked the organizer or the owner of an accepted dog (spec §4.7).</param>
internal sealed record WalkRow(
    Walk Walk,
    User? Organizer,
    int AcceptedDogsCount,
    bool SeriesActive,
    string? OrganizationName,
    bool HasBlockedParticipant)
{
    public GeoPoint Location => new() { Latitude = Walk.Location.Y, Longitude = Walk.Location.X };
}

/// <summary>
/// Walk response fields added by later lots (series, organisations, blocks). They are part of
/// the contract from Lot 2 so that the mobile app is written once; this is the single place to fill them.
/// </summary>
internal sealed record LaterLotFields(
    bool IsProfessional,
    WalkOrganizationResponse? Organization,
    bool RepeatsWeekly,
    bool HasBlockedParticipant)
{
    public static LaterLotFields For(WalkRow row)
        => new(
            IsProfessional: row.Walk.OrganizationId is not null,
            Organization: row.Walk.OrganizationId is { } organizationId && row.OrganizationName is { } name
                ? new WalkOrganizationResponse(organizationId, name)
                : null,
            RepeatsWeekly: row.SeriesActive,
            HasBlockedParticipant: row.HasBlockedParticipant);
}

internal static class WalkQueries
{
    /// <summary>
    /// Projects walks to <see cref="WalkRow"/> in one SQL query: organizer, accepted dogs count,
    /// series, organisation, and whether <paramref name="viewerId"/> blocked the organizer or the
    /// owner of an accepted dog (a subquery on <c>blocks</c>, never computed in memory).
    /// </summary>
    public static IQueryable<WalkRow> ToRows(this IQueryable<Walk> walks, DogoDbContext db, Guid viewerId) =>
        walks.Select(w => new WalkRow(
            w,
            w.Organizer,
            w.Registrations.Count(r => r.Status == RegistrationStatus.Accepted),
            w.Series != null && w.Series.Active,
            w.Organization != null ? w.Organization.Name : null,
            db.Blocks.Any(b => b.BlockerId == viewerId
                && (b.BlockedId == w.OrganizerId
                    || w.Registrations.Any(r => r.Status == RegistrationStatus.Accepted && r.UserId == b.BlockedId)))));

    public static Task<int> CountAcceptedAsync(this DogoDbContext db, Guid walkId, CancellationToken cancellationToken) =>
        db.Registrations.CountAsync(r => r.WalkId == walkId && r.Status == RegistrationStatus.Accepted, cancellationToken);

    /// <summary>
    /// Loads a walk for writing and locks its row (<c>SELECT … FOR UPDATE</c>) until the
    /// current transaction ends. Every write that depends on the walk's capacity or on its
    /// accepted registrations takes this lock first, so concurrent requests are serialised.
    /// </summary>
    public static async Task<Walk?> LockWalkAsync(this DogoDbContext db, Guid id, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A walk can only be locked inside a transaction.");
        }

        var walks = await db.Walks.FromSql($"SELECT * FROM walks WHERE id = {id} FOR UPDATE").ToListAsync(cancellationToken);
        return walks.SingleOrDefault();
    }

    /// <summary>Builds the walk page as seen by <paramref name="viewerId"/>; null if the walk does not exist.</summary>
    public static async Task<WalkDetailResponse?> LoadDetailAsync(this DogoDbContext db, Guid id, Guid viewerId, CancellationToken cancellationToken)
    {
        var row = await db.Walks.AsNoTracking().Where(w => w.Id == id).ToRows(db, viewerId).SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        // Pending registrations are visible to the organizer, to their author and to the owners of
        // the dog (spec §4.3, ADR 0042).
        var isOrganizer = row.Walk.OrganizerId == viewerId;
        var registrations = await db.Registrations.AsNoTracking()
            .Where(r => r.WalkId == id
                && (r.Status == RegistrationStatus.Accepted
                    || r.UserId == viewerId
                    || (isOrganizer && r.Status == RegistrationStatus.Pending)
                    || (r.Status == RegistrationStatus.Pending
                        && r.Dog!.Members.Any(m => m.UserId == viewerId && m.Role == DogMemberRole.Owner))))
            .Include(r => r.Dog).ThenInclude(d => d!.Members).ThenInclude(m => m.User)
            .Include(r => r.User)
            .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        // Registrations of a deleted dog (only possible on past walks) have no dog to show.
        var participants = registrations
            .Where(r => r.Status == RegistrationStatus.Accepted && r.Dog is not null)
            .Select(ParticipantResponse.From)
            .ToList();

        // Pending requests are visible only to the organizer and to their author (spec §4.3);
        // the author sees theirs in myRegistrations.
        var pending = isOrganizer
            ? registrations.Where(r => r.Status == RegistrationStatus.Pending && r.Dog is not null).Select(ParticipantResponse.From).ToList()
            : [];

        var mine = registrations.Where(r => r.UserId == viewerId).Select(MyRegistrationResponse.From).ToList();

        // Active registrations of the viewer's dogs, whoever made them: « Déjà inscrit par … ».
        var myDogs = registrations
            .Where(r => r.IsActive && r.Dog is not null
                && r.Dog.Members.Any(m => m.UserId == viewerId && m.Role == DogMemberRole.Owner))
            .Select(DogRegistrationResponse.From)
            .ToList();

        return WalkDetailResponse.From(row, LaterLotFields.For(row), isOrganizer, participants, mine, pending, myDogs);
    }
}
