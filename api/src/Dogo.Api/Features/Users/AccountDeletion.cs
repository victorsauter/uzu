using Dogo.Api.Features.Dogs;
using Dogo.Api.Features.Notifications;
using Dogo.Api.Features.Organizations;
using Dogo.Api.Features.Walks;
using Dogo.Api.Infrastructure.Persistence;
using Dogo.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Dogo.Api.Features.Users;

/// <summary>
/// Deletes an account exactly as spec §4.8 says, in one transaction (ADR 0023): upcoming walks
/// cancelled, personal data deleted, organisations left without an owner closed, messages,
/// reports and walks kept without their author. Notifications and photo files follow the commit.
/// </summary>
public sealed partial class AccountDeletion(
    DogoDbContext db,
    IPhotoStorage photos,
    NotificationQueue notifications,
    TimeProvider timeProvider,
    ILogger<AccountDeletion> logger)
{
    /// <summary>Deletes the account; returns false when it does not exist.</summary>
    public async Task<bool> DeleteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // The user row lock serialises a deletion with the user's other writes that take it.
        var user = (await db.Users.FromSql($"SELECT * FROM users WHERE id = {userId} FOR UPDATE").ToListAsync(cancellationToken))
            .SingleOrDefault();
        if (user is null)
        {
            return false;
        }

        // The user's dogs are locked next (lock order: account, dogs, walks; ADR 0037, 0042), by
        // increasing id: a co-owner leaving a shared dog at the same time either left first (and
        // this user is then its last owner) or waits and finds this user gone. Only the dogs this
        // user was the last owner of are deleted; the others stay with their other humans.
        var linkedDogIds = await db.DogMembers.Where(m => m.UserId == userId).Select(m => m.DogId).ToListAsync(cancellationToken);
        var lockedDogIds = linkedDogIds.Order().ToArray();
        await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM dogs WHERE id = ANY({lockedDogIds}) ORDER BY id FOR UPDATE")
            .ToListAsync(cancellationToken);
        var lastOwnedDogs = await db.Dogs
            .Where(d => lockedDogIds.Contains(d.Id)
                && !d.Members.Any(m => m.UserId != userId && m.Role == DogMemberRole.Owner))
            .Select(d => new { d.Id, d.PhotoFileName })
            .ToListAsync(cancellationToken);

        // Upcoming walks are cancelled, as the organizer would (shared with the suspension).
        var cancelledWalks = await db.CancelUpcomingWalksAsync(userId, now, cancellationToken);

        // Every walk they organised is kept, without organizer, for its participants' history.
        // Their series stop (the series rows go; walks keep no series).
        await db.Walks.Where(w => w.OrganizerId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.OrganizerId, (Guid?)null), cancellationToken);
        await db.WalkSeries.Where(s => s.OrganizerId == userId).ExecuteDeleteAsync(cancellationToken);

        // Registrations (which frees their places), links to dogs, the dogs they were the last
        // owner of (their upcoming registrations by anyone are cancelled, spec §4.1), devices,
        // preferences and sessions. Dog invitations they created or accepted stay with the dog,
        // without author (spec §4.8).
        await db.Registrations.Where(r => r.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.DogMembers.Where(m => m.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await DogEndpoints.DeleteLockedDogsAsync(db, lastOwnedDogs.Select(d => d.Id).ToList(), now, cancellationToken);
        await db.DogInvitations.Where(i => i.CreatedById == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.CreatedById, (Guid?)null), cancellationToken);
        await db.DogInvitations.Where(i => i.UsedById == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.UsedById, (Guid?)null), cancellationToken);
        await db.Devices.Where(d => d.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.NotificationPreferences.Where(p => p.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.RefreshTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(cancellationToken);

        // Organisations whose only owner they were are closed, then their memberships go.
        var closedCount = await db.Organizations
            .Where(o => o.Status != OrganizationStatus.Closed
                && o.Memberships.Any(m => m.UserId == userId && m.Role == MembershipRole.Owner && m.Status == MembershipStatus.Accepted)
                && !o.Memberships.Any(m => m.UserId != userId && m.Role == MembershipRole.Owner && m.Status == MembershipStatus.Accepted))
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrganizationStatus.Closed), cancellationToken);
        await db.Memberships.Where(m => m.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Organizations.Where(o => o.CreatedById == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.CreatedById, (Guid?)null), cancellationToken);

        // Blocks in both directions.
        await db.Blocks.Where(b => b.BlockerId == userId || b.BlockedId == userId).ExecuteDeleteAsync(cancellationToken);

        // Messages and reports are kept without author; reports about them lose that target.
        await db.Messages.Where(m => m.AuthorId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.AuthorId, (Guid?)null), cancellationToken);
        await db.Reports.Where(r => r.ReporterId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ReporterId, (Guid?)null), cancellationToken);
        await db.Reports.Where(r => r.TargetUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.TargetUserId, (Guid?)null), cancellationToken);

        await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        LogDeleted(userId, cancelledWalks.Count, closedCount);

        // After the commit: photo files, then the registrants of the cancelled walks (spec §4.8).
        foreach (var fileName in lastOwnedDogs.Select(d => d.PhotoFileName).Append(user.PhotoFileName).OfType<string>())
        {
            await photos.DeleteAsync(fileName, cancellationToken);
        }

        foreach (var notification in cancelledWalks)
        {
            notifications.Enqueue(notification);
        }

        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Account {UserId} deleted: {WalkCount} upcoming walks cancelled, {OrganizationCount} organisations closed")]
    private partial void LogDeleted(Guid userId, int walkCount, int organizationCount);
}
