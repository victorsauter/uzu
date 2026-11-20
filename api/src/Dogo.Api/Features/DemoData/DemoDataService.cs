using System.Diagnostics;
using System.Linq.Expressions;
using Dogo.Api.Features.Organizations;
using Dogo.Api.Features.Photos;
using Dogo.Api.Features.Users;
using Dogo.Api.Features.Walks;
using Dogo.Api.Infrastructure.Persistence;
using Dogo.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Dogo.Api.Features.DemoData;

/// <summary>
/// Generates, describes and removes the demo data set (ADR 0027). Demo accounts are ordinary
/// <c>Dev</c> accounts whose subject starts with <see cref="DemoDataBuilder.SubjectPrefix"/>;
/// nothing that belongs to another account is ever deleted or changed, apart from what removing
/// any account implies (their blocks of a demo account, their registrations' place in a demo walk).
/// </summary>
public sealed partial class DemoDataService(
    DogoDbContext db,
    IPhotoStorage storage,
    DemoPhotoFetcher photoFetcher,
    TimeProvider timeProvider,
    ILogger<DemoDataService> logger)
{
    /// <summary>Transaction-level advisory lock key: two generations or removals never interleave.</summary>
    private const long LockKey = 0x646f676f_64656d6f; // "dogodemo"

    private static readonly Expression<Func<User, bool>> IsDemoUser = u =>
        u.Provider == IdentityProvider.Dev && u.ProviderSubject.StartsWith(DemoDataBuilder.SubjectPrefix);

    /// <summary>Removes the previous demo data, then creates the data set again, in one transaction.</summary>
    public async Task<DemoDataGenerationResponse> GenerateAsync(bool withPhotos, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var set = DemoDataBuilder.Build(timeProvider.GetUtcNow());

        // Photos first, outside the transaction: downloads may take a while.
        var photos = withPhotos ? await photoFetcher.FetchAsync(set.Photos, cancellationToken) : new DemoPhotoResult([], 0);

        IReadOnlyList<string> previousFiles;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await LockAsync(cancellationToken);
            previousFiles = await RemoveAllAsync(cancellationToken);

            db.AddRange(set.Users);
            db.AddRange(set.Dogs);
            db.AddRange(set.DogMembers);
            db.AddRange(set.Organizations);
            db.AddRange(set.Memberships);
            db.AddRange(set.Series);
            db.AddRange(set.Walks);
            db.AddRange(set.Registrations);
            db.AddRange(set.Messages);
            db.AddRange(set.Blocks);
            db.AddRange(set.Reports);
            await db.SaveChangesAsync(cancellationToken);

            // Inserts are stamped with the current time; a second save back-dates them.
            foreach (var (entity, createdAt) in set.CreatedAt)
            {
                entity.CreatedAt = createdAt;
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await DeleteFilesAsync(photos.StoredFiles);
            throw;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }

        await DeleteFilesAsync(previousFiles);
        var counts = await CountAsync(cancellationToken);
        LogGenerated(counts.Owners + counts.Educators, counts.Walks, photos.StoredFiles.Count, photos.Failed, stopwatch.ElapsedMilliseconds);
        return new DemoDataGenerationResponse(counts, withPhotos, photos.StoredFiles.Count, photos.Failed);
    }

    /// <summary>Removes every demo account and what belongs to it, photo files included.</summary>
    public async Task RemoveAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> files;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await LockAsync(cancellationToken);
            files = await RemoveAllAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await DeleteFilesAsync(files);
        LogRemoved(files.Count);
    }

    public async Task<DemoDataStatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        var counts = await CountAsync(cancellationToken);
        var users = await db.Users.AsNoTracking().Where(IsDemoUser)
            .OrderBy(u => u.DisplayName)
            .Select(u => new
            {
                u.Id,
                u.ProviderSubject,
                u.DisplayName,
                u.PhotoFileName,
                Dogs = u.DogLinks.OrderBy(m => m.Dog.CreatedAt).ThenBy(m => m.Dog.Name).Select(m => m.Dog.Name).ToList(),
                Organization = db.Memberships
                    .Where(m => m.UserId == u.Id && m.Status == MembershipStatus.Accepted
                        && (m.Role == MembershipRole.Owner || m.Role == MembershipRole.Educator))
                    .OrderBy(m => m.Role).ThenBy(m => m.Organization.Name)
                    .Select(m => m.Organization.Name)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var accounts = users.Select(u => new DemoAccountResponse(
                u.ProviderSubject,
                u.DisplayName,
                PhotoUrls.For(u.PhotoFileName),
                u.Organization is null ? DemoAccountKind.Owner : DemoAccountKind.Educator,
                u.Dogs,
                u.Organization))
            .ToList();
        return new DemoDataStatusResponse(accounts.Count > 0, counts, accounts);
    }

    private async Task<DemoDataCounts> CountAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var demoUsers = db.Users.AsNoTracking().Where(IsDemoUser);
        var demoIds = demoUsers.Select(u => u.Id);
        var educators = await demoUsers.CountAsync(
            u => db.Memberships.Any(m => m.UserId == u.Id && m.Status == MembershipStatus.Accepted
                && (m.Role == MembershipRole.Owner || m.Role == MembershipRole.Educator)),
            cancellationToken);
        var walks = db.Walks.Where(w => w.OrganizerId != null && demoIds.Contains(w.OrganizerId.Value));
        var dogs = db.Dogs.Where(d => d.Members.Any(m => demoIds.Contains(m.UserId)));
        return new DemoDataCounts(
            Owners: await demoUsers.CountAsync(cancellationToken) - educators,
            Educators: educators,
            Dogs: await dogs.CountAsync(cancellationToken),
            Organizations: await db.Organizations.CountAsync(o => o.CreatedById != null && demoIds.Contains(o.CreatedById.Value), cancellationToken),
            Walks: await walks.CountAsync(cancellationToken),
            UpcomingWalks: await walks.CountAsync(w => w.StartsAt > now, cancellationToken),
            PastWalks: await walks.CountAsync(w => w.StartsAt <= now, cancellationToken),
            Registrations: await db.Registrations.CountAsync(r => demoIds.Contains(r.UserId), cancellationToken),
            Messages: await db.Messages.CountAsync(m => m.AuthorId != null && demoIds.Contains(m.AuthorId.Value), cancellationToken),
            Blocks: await db.Blocks.CountAsync(b => demoIds.Contains(b.BlockerId), cancellationToken),
            Reports: await db.Reports.CountAsync(r => r.ReporterId != null && demoIds.Contains(r.ReporterId.Value), cancellationToken),
            Photos: await demoUsers.CountAsync(u => u.PhotoFileName != null, cancellationToken)
                + await dogs.CountAsync(d => d.PhotoFileName != null, cancellationToken));
    }

    /// <summary>
    /// Deletes the demo accounts and what belongs to them, inside the caller's transaction, and
    /// returns their photo files to delete after the commit. What other accounts wrote stays:
    /// a demo walk with another person's registration or message is kept like the walks of a deleted
    /// account (spec §4.8: cancelled if upcoming, without organizer), and so is a demo organisation
    /// with another member or another person's walk.
    /// </summary>
    private async Task<IReadOnlyList<string>> RemoveAllAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var demoIds = await db.Users.Where(IsDemoUser).Select(u => u.Id).ToListAsync(cancellationToken);
        if (demoIds.Count == 0)
        {
            return [];
        }

        // Dogs whose humans are all demo accounts; a dog someone else also owns stays with them.
        var demoDogs = db.Dogs.Where(d => d.Members.Any(m => demoIds.Contains(m.UserId)) && !d.Members.Any(m => !demoIds.Contains(m.UserId)));
        var files = await db.Users.Where(u => demoIds.Contains(u.Id) && u.PhotoFileName != null).Select(u => u.PhotoFileName!)
            .Concat(demoDogs.Where(d => d.PhotoFileName != null).Select(d => d.PhotoFileName!))
            .ToListAsync(cancellationToken);

        // Walks: kept when someone else registered or wrote; deleted otherwise (with their registrations and thread).
        var demoWalks = db.Walks.Where(w => w.OrganizerId != null && demoIds.Contains(w.OrganizerId.Value));
        var keptWalkIds = await demoWalks
            .Where(w => db.Registrations.Any(r => r.WalkId == w.Id && !demoIds.Contains(r.UserId))
                || db.Messages.Any(m => m.WalkId == w.Id && m.AuthorId != null && !demoIds.Contains(m.AuthorId.Value)))
            .Select(w => w.Id)
            .ToListAsync(cancellationToken);
        await db.Walks.Where(w => keptWalkIds.Contains(w.Id) && w.Status == WalkStatus.Scheduled && w.StartsAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, WalkStatus.Cancelled), cancellationToken);
        await db.Walks.Where(w => keptWalkIds.Contains(w.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.OrganizerId, (Guid?)null), cancellationToken);
        await demoWalks.ExecuteDeleteAsync(cancellationToken);

        // What the demo accounts wrote or registered anywhere else.
        await db.Messages.Where(m => m.AuthorId != null && demoIds.Contains(m.AuthorId.Value)).ExecuteDeleteAsync(cancellationToken);
        await db.Registrations.Where(r => demoIds.Contains(r.UserId)).ExecuteDeleteAsync(cancellationToken);
        await db.Reports.Where(r => r.ReporterId != null && demoIds.Contains(r.ReporterId.Value)).ExecuteDeleteAsync(cancellationToken);

        // Organisations: kept (without creator, closed if no owner is left) when someone else is a
        // member or carries a walk there; deleted otherwise, with their memberships.
        var demoOrganizations = db.Organizations.Where(o => o.CreatedById != null && demoIds.Contains(o.CreatedById.Value));
        var keptOrganizationIds = await demoOrganizations
            .Where(o => o.Memberships.Any(m => !demoIds.Contains(m.UserId)) || db.Walks.Any(w => w.OrganizationId == o.Id))
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);
        await db.Memberships.Where(m => demoIds.Contains(m.UserId)).ExecuteDeleteAsync(cancellationToken);
        await db.Organizations.Where(o => keptOrganizationIds.Contains(o.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.CreatedById, (Guid?)null), cancellationToken);
        await db.Organizations
            .Where(o => keptOrganizationIds.Contains(o.Id)
                && !o.Memberships.Any(m => m.Role == MembershipRole.Owner && m.Status == MembershipStatus.Accepted))
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrganizationStatus.Closed), cancellationToken);
        await demoOrganizations.Where(o => !keptOrganizationIds.Contains(o.Id)).ExecuteDeleteAsync(cancellationToken);

        // Their dogs (with their links and invitations), then the accounts; their remaining links,
        // series, blocks (both ways), devices, preferences and sessions follow by cascade.
        await demoDogs.ExecuteDeleteAsync(cancellationToken);
        await db.Users.Where(u => demoIds.Contains(u.Id)).ExecuteDeleteAsync(cancellationToken);
        return files;
    }

    private Task LockAsync(CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({LockKey})", cancellationToken);

    private async Task DeleteFilesAsync(IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            await storage.DeleteAsync(file, CancellationToken.None);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo data generated: {AccountCount} accounts, {WalkCount} walks, {PhotoCount} photos stored, {FailedCount} skipped, in {ElapsedMilliseconds} ms")]
    private partial void LogGenerated(int accountCount, int walkCount, int photoCount, int failedCount, long elapsedMilliseconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo data removed ({FileCount} photo files)")]
    private partial void LogRemoved(int fileCount);
}
