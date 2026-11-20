using System.Net.Http.Json;
using Dogo.Api.Features.Dogs;
using Dogo.Api.Features.Messages;
using Dogo.Api.Features.Organizations;
using Dogo.Api.Features.Reports;
using Dogo.Api.Features.Users;
using Dogo.Api.Features.Walks;
using Dogo.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Dogo.Api.Tests.DemoData;

/// <summary>The generated data set respects every business rule of the specification, as if created through the API.</summary>
public sealed class DemoDataInvariantTests(ApiFixture fixture) : DemoDataTestBase(fixture)
{
    private static readonly RegistrationStatus[] Active = [RegistrationStatus.Pending, RegistrationStatus.Accepted];

    [Fact]
    public async Task Walks_and_registrations_respect_sizes_capacity_and_access()
    {
        await GenerateAsync();
        var now = DateTimeOffset.UtcNow;
        var (walks, registrations, dogs, members) = await WithDbAsync(async db => (
            await db.Walks.AsNoTracking().ToListAsync(Ct),
            await db.Registrations.AsNoTracking().ToListAsync(Ct),
            (await db.Dogs.AsNoTracking().ToListAsync(Ct)).ToDictionary(d => d.Id),
            (await db.DogMembers.AsNoTracking().Where(m => m.Role == DogMemberRole.Owner).Select(m => new { m.DogId, m.UserId }).ToListAsync(Ct))
                .Select(m => (m.DogId, m.UserId)).ToHashSet()));

        foreach (var walk in walks)
        {
            var mine = registrations.Where(r => r.WalkId == walk.Id).ToList();
            var accepted = mine.Where(r => r.Status == RegistrationStatus.Accepted).ToList();

            // The human of a registration is an owner of the dog, not necessarily the first (ADR 0042).
            Assert.InRange(accepted.Count, 0, walk.MaxDogs);
            Assert.All(mine, r => Assert.Contains((r.DogId!.Value, r.UserId), members));
            Assert.All(mine.Where(r => Active.Contains(r.Status)), r => Assert.Contains(dogs[r.DogId!.Value].Size, walk.AllowedSizes));
            Assert.Equal(mine.Count(r => Active.Contains(r.Status)), mine.Where(r => Active.Contains(r.Status)).Select(r => r.DogId).Distinct().Count());

            // Open walks accept at once; pending requests only on upcoming walks with approval.
            Assert.All(mine.Where(r => r.Status == RegistrationStatus.Pending), _ =>
            {
                Assert.Equal(WalkAccess.Approval, walk.Access);
                Assert.True(walk.StartsAt > now);
            });

            // Field limits of the API (spec §3, WalkRequests).
            Assert.InRange(walk.Title.Length, Walk.TitleMinLength, Walk.TitleMaxLength);
            Assert.InRange(walk.Description.Length, 0, Walk.DescriptionMaxLength);
            Assert.InRange(walk.Rules.Length, 0, Walk.RulesMaxLength);
            Assert.InRange(walk.LocationLabel.Length, 1, Walk.LocationLabelMaxLength);
            Assert.InRange(walk.DurationMinutes, Walk.MinDurationMinutes, Walk.MaxDurationMinutes);
            Assert.InRange(walk.MaxDogs, Walk.MinDogs, Walk.MaxDogsLimit);
            Assert.NotEmpty(walk.AllowedSizes);
            Assert.Equal(walk.AllowedSizes.Distinct().Order(), walk.AllowedSizes);
            Assert.InRange(DistanceKm(walk.Location.Y, walk.Location.X), 0, 15);
            Assert.True(walk.CreatedAt <= now && walk.CreatedAt < walk.StartsAt);
            Assert.All(mine, r => Assert.True(r.CreatedAt >= walk.CreatedAt && r.CreatedAt <= now && r.CreatedAt <= walk.StartsAt));
        }

        var upcoming = walks.Where(w => w.StartsAt > now).ToList();
        var past = walks.Where(w => w.StartsAt <= now).ToList();
        Assert.All(upcoming, w => Assert.True(w.StartsAt <= now.AddDays(22)));
        Assert.All(past, w => Assert.True(w.StartsAt >= now.AddDays(-43)));
        Assert.Contains(upcoming, w => w.StartsAt < now.AddDays(1));
        Assert.Contains(upcoming, w => w.Status == WalkStatus.Scheduled && registrations.Count(r => r.WalkId == w.Id && r.Status == RegistrationStatus.Accepted) == w.MaxDogs);
        Assert.Single(upcoming, w => w.Status == WalkStatus.Cancelled);
        Assert.Contains(upcoming, w => w.AllowedSizes.SequenceEqual([DogSize.Small]));
        Assert.Contains(upcoming, w => w.AllowedSizes.SequenceEqual([DogSize.Large]));
        Assert.Contains(upcoming, w => registrations.Any(r => r.WalkId == w.Id && r.Status == RegistrationStatus.Pending));
        Assert.Contains(walks, w => w.Access == WalkAccess.Open && w.OrganizationId is null);
        Assert.Contains(walks, w => w.Access == WalkAccess.Approval && w.OrganizationId is null);
    }

    [Fact]
    public async Task Professional_walks_belong_to_approved_organisations_and_are_for_members_only()
    {
        await GenerateAsync();
        var (walks, registrations, organizations, memberships) = await WithDbAsync(async db => (
            await db.Walks.AsNoTracking().ToListAsync(Ct),
            await db.Registrations.AsNoTracking().ToListAsync(Ct),
            (await db.Organizations.AsNoTracking().ToListAsync(Ct)).ToDictionary(o => o.Id),
            await db.Memberships.AsNoTracking().ToListAsync(Ct)));

        bool IsAccepted(Guid organizationId, Guid userId, params MembershipRole[] roles) => memberships.Any(m =>
            m.OrganizationId == organizationId && m.UserId == userId && m.Status == MembershipStatus.Accepted && (roles.Length == 0 || roles.Contains(m.Role)));

        var professional = walks.Where(w => w.OrganizationId is not null).ToList();
        Assert.NotEmpty(professional);
        foreach (var walk in professional)
        {
            var organizationId = walk.OrganizationId!.Value;
            Assert.Equal(OrganizationStatus.Approved, organizations[organizationId].Status);
            Assert.True(IsAccepted(organizationId, walk.OrganizerId!.Value, MembershipRole.Owner, MembershipRole.Educator));
            Assert.All(registrations.Where(r => r.WalkId == walk.Id && r.UserId != walk.OrganizerId && Active.Contains(r.Status)),
                r => Assert.True(IsAccepted(organizationId, r.UserId)));
        }

        foreach (var organization in organizations.Values)
        {
            var members = memberships.Where(m => m.OrganizationId == organization.Id).ToList();
            var owner = Assert.Single(members, m => m.Role == MembershipRole.Owner);
            Assert.Equal((organization.CreatedById, MembershipStatus.Accepted), (owner.UserId, owner.Status));
            Assert.Equal(members.Count(m => m.Status != MembershipStatus.Declined), members.Where(m => m.Status != MembershipStatus.Declined).Select(m => m.UserId).Distinct().Count());
            Assert.All(members.Where(m => m.Status != MembershipStatus.Accepted), m => Assert.Equal(MembershipRole.Member, m.Role));
            Assert.InRange(organization.Name.Length, Organization.NameMinLength, Organization.NameMaxLength);
            if (organization.Status == OrganizationStatus.Approved)
            {
                Assert.InRange(members.Count(m => m.Status == MembershipStatus.Accepted && m.Role == MembershipRole.Member), 5, 20);
                Assert.InRange(members.Count(m => m.Status == MembershipStatus.Pending), 1, 3);
            }
            else
            {
                Assert.DoesNotContain(walks, w => w.OrganizationId == organization.Id);
            }
        }

        Assert.Equal(4, organizations.Values.Count(o => o.Status == OrganizationStatus.Approved));
        Assert.Equal(1, organizations.Values.Count(o => o.Status == OrganizationStatus.Pending));
        Assert.All(organizations.Values.GroupBy(o => o.CreatedById), g => Assert.True(g.Count(o => o.Status == OrganizationStatus.Pending) <= Organization.MaxPendingPerUser));
    }

    [Fact]
    public async Task Series_threads_blocks_and_reports_are_consistent()
    {
        await GenerateAsync();
        var now = DateTimeOffset.UtcNow;
        var (walks, registrations, messages, blocks, reports, series) = await WithDbAsync(async db => (
            await db.Walks.AsNoTracking().ToListAsync(Ct),
            await db.Registrations.AsNoTracking().ToListAsync(Ct),
            await db.Messages.AsNoTracking().ToListAsync(Ct),
            await db.Blocks.AsNoTracking().ToListAsync(Ct),
            await db.Reports.AsNoTracking().ToListAsync(Ct),
            await db.WalkSeries.AsNoTracking().ToListAsync(Ct)));

        // Never more than one upcoming walk per series (spec §4.5), all by the series' organizer.
        foreach (var one in series)
        {
            var occurrences = walks.Where(w => w.SeriesId == one.Id).ToList();
            Assert.Equal(3, occurrences.Count);
            Assert.Single(occurrences, w => w.StartsAt > now);
            Assert.All(occurrences, w => Assert.Equal(one.OrganizerId, w.OrganizerId));
            Assert.True(one.Active);
        }

        // Threads: only the organizer and owners of accepted dogs write (spec §4.4), before now.
        foreach (var thread in messages.GroupBy(m => m.WalkId))
        {
            var walk = walks.Single(w => w.Id == thread.Key);
            var writers = registrations.Where(r => r.WalkId == walk.Id && r.Status == RegistrationStatus.Accepted).Select(r => r.UserId).Append(walk.OrganizerId!.Value).ToHashSet();
            Assert.InRange(thread.Count(), 3, 15);
            Assert.True(thread.Select(m => m.AuthorId).Distinct().Count() >= 2);
            Assert.All(thread, m =>
            {
                Assert.Contains(m.AuthorId!.Value, writers);
                Assert.InRange(m.Text.Length, 1, Message.TextMaxLength);
                Assert.DoesNotContain("{", m.Text, StringComparison.Ordinal);
                Assert.True(m.CreatedAt < now && m.CreatedAt > walk.CreatedAt);
            });
            Assert.Equal(thread.Count(), thread.Select(m => m.CreatedAt).Distinct().Count());
            Assert.NotEqual(WalkStatus.Cancelled, walk.Status);
        }

        Assert.Contains(messages, m => walks.Single(w => w.Id == m.WalkId).StartsAt > now);
        Assert.Contains(messages, m => walks.Single(w => w.Id == m.WalkId).StartsAt <= now && m.CreatedAt > walks.Single(w => w.Id == m.WalkId).StartsAt);

        // Blocked people never registered to the blocker's walks (spec §4.7).
        Assert.All(blocks, b => Assert.NotEqual(b.BlockerId, b.BlockedId));
        foreach (var block in blocks)
        {
            Assert.DoesNotContain(registrations, r => r.UserId == block.BlockedId && walks.Single(w => w.Id == r.WalkId).OrganizerId == block.BlockerId);
        }

        // The blocker sees the warning somewhere: a blocked person is accepted on someone else's upcoming walk.
        Assert.Contains(registrations, r => r.Status == RegistrationStatus.Accepted
            && blocks.Any(b => b.BlockedId == r.UserId && walks.Single(w => w.Id == r.WalkId) is var walk && walk.StartsAt > now && walk.OrganizerId != b.BlockerId));

        // Reports: one target at least, never oneself, the four types.
        Assert.Equal(Enum.GetValues<ReportType>().Order(), reports.Select(r => r.Type).Order());
        Assert.Equal(3, reports.Select(r => r.Status).Distinct().Count());
        Assert.All(reports, r =>
        {
            Assert.True(r.TargetUserId is not null || r.TargetDogId is not null || r.WalkId is not null);
            Assert.NotEqual(r.ReporterId, r.TargetUserId);
            Assert.InRange(r.Facts.Length, Report.FactsMinLength, Report.FactsMaxLength);
        });
    }

    [Fact]
    public async Task People_and_dogs_are_valid_profiles()
    {
        await GenerateAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (users, dogs, educators, links) = await WithDbAsync(async db => (
            await db.Users.AsNoTracking().ToListAsync(Ct),
            await db.Dogs.AsNoTracking().ToListAsync(Ct),
            await db.Memberships.Where(m => m.Status == MembershipStatus.Accepted && m.Role != MembershipRole.Member).Select(m => m.UserId).Distinct().ToListAsync(Ct),
            await db.DogMembers.AsNoTracking().ToListAsync(Ct)));

        // Every dog has at least one owner, and every link is an owner (the only role in V1.x).
        Assert.All(dogs, d => Assert.Contains(links, m => m.DogId == d.Id));
        Assert.All(links, m => Assert.Equal(DogMemberRole.Owner, m.Role));

        Assert.Equal(users.Count, users.Select(u => u.ProviderSubject).Distinct().Count());
        Assert.All(users, u =>
        {
            Assert.Equal(IdentityProvider.Dev, u.Provider);
            Assert.InRange(u.DisplayName.Length, 1, User.DisplayNameMaxLength);
            Assert.InRange(u.Bio.Length, 0, User.BioMaxLength);
            Assert.Null(u.Email);
            Assert.Equal(UserStatus.Active, u.Status);
            var count = links.Count(m => m.UserId == u.Id);
            Assert.InRange(count, educators.Contains(u.Id) ? 0 : 1, educators.Contains(u.Id) ? 2 : 3);
        });
        Assert.True(users.Count(u => u.Bio.Length > 0) > users.Count / 2);

        Assert.All(dogs, d =>
        {
            Assert.InRange(d.Name.Length, 1, Dog.NameMaxLength);
            Assert.InRange(d.Breed!.Length, 1, Dog.BreedMaxLength);
            Assert.InRange(d.Description.Length, 0, Dog.DescriptionMaxLength);
            Assert.True(d.BirthDate < today && d.BirthDate > today.AddYears(-16));
        });

        // Realistic behaviour: most dogs easygoing and not reactive, a few strongly reactive.
        Assert.True(dogs.Count(d => d.Sociability == Sociability.Easygoing) > dogs.Count * 0.6);
        Assert.True(dogs.Count(d => d.Reactivity == Reactivity.None) > dogs.Count * 0.6);
        Assert.InRange(dogs.Count(d => d.Reactivity == Reactivity.Strong), 2, 6);
        Assert.All(Enum.GetValues<DogSize>(), size => Assert.True(dogs.Count(d => d.Size == size) >= 15));
        Assert.Contains(dogs, d => d.BirthDate > today.AddYears(-1));
        Assert.Contains(dogs, d => d.BirthDate < today.AddYears(-11));
    }

    [Fact]
    public async Task A_shared_dog_is_registered_by_its_other_owner_and_its_first_owner_sees_it()
    {
        await GenerateAsync();
        var now = DateTimeOffset.UtcNow;
        var (links, registrations) = await WithDbAsync(async db => (
            await db.DogMembers.AsNoTracking().ToListAsync(Ct),
            await db.Registrations.AsNoTracking().Include(r => r.Walk)
                .Where(r => r.Status == RegistrationStatus.Accepted || r.Status == RegistrationStatus.Pending).ToListAsync(Ct)));

        // A dog with two humans, registered to an upcoming walk by the one who joined second.
        var shared = links.GroupBy(m => m.DogId).Where(g => g.Count() == 2).ToList();
        Assert.NotEmpty(shared);
        var (registration, firstOwnerId) = shared
            .SelectMany(g => registrations
                .Where(r => r.DogId == g.Key && r.Walk.StartsAt > now && r.UserId == g.OrderBy(m => m.CreatedAt).Last().UserId)
                .Select(r => (Registration: r, FirstOwnerId: g.OrderBy(m => m.CreatedAt).First().UserId)))
            .First();

        // Signed in as the first owner, the walk shows the registration and who made it.
        var subject = await WithDbAsync(db => db.Users.Where(u => u.Id == firstOwnerId).Select(u => u.ProviderSubject).SingleAsync(Ct));
        var client = await SignInAsDemoAsync(subject);
        var walk = await client.GetFromJsonAsync<WalkDetailResponse>($"/api/v1/walks/{registration.WalkId}", Json, Ct);
        var seen = Assert.Single(walk!.MyDogsRegistrations, r => r.DogId == registration.DogId);
        Assert.Equal((registration.Id, registration.UserId), (seen.Id, seen.RegisteredBy.Id));
        Assert.DoesNotContain(walk.MyRegistrations, r => r.Id == registration.Id);
    }

    /// <summary>Great-circle distance from Seppois-le-Haut, in kilometres.</summary>
    private static double DistanceKm(double latitude, double longitude)
    {
        static double Rad(double degrees) => degrees * Math.PI / 180;
        var dLat = Rad(latitude - SeppoisLatitude);
        var dLng = Rad(longitude - SeppoisLongitude);
        var h = (Math.Sin(dLat / 2) * Math.Sin(dLat / 2))
            + (Math.Cos(Rad(SeppoisLatitude)) * Math.Cos(Rad(latitude)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2));
        return 2 * 6371 * Math.Asin(Math.Sqrt(h));
    }
}
