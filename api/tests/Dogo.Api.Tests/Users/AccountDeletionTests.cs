using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dogo.Api.Features.Organizations;
using Dogo.Api.Features.Walks;
using Dogo.Api.Tests.Infrastructure;
using Dogo.Api.Tests.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Dogo.Api.Tests.Users;

/// <summary>Spec §4.8: deleting one's account, and what happens to everything around it.</summary>
public sealed class AccountDeletionTests(ApiFixture fixture) : OrganizationTestBase(fixture)
{
    private static async Task<string> UploadPhotoAsync(HttpClient client, string url)
    {
        var file = new ByteArrayContent(TestImages.Jpeg());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        using var content = new MultipartFormDataContent { { file, "file", "photo.jpg" } };
        var response = await client.PutAsync(url, content, Ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await ReadJsonAsync(response)).GetProperty("photoUrl").GetString()!.Split('/')[^1];
    }

    private static Task<HttpResponseMessage> DeleteMeAsync(HttpClient client) => client.DeleteAsync("/api/v1/me", Ct);

    private static Task<HttpResponseMessage> ReportAsync(HttpClient client, object body) => client.PostAsJsonAsync("/api/v1/reports", body, Ct);

    [Fact]
    public async Task Deleting_an_account_applies_every_consequence_of_the_specification()
    {
        // Alice, whose account is deleted, with a photo, two dogs, a device and preferences.
        var (alice, aliceSession) = await SignedInAsync("alice", "Alice");
        var aliceId = aliceSession.User.Id;
        var userPhoto = await UploadPhotoAsync(alice, "/api/v1/me/photo");
        var aliceDog = await CreateDogAsync(alice);
        var secondDog = await CreateDogAsync(alice, ValidDog("Second"));
        var dogPhoto = await UploadPhotoAsync(alice, $"/api/v1/dogs/{secondDog.Id}/photo");
        await RegisterDeviceAsync(alice, "alice");
        await alice.PutAsJsonAsync("/api/v1/me/notification-preferences", new { registrations = false, walkChanges = true, messages = true, reminders = true }, Ct);
        await SignInDevAsync("alice", "Alice"); // a second session

        // Bob and Carol, who stay.
        var (bob, bobId, bobDog) = await DogOwnerAsync("bob");
        var (carol, carolId, carolDog) = await DogOwnerAsync("carol");
        await RegisterDeviceAsync(bob, "bob");
        await RegisterDeviceAsync(carol, "carol");

        // Walks Alice organises: upcoming with an accepted and a pending registrant, weekly, already cancelled, past.
        var upcoming = await CreateWalkAsync(alice, ("access", "Approval"), ("title", "Tour du lac"));
        await AcceptAsync(alice, upcoming.Id, Assert.Single(await RegisterOkAsync(bob, upcoming.Id, bobDog.Id)).Id);
        await RegisterOkAsync(carol, upcoming.Id, carolDog.Id);
        var weekly = await CreateWalkAsync(alice, ("repeatWeekly", true), ("startsAt", Tomorrow.AddDays(1)));
        var alreadyCancelled = await CreateWalkAsync(alice, ("startsAt", Tomorrow.AddDays(2)));
        await alice.PostAsync($"/api/v1/walks/{alreadyCancelled.Id}/cancel", null, Ct);
        var past = await CreateWalkAsync(alice, ("title", "Balade passée"));
        await RegisterOkAsync(bob, past.Id, bobDog.Id);
        await bob.PostAsJsonAsync($"/api/v1/walks/{past.Id}/messages", new { text = "Merci pour la balade" }, Ct);
        await alice.PostAsJsonAsync($"/api/v1/walks/{past.Id}/messages", new { text = "Avec plaisir" }, Ct);
        await MoveWalkStartAsync(past.Id, DateTimeOffset.UtcNow.AddDays(-1));

        // Walks Bob organises, with Alice registered and writing.
        var bobWalk = await CreateWalkAsync(bob, ("title", "Chez Bob"));
        await RegisterOkAsync(alice, bobWalk.Id, aliceDog.Id, secondDog.Id);
        await alice.PostAsJsonAsync($"/api/v1/walks/{bobWalk.Id}/messages", new { text = "J'arrive avec mes deux chiens" }, Ct);
        var bobPastWalk = await CreateWalkAsync(bob);
        await RegisterOkAsync(alice, bobPastWalk.Id, aliceDog.Id);
        await MoveWalkStartAsync(bobPastWalk.Id, DateTimeOffset.UtcNow.AddDays(-1));

        // Organisations: Alice is the only owner of two, educator in Bob's, and has a request in another.
        var aliceClub = await ApprovedOrganizationAsync(alice, "Club d'Alice");
        await AddMemberAsync(alice, aliceClub.Id, bob);
        var alicePending = await CreateOrganizationOkAsync(alice, "Projet d'Alice");
        var bobClub = await ApprovedOrganizationAsync(bob, "Club de Bob");
        await AddEducatorAsync(bob, bobClub.Id, alice);
        var carolClub = await ApprovedOrganizationAsync(carol, "Club de Carol");
        await RequestMembershipOkAsync(alice, carolClub.Id);

        // Blocks both ways, reports by and about Alice.
        await alice.PostAsJsonAsync("/api/v1/blocks", new { userId = bobId }, Ct);
        await carol.PostAsJsonAsync("/api/v1/blocks", new { userId = aliceId }, Ct);
        await ReportAsync(alice, new { type = "HumanBehavior", facts = "Bob a été désagréable.", targetUserId = bobId });
        await ReportAsync(bob, new { type = "Altercation", facts = "Le chien d'Alice a mordu.", targetUserId = aliceId, targetDogId = aliceDog.Id, walkId = bobWalk.Id });
        await ReportAsync(carol, new { type = "BiteOrInjury", facts = "Morsure par le second chien.", targetDogId = secondDog.Id });
        await WaitForNotificationsAsync();
        var pushesBefore = Pushes.Count;

        var response = await DeleteMeAsync(alice);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await WithDbAsync(async db =>
        {
            Assert.False(await db.Users.AnyAsync(u => u.Id == aliceId, Ct));

            // Upcoming walks cancelled; every walk kept without organizer; series stopped.
            var walks = await db.Walks.AsNoTracking()
                .Where(w => new[] { upcoming.Id, weekly.Id, alreadyCancelled.Id, past.Id }.Contains(w.Id))
                .ToDictionaryAsync(w => w.Id, Ct);
            Assert.Equal(4, walks.Count);
            Assert.All(walks.Values, w => Assert.Null(w.OrganizerId));
            Assert.Equal(WalkStatus.Cancelled, walks[upcoming.Id].Status);
            Assert.Equal(WalkStatus.Cancelled, walks[weekly.Id].Status);
            Assert.Equal(WalkStatus.Cancelled, walks[alreadyCancelled.Id].Status);
            Assert.Equal(WalkStatus.Scheduled, walks[past.Id].Status);
            Assert.Null(walks[weekly.Id].SeriesId);
            Assert.Equal(0, await db.WalkSeries.CountAsync(Ct));
            Assert.Equal(1, await db.Walks.CountAsync(w => w.SeriesId == null && w.Id == weekly.Id, Ct));

            // Other people's registrations on Alice's walks are kept; hers are all deleted.
            Assert.Equal(2, await db.Registrations.CountAsync(r => r.WalkId == upcoming.Id, Ct));
            Assert.Equal(1, await db.Registrations.CountAsync(r => r.WalkId == past.Id, Ct));
            Assert.Equal(0, await db.Registrations.CountAsync(r => r.UserId == aliceId, Ct));
            Assert.Equal(0, await db.CountAcceptedAsync(bobWalk.Id, Ct));

            // Dogs, devices, preferences and sessions are deleted.
            Assert.Equal(0, await db.DogMembers.CountAsync(m => m.UserId == aliceId, Ct));
            Assert.Equal(0, await db.Devices.CountAsync(d => d.UserId == aliceId, Ct));
            Assert.Equal(0, await db.NotificationPreferences.CountAsync(p => p.UserId == aliceId, Ct));
            Assert.Equal(0, await db.RefreshTokens.CountAsync(t => t.UserId == aliceId, Ct));
            Assert.Equal(2, await db.Devices.CountAsync(Ct));

            // Organisations: those she alone owned are closed, the others untouched; memberships gone.
            var organizations = await db.Organizations.AsNoTracking().ToDictionaryAsync(o => o.Id, Ct);
            Assert.Equal(OrganizationStatus.Closed, organizations[aliceClub.Id].Status);
            Assert.Equal(OrganizationStatus.Closed, organizations[alicePending.Id].Status);
            Assert.Equal(OrganizationStatus.Approved, organizations[bobClub.Id].Status);
            Assert.Equal(OrganizationStatus.Approved, organizations[carolClub.Id].Status);
            Assert.Null(organizations[aliceClub.Id].CreatedById);
            Assert.Equal(0, await db.Memberships.CountAsync(m => m.UserId == aliceId, Ct));
            Assert.Equal(1, await db.Memberships.CountAsync(m => m.OrganizationId == aliceClub.Id, Ct));

            // Blocks in both directions are deleted.
            Assert.Equal(0, await db.Blocks.CountAsync(Ct));

            // Messages are kept without author.
            var messages = await db.Messages.AsNoTracking().OrderBy(m => m.CreatedAt).ToListAsync(Ct);
            Assert.Equal(["Merci pour la balade", "Avec plaisir", "J'arrive avec mes deux chiens"], messages.Select(m => m.Text));
            Assert.Equal(new Guid?[] { bobId, null, null }, messages.Select(m => m.AuthorId));

            // Reports are kept: hers without reporter, those about her or her dogs without that target.
            var reports = await db.Reports.AsNoTracking().OrderBy(r => r.CreatedAt).ToListAsync(Ct);
            Assert.Equal(3, reports.Count);
            Assert.Equal(((Guid?)null, (Guid?)bobId), (reports[0].ReporterId, reports[0].TargetUserId));
            Assert.Equal(((Guid?)bobId, (Guid?)null, (Guid?)null, (Guid?)bobWalk.Id), (reports[1].ReporterId, reports[1].TargetUserId, reports[1].TargetDogId, reports[1].WalkId));
            Assert.Equal(((Guid?)carolId, (Guid?)null), (reports[2].ReporterId, reports[2].TargetDogId));
        });

        // Stored photo files are deleted.
        Assert.False(File.Exists(Path.Combine(Factory.PhotoPath, userPhoto)));
        Assert.False(File.Exists(Path.Combine(Factory.PhotoPath, dogPhoto)));

        // Registrants of the upcoming walk are told, once each, after the commit; nobody else.
        var pushes = (await SentPushesAsync()).Skip(pushesBefore).ToList();
        Assert.Equal(new[] { bobId, carolId }.Order(), pushes.Select(p => p.UserId).Order());
        Assert.All(pushes, p =>
        {
            Assert.Equal("walk_cancelled", p.Data["type"]);
            Assert.Equal(upcoming.Id.ToString(), p.Data["walkId"]);
            Assert.Equal("Balade annulée", p.Title);
            Assert.StartsWith("« Tour du lac », prévue le ", p.Body, StringComparison.Ordinal);
        });

        // What the others still see.
        var pastSeenByBob = await GetWalkAsync(bob, past.Id);
        Assert.Null(pastSeenByBob.Organizer);
        Assert.False(pastSeenByBob.IsOrganizer);
        var thread = await ReadJsonAsync(await bob.GetAsync($"/api/v1/walks/{past.Id}/messages", Ct));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, thread.GetProperty("items")[1].GetProperty("author").ValueKind);
        var history = (await bob.GetFromJsonAsync<List<MyWalkResponse>>("/api/v1/me/walks?role=Participant&when=Past", Json, Ct))!;
        Assert.Contains(history, w => w.Id == past.Id && w.Organizer is null);
        Assert.Equal(WalkStatus.Cancelled, (await GetWalkAsync(carol, upcoming.Id)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/v1/users/{aliceId}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await carol.GetAsync($"/api/v1/organizations/{aliceClub.Id}", Ct)).StatusCode);
        Assert.Equal(OrganizationStatus.Closed, (await GetOrganizationAsync(bob, aliceClub.Id)).Status);
    }

    [Fact]
    public async Task Deleting_an_account_without_data_works()
    {
        var (alice, session) = await SignedInAsync("alice", "Alice");

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteMeAsync(alice)).StatusCode);

        Assert.Equal(0, await WithDbAsync(db => db.Users.CountAsync(Ct)));
        await AssertProblemAsync(
            await CreateClient().PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = session.RefreshToken }, Ct), HttpStatusCode.Unauthorized, "auth.invalid_token");
    }

    [Fact]
    public async Task Signing_in_again_after_deletion_creates_a_new_account()
    {
        var (alice, session) = await SignedInAsync("alice", "Alice");
        await DeleteMeAsync(alice);

        var again = await SignInDevAsync("alice", "Alice");

        Assert.NotEqual(session.User.Id, again.User.Id);
    }

    [Fact]
    public async Task An_organisation_with_another_owner_is_not_closed()
    {
        var (alice, _) = await SignedInAsync("alice", "Alice");
        var organization = await ApprovedOrganizationAsync(alice);
        var (bob, bobId, _) = await DogOwnerAsync("bob");
        await AddMemberAsync(alice, organization.Id, bob);

        // Only possible through the database: the API has no way to name a second owner.
        await WithDbAsync(db => db.Memberships.Where(m => m.UserId == bobId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Role, MembershipRole.Owner), Ct));
        await DeleteMeAsync(alice);

        Assert.Equal(OrganizationStatus.Approved, (await GetOrganizationAsync(bob, organization.Id)).Status);
    }

    public static TheoryData<string, string> Endpoints => new()
    {
        { "GET", "/api/v1/me" },
        { "GET", "/api/v1/me/dogs" },
        { "GET", "/api/v1/walks?lat=48.85&lng=2.35" },
        { "GET", "/api/v1/me/walks?role=Organizer" },
        { "GET", "/api/v1/me/organizations" },
        { "GET", "/api/v1/me/blocks" },
        { "GET", $"/api/v1/users/{Guid.NewGuid()}" },
        { "POST", "/api/v1/dogs" },
        { "POST", "/api/v1/walks" },
        { "POST", "/api/v1/organizations" },
        { "POST", "/api/v1/blocks" },
        { "POST", "/api/v1/reports" },
        { "PATCH", "/api/v1/me" },
        { "PUT", "/api/v1/me/devices" },
        { "DELETE", "/api/v1/me" },
        { "GET", "/api/v1/admin/reports" },
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task The_access_token_of_a_deleted_account_stops_working_everywhere(string method, string url)
    {
        var (alice, _) = await SignedInAsync("alice", "Alice", DogoApiFactory.AdminEmail);
        await DeleteMeAsync(alice);
        var (bob, bobSession) = await SignedInAsync("bob", "Bob");

        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method != "GET" && method != "DELETE")
        {
            request.Content = JsonContent.Create(new { userId = bobSession.User.Id });
        }

        await AssertProblemAsync(await alice.SendAsync(request, Ct), HttpStatusCode.Unauthorized, "auth.invalid_token");
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync("/api/v1/me", Ct)).StatusCode);
    }

    [Fact]
    public async Task A_suspended_account_can_delete_itself_and_the_reports_about_it_are_kept()
    {
        // ADR 0036: the right to erasure outlives a suspension; DELETE /me is its only write.
        var (alice, session) = await SignedInAsync("alice", "Alice");
        var (bob, bobId, _) = await DogOwnerAsync("bob");
        var (admin, _) = await AdminAsync();
        Assert.Equal(HttpStatusCode.Created, (await ReportAsync(bob, new { type = "HumanBehavior", facts = "Propos insultants envers Bob.", targetUserId = session.User.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await ReportAsync(alice, new { type = "Altercation", facts = "Altercation au parc avec Bob.", targetUserId = bobId })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/v1/admin/users/{session.User.Id}/suspend", null, Ct)).StatusCode);
        await AssertProblemAsync(await alice.PatchAsJsonAsync("/api/v1/me", new { bio = "Encore là" }, Ct), HttpStatusCode.Forbidden, "account.suspended");

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteMeAsync(alice)).StatusCode);

        Assert.False(await WithDbAsync(db => db.Users.AnyAsync(u => u.Id == session.User.Id, Ct)));
        var reports = await WithDbAsync(db => db.Reports.OrderBy(r => r.CreatedAt).ToListAsync(Ct));
        Assert.Equal(2, reports.Count);
        Assert.Null(reports[0].TargetUserId);
        Assert.Equal(bobId, reports[0].ReporterId);
        Assert.Null(reports[1].ReporterId);
        Assert.Equal(bobId, reports[1].TargetUserId);
    }
}
