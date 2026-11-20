using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dogo.Api.Features.DemoData;
using Dogo.Api.Features.Users;
using Dogo.Api.Features.Walks;
using Dogo.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Dogo.Api.Tests.DemoData;

public sealed class DemoDataTests(ApiFixture fixture) : DemoDataTestBase(fixture)
{
    [Fact]
    public async Task Routes_do_not_exist_when_demo_data_is_disabled()
    {
        var signedIn = await SignInDevAsync("bob", "Bob");
        await using var factory = new DogoApiFactory(Fixture.ConnectionString, new Dictionary<string, string?> { ["DemoData:Enabled"] = "false" });
        var anonymous = factory.CreateClient();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", signedIn.AccessToken);

        // Like any unknown route (see ErrorHandlingTests): 401 without a token, 404 with one.
        await AssertProblemAsync(await anonymous.PostAsJsonAsync(Url, new { photos = false }, Ct), HttpStatusCode.Unauthorized, "auth.unauthenticated");
        await AssertProblemAsync(await client.PostAsJsonAsync(Url, new { photos = false }, Ct), HttpStatusCode.NotFound, "not_found");
        await AssertProblemAsync(await client.GetAsync(Url, Ct), HttpStatusCode.NotFound, "not_found");
        await AssertProblemAsync(await client.DeleteAsync(Url, Ct), HttpStatusCode.NotFound, "not_found");
        Assert.Equal(1, await WithDbAsync(db => db.Users.CountAsync(Ct)));
    }

    [Fact]
    public async Task Status_is_empty_before_any_generation()
    {
        var status = await StatusAsync();

        Assert.False(status.Exists);
        Assert.Empty(status.Accounts);
        Assert.Equal(new DemoDataCounts(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), status.Counts);
    }

    [Fact]
    public async Task Generation_creates_the_whole_community_and_reports_its_counts()
    {
        var result = await GenerateAsync();

        var counts = result.Counts;
        Assert.Equal((50, 10), (counts.Owners, counts.Educators));
        Assert.Equal(TableDogCount, counts.Dogs);
        Assert.InRange(DemoPeople.Owners.Sum(p => p.Dogs.Count), 80, 90);
        Assert.Equal(5, counts.Organizations);
        Assert.Equal((45, 25, 20), (counts.Walks, counts.UpcomingWalks, counts.PastWalks));
        Assert.InRange(counts.Registrations, 150, 400);
        Assert.InRange(counts.Messages, 20 * 3, 20 * 15);
        Assert.Equal((3, 4), (counts.Blocks, counts.Reports));
        Assert.Equal(0, counts.Photos);
        Assert.Equal((false, 0, 0), (result.PhotosRequested, result.PhotosStored, result.PhotosFailed));
        Assert.Empty(Factory.DemoPhotos.Requests);

        var threads = await WithDbAsync(db => db.Messages.Select(m => m.WalkId).Distinct().CountAsync(Ct));
        Assert.Equal(20, threads);
        Assert.Equal(5, await WithDbAsync(db => db.WalkSeries.CountAsync(Ct)));
        Assert.Equal(1, await WithDbAsync(db => db.Walks.CountAsync(w => w.Status == WalkStatus.Cancelled, Ct)));
    }

    [Fact]
    public async Task Generation_without_a_body_downloads_photos_by_default()
    {
        // The fake photo sources are unreachable: photos are attempted, and their failure tolerated.
        var response = await CreateClient().PostAsync(Url, null, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<DemoDataGenerationResponse>(Json, Ct))!;
        Assert.True(result.PhotosRequested);
        Assert.Equal(0, result.PhotosStored);
        Assert.True(result.PhotosFailed > 0);
        Assert.NotEmpty(Factory.DemoPhotos.Requests);
        Assert.Equal(60, result.Counts.Owners + result.Counts.Educators);
    }

    [Fact]
    public async Task Generating_again_replaces_the_data_with_the_same_structure()
    {
        var first = await GenerateAsync();
        var firstStatus = await StatusAsync();
        var firstWalks = await WalkShapesAsync();

        var second = await GenerateAsync();
        var secondStatus = await StatusAsync();

        Assert.Equal(first.Counts, second.Counts);
        Assert.Equal(Describe(firstStatus.Accounts), Describe(secondStatus.Accounts));
        Assert.Equal(firstWalks, await WalkShapesAsync());
        Assert.Equal(60, await WithDbAsync(db => db.Users.CountAsync(Ct)));
    }

    [Fact]
    public async Task Status_lists_the_demo_accounts_to_sign_in_as()
    {
        await SignInDevAsync("dev-user-1", "Camille Test");
        await GenerateAsync();

        var status = await StatusAsync();

        Assert.True(status.Exists);
        Assert.Equal(60, status.Accounts.Count);
        Assert.All(status.Accounts, a => Assert.StartsWith("demo-", a.Subject, StringComparison.Ordinal));

        var marie = status.Accounts.Single(a => a.Subject == "demo-marie-schmitt");
        Assert.Equal(("Marie Schmitt", DemoAccountKind.Owner, null), (marie.DisplayName, marie.Kind, marie.OrganizationName));
        Assert.Equal(["Filou", "Praline"], marie.DogNames);
        Assert.Null(marie.PhotoUrl);

        var sandrine = status.Accounts.Single(a => a.Subject == "demo-sandrine-gutzwiller");
        Assert.Equal((DemoAccountKind.Educator, "Club canin du Sundgau"), (sandrine.Kind, sandrine.OrganizationName));
        var marc = status.Accounts.Single(a => a.Subject == "demo-marc-hell");
        Assert.Equal((DemoAccountKind.Educator, "Club canin du Sundgau"), (marc.Kind, marc.OrganizationName));
        var lucie = status.Accounts.Single(a => a.Subject == "demo-lucie-reinhard");
        Assert.Equal((DemoAccountKind.Educator, "Ô Chiens Sages"), (lucie.Kind, lucie.OrganizationName));
        var hugo = status.Accounts.Single(a => a.Subject == "demo-hugo-lefebvre");
        Assert.Empty(hugo.DogNames);
        Assert.Equal(10, status.Accounts.Count(a => a.Kind == DemoAccountKind.Educator));
    }

    [Fact]
    public async Task A_demo_owner_signs_in_and_finds_walks_with_participants_and_threads_around_Seppois()
    {
        await GenerateAsync();
        var marie = await SignInAsDemoAsync("demo-marie-schmitt");

        var me = await marie.GetFromJsonAsync<JsonElement>("/api/v1/me", Json, Ct);
        Assert.Equal("Marie Schmitt", me.GetProperty("displayName").GetString());

        var url = string.Create(CultureInfo.InvariantCulture, $"/api/v1/walks?lat={SeppoisLatitude}&lng={SeppoisLongitude}&radiusKm=20&limit=100");
        var walks = (await marie.GetFromJsonAsync<List<WalkSearchResult>>(url, Json, Ct))!;
        Assert.Equal(24, walks.Count); // The 25 upcoming walks but the cancelled one.
        Assert.All(walks, w => Assert.InRange(w.DistanceMeters, 0, 15_000));
        Assert.Contains(walks, w => w.AcceptedDogsCount == w.MaxDogs);
        Assert.Contains(walks, w => w.IsProfessional);

        var full = walks.Single(w => w.Title == "Balade du dimanche à l'étang de Pfetterhouse");
        var detail = await GetWalkAsync(marie, full.Id);
        Assert.Equal(6, detail.Participants.Count);
        var thread = await marie.GetFromJsonAsync<JsonElement>($"/api/v1/walks/{full.Id}/messages", Json, Ct);
        Assert.InRange(thread.GetProperty("items").GetArrayLength(), 3, 15);

        var mine = (await marie.GetFromJsonAsync<List<JsonElement>>("/api/v1/me/walks?role=Organizer&when=Past", Json, Ct))!;
        Assert.NotEmpty(mine);
    }

    [Fact]
    public async Task The_blocker_sees_the_blocked_participant_warning()
    {
        await GenerateAsync();
        var thomas = await SignInAsDemoAsync("demo-thomas-haas");

        var url = string.Create(CultureInfo.InvariantCulture, $"/api/v1/walks?lat={SeppoisLatitude}&lng={SeppoisLongitude}&radiusKm=20&limit=100");
        var walks = (await thomas.GetFromJsonAsync<List<WalkSearchResult>>(url, Json, Ct))!;

        Assert.Contains(walks, w => w.HasBlockedParticipant);
        Assert.Equal(2, (await thomas.GetFromJsonAsync<List<JsonElement>>("/api/v1/me/blocks", Json, Ct))!.Count);
    }

    [Fact]
    public async Task An_educator_sees_the_members_and_requests_of_their_organisation()
    {
        await GenerateAsync();
        var sandrine = await SignInAsDemoAsync("demo-sandrine-gutzwiller");

        var mine = (await sandrine.GetFromJsonAsync<List<JsonElement>>("/api/v1/me/organizations", Json, Ct))!;
        var club = mine.Single().GetProperty("organization");
        Assert.Equal("Club canin du Sundgau", club.GetProperty("name").GetString());

        var memberships = await sandrine.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{club.GetProperty("id").GetGuid()}/memberships", Json, Ct);
        Assert.Equal(1 + 2 + 1 + 18, memberships.GetProperty("members").GetArrayLength());
        Assert.Equal(3, memberships.GetProperty("requests").GetArrayLength());
    }

    [Fact]
    public async Task Removal_deletes_every_demo_account_and_what_belongs_to_them()
    {
        await GenerateAsync();

        await RemoveAsync();

        var status = await StatusAsync();
        Assert.False(status.Exists);
        Assert.Equal(new DemoDataCounts(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), status.Counts);
        await WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.Users.CountAsync(Ct));
            Assert.Equal(0, await db.Dogs.CountAsync(Ct));
            Assert.Equal(0, await db.Walks.CountAsync(Ct));
            Assert.Equal(0, await db.WalkSeries.CountAsync(Ct));
            Assert.Equal(0, await db.Registrations.CountAsync(Ct));
            Assert.Equal(0, await db.Messages.CountAsync(Ct));
            Assert.Equal(0, await db.Organizations.CountAsync(Ct));
            Assert.Equal(0, await db.Memberships.CountAsync(Ct));
            Assert.Equal(0, await db.Blocks.CountAsync(Ct));
            Assert.Equal(0, await db.Reports.CountAsync(Ct));
        });

        // Removing again does nothing.
        await RemoveAsync();
    }

    [Fact]
    public async Task Only_dev_accounts_with_the_demo_prefix_are_demo_accounts()
    {
        await WithDbAsync(async db =>
        {
            db.Users.Add(new User { Provider = IdentityProvider.Google, ProviderSubject = "demo-google", DisplayName = "Google" });
            db.Users.Add(new User { Provider = IdentityProvider.Dev, ProviderSubject = "my-demo-account", DisplayName = "Dev" });
            await db.SaveChangesAsync(Ct);
        });

        await GenerateAsync();
        await RemoveAsync();

        var subjects = await WithDbAsync(db => db.Users.OrderBy(u => u.ProviderSubject).Select(u => u.ProviderSubject).ToListAsync(Ct));
        Assert.Equal(["demo-google", "my-demo-account"], subjects);
    }

    [Fact]
    public async Task The_owners_own_account_and_data_come_through_generation_and_removal_untouched()
    {
        // The product owner's test account, with UZU, a walk, an organisation and a message.
        var (owner, session) = await SignedInAsync("dev-user-1", "Camille Test");
        var uzu = await CreateDogAsync(owner, new
        {
            name = "UZU", size = "Medium", breed = "Shiba inu", sociability = "Easygoing", reactivity = "None", leashHabit = "OnLeash",
        });
        var ownWalk = await CreateWalkAsync(owner, ("title", "Balade de UZU"));
        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync(owner, ownWalk.Id, uzu.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync($"/api/v1/walks/{ownWalk.Id}/messages", new { text = "Bienvenue !" }, Ct)).StatusCode);
        var organization = await CreateOrganizationOkAsync(owner, "Club de UZU");

        await GenerateAsync();

        // UZU joins a demo walk and the owner writes in its thread.
        var url = string.Create(CultureInfo.InvariantCulture, $"/api/v1/walks?lat={SeppoisLatitude}&lng={SeppoisLongitude}&radiusKm=20&limit=100");
        var demoWalk = (await owner.GetFromJsonAsync<List<WalkSearchResult>>(url, Json, Ct))!
            .First(w => !w.IsProfessional && w.Access == WalkAccess.Open && w.AcceptedDogsCount < w.MaxDogs && w.AllowedSizes.Contains(Features.Dogs.DogSize.Medium));
        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync(owner, demoWalk.Id, uzu.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync($"/api/v1/walks/{demoWalk.Id}/messages", new { text = "UZU arrive !" }, Ct)).StatusCode);
        await WaitForNotificationsAsync();

        var before = await SnapshotAsync(session.User.Id);
        await GenerateAsync();
        Assert.Equal(before, await SnapshotAsync(session.User.Id));
        await RemoveAsync();
        Assert.Equal(before, await SnapshotAsync(session.User.Id));

        // The demo walk UZU joined is kept for its history, like the walk of a deleted account.
        var kept = await GetWalkAsync(owner, demoWalk.Id);
        Assert.Null(kept.Organizer);
        Assert.Equal(WalkStatus.Cancelled, kept.Status);
        Assert.Equal(["UZU"], kept.Participants.Select(p => p.Dog.Name));
        Assert.Equal(organization.Id, (await GetOrganizationAsync(owner, organization.Id)).Id);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/v1/me", Ct)).StatusCode);
    }

    /// <summary>Everything that belongs to one account, as comparable text.</summary>
    private Task<string> SnapshotAsync(Guid userId) => WithDbAsync(async db =>
    {
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, Ct);
        var dogs = await db.Dogs.AsNoTracking().Where(d => d.Members.Any(m => m.UserId == userId)).OrderBy(d => d.Id).ToListAsync(Ct);
        var walks = await db.Walks.AsNoTracking().Where(w => w.OrganizerId == userId).OrderBy(w => w.Id)
            .Select(w => new { w.Id, w.Title, w.Description, w.StartsAt, w.Status, w.MaxDogs, w.AllowedSizes, w.LocationLabel, w.CreatedAt })
            .ToListAsync(Ct);
        var registrations = await db.Registrations.AsNoTracking().Where(r => r.UserId == userId).OrderBy(r => r.Id)
            .Select(r => new { r.Id, r.WalkId, r.DogId, r.Status, r.CreatedAt })
            .ToListAsync(Ct);
        var messages = await db.Messages.AsNoTracking().Where(m => m.AuthorId == userId).OrderBy(m => m.Id)
            .Select(m => new { m.Id, m.WalkId, m.Text, m.CreatedAt })
            .ToListAsync(Ct);
        var organizations = await db.Organizations.AsNoTracking().Where(o => o.CreatedById == userId).OrderBy(o => o.Id)
            .Select(o => new { o.Id, o.Name, o.Description, o.City, o.Status, o.CreatedAt })
            .ToListAsync(Ct);
        var memberships = await db.Memberships.AsNoTracking().Where(m => m.UserId == userId).OrderBy(m => m.Id)
            .Select(m => new { m.Id, m.OrganizationId, m.Role, m.Status })
            .ToListAsync(Ct);
        return JsonSerializer.Serialize(new
        {
            user = new { user.Id, user.ProviderSubject, user.DisplayName, user.Bio, user.PhotoFileName, user.Status, user.CreatedAt },
            dogs = dogs.Select(d => new { d.Id, d.Name, d.Size, d.Breed, d.BirthDate, d.PhotoFileName, d.Description, d.CreatedAt }),
            walks,
            registrations,
            messages,
            organizations,
            memberships,
        });
    });

    private static List<string> Describe(IEnumerable<DemoAccountResponse> accounts) =>
        accounts.Select(a => $"{a.Subject}|{a.DisplayName}|{a.Kind}|{a.OrganizationName}|{string.Join(",", a.DogNames)}").ToList();

    /// <summary>Walks without their ids and dates: title, place, capacity, status, accepted and pending counts.</summary>
    private Task<List<string>> WalkShapesAsync() => WithDbAsync(db => db.Walks.AsNoTracking()
        .OrderBy(w => w.Title).ThenBy(w => w.StartsAt)
        .Select(w => w.Title + "|" + w.LocationLabel + "|" + w.MaxDogs + "|" + w.Status + "|" + w.Organizer!.ProviderSubject + "|"
            + w.Registrations.Count(r => r.Status == RegistrationStatus.Accepted) + "|"
            + w.Registrations.Count(r => r.Status == RegistrationStatus.Pending) + "|"
            + db.Messages.Count(m => m.WalkId == w.Id))
        .ToListAsync(Ct));
}
