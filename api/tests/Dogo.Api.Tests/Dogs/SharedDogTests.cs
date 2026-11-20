using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dogo.Api.Features.Admin;
using Dogo.Api.Features.Dogs;
using Dogo.Api.Features.Users;
using Dogo.Api.Features.Walks;
using Dogo.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Dogo.Api.Tests.Dogs;

/// <summary>
/// A dog with several humans (spec §4.1, §4.3, §4.4, §4.7, §4.8; ADR 0042): equal rights, leaving,
/// deleting by the last owner only, registrations by any owner, account deletion and suspension.
/// </summary>
public sealed class SharedDogTests(ApiFixture fixture) : SharedDogTestBase(fixture)
{
    private static MultipartFormDataContent PhotoContent(byte[] bytes)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return new MultipartFormDataContent { { file, "file", "photo.jpg" } };
    }

    [Fact]
    public async Task Every_owner_edits_changes_the_photo_invites_and_registers()
    {
        var (_, _, camille, camilleId, dog) = await CoupleAsync();

        var patched = await camille.PatchAsJsonAsync($"/api/v1/dogs/{dog.Id}", new { name = "Noux" }, Ct);
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        Assert.Equal("Noux", (await patched.Content.ReadFromJsonAsync<DogResponse>(Json, Ct))!.Name);

        var photo = await camille.PutAsync($"/api/v1/dogs/{dog.Id}/photo", PhotoContent(TestImages.Jpeg()), Ct);
        Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
        var withPhoto = (await photo.Content.ReadFromJsonAsync<DogResponse>(Json, Ct))!;
        Assert.NotNull(withPhoto.PhotoUrl);
        Assert.Equal(2, withPhoto.Members.Count);

        await InviteOkAsync(camille, dog.Id);

        var (organizer, _) = await SignedInAsync("organizer", "Organizer");
        var walk = await CreateWalkAsync(organizer);
        var registration = Assert.Single(await RegisterOkAsync(camille, walk.Id, dog.Id));
        Assert.Equal(camilleId, registration.UserId);
    }

    [Fact]
    public async Task Members_are_listed_from_the_oldest_link_to_the_newest()
    {
        var (victor, victorId, dog) = await DogOwnerAsync("victor");
        var (camille, camilleSession) = await SignedInAsync("camille", "Camille");
        var (bob, bobSession) = await SignedInAsync("bob", "Bob");
        await ShareAsync(victor, dog.Id, camille);
        await ShareAsync(camille, dog.Id, bob);

        var members = (await GetDogAsync(bob, dog.Id)).Members;

        Assert.Equal([victorId, camilleSession.User.Id, bobSession.User.Id], members.Select(m => m.User.Id));
        Assert.All(members, m => Assert.Equal(DogMemberRole.Owner, m.Role));
    }

    [Fact]
    public async Task Only_the_last_owner_deletes_the_dog()
    {
        var (victor, _, camille, _, dog) = await CoupleAsync();

        await AssertProblemAsync(await victor.DeleteAsync($"/api/v1/dogs/{dog.Id}", Ct), HttpStatusCode.Conflict, "dog.has_other_owners");
        await AssertProblemAsync(await camille.DeleteAsync($"/api/v1/dogs/{dog.Id}", Ct), HttpStatusCode.Conflict, "dog.has_other_owners");

        Assert.Equal(HttpStatusCode.NoContent, (await LeaveAsync(camille, dog.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await victor.DeleteAsync($"/api/v1/dogs/{dog.Id}", Ct)).StatusCode);
        Assert.Equal(0, await WithDbAsync(db => db.DogMembers.CountAsync(Ct)));
    }

    [Fact]
    public async Task Deleting_by_the_last_owner_cancels_upcoming_registrations_made_by_anyone()
    {
        var (victor, _, camille, camilleId, dog) = await CoupleAsync();
        var (organizer, _) = await SignedInAsync("organizer", "Organizer");
        var walk = await CreateWalkAsync(organizer);
        var registration = Assert.Single(await RegisterOkAsync(camille, walk.Id, dog.Id));
        Assert.Equal(HttpStatusCode.NoContent, (await LeaveAsync(camille, dog.Id)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await victor.DeleteAsync($"/api/v1/dogs/{dog.Id}", Ct)).StatusCode);

        var stored = await WithDbAsync(db => db.Registrations.AsNoTracking().SingleAsync(r => r.Id == registration.Id, Ct));
        Assert.Equal((RegistrationStatus.Cancelled, camilleId), (stored.Status, stored.UserId));
        Assert.Equal(0, (await GetWalkAsync(organizer, walk.Id)).AcceptedDogsCount);
    }

    [Fact]
    public async Task Leaving_removes_only_my_link_and_keeps_my_registrations()
    {
        var (victor, victorId, camille, camilleId, dog) = await CoupleAsync();
        var (organizer, _) = await SignedInAsync("organizer", "Organizer");
        var walk = await CreateWalkAsync(organizer);
        var registration = Assert.Single(await RegisterOkAsync(camille, walk.Id, dog.Id));

        Assert.Equal(HttpStatusCode.NoContent, (await LeaveAsync(camille, dog.Id)).StatusCode);

        Assert.Empty(await MyDogsAsync(camille));
        Assert.Equal([victorId], (await GetDogAsync(victor, dog.Id)).Members.Select(m => m.User.Id));
        var stored = await WithDbAsync(db => db.Registrations.AsNoTracking().SingleAsync(r => r.Id == registration.Id, Ct));
        Assert.Equal((RegistrationStatus.Accepted, camilleId), (stored.Status, stored.UserId));

        // No longer an owner: no rights left on the dog; the registration stays hers to cancel.
        await AssertProblemAsync(await camille.PatchAsJsonAsync($"/api/v1/dogs/{dog.Id}", new { name = "Mine" }, Ct), HttpStatusCode.Forbidden, "dog.forbidden");
        await AssertProblemAsync(await LeaveAsync(camille, dog.Id), HttpStatusCode.Forbidden, "dog.forbidden");
        Assert.Equal(HttpStatusCode.NoContent, (await CancelRegistrationAsync(camille, walk.Id, registration.Id)).StatusCode);
    }

    [Fact]
    public async Task The_last_owner_cannot_leave()
    {
        var (victor, _, dog) = await DogOwnerAsync("victor");
        var (stranger, _) = await SignedInAsync("stranger", "Stranger");

        await AssertProblemAsync(await LeaveAsync(victor, dog.Id), HttpStatusCode.Conflict, "dog.last_owner");
        await AssertProblemAsync(await LeaveAsync(stranger, dog.Id), HttpStatusCode.Forbidden, "dog.forbidden");
        await AssertProblemAsync(await LeaveAsync(victor, Guid.NewGuid()), HttpStatusCode.NotFound, "dog.not_found");
        Assert.Single(await MyDogsAsync(victor));
    }

    [Fact]
    public async Task Two_owners_leaving_at_once_leave_one_owner()
    {
        for (var round = 0; round < 5; round++)
        {
            var (victor, _, camille, _, dog) = await CoupleAsync($"victor{round}", $"camille{round}");

            using var start = new SemaphoreSlim(0);
            var requests = new[] { victor, camille }.Select(client => Task.Run(async () =>
            {
                await start.WaitAsync(Ct);
                return await LeaveAsync(client, dog.Id);
            }, Ct)).ToList();
            start.Release(2);
            var responses = await Task.WhenAll(requests);

            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.NoContent);
            await AssertProblemAsync(Assert.Single(responses, r => r.StatusCode != HttpStatusCode.NoContent), HttpStatusCode.Conflict, "dog.last_owner");
            Assert.Equal(1, await WithDbAsync(db => db.DogMembers.CountAsync(m => m.DogId == dog.Id, Ct)));
        }
    }

    [Fact]
    public async Task Leaving_and_deleting_at_once_never_leave_a_dog_without_owner()
    {
        for (var round = 0; round < 5; round++)
        {
            var (victor, _, camille, _, dog) = await CoupleAsync($"victor{round}", $"camille{round}");

            using var start = new SemaphoreSlim(0);
            var leave = Task.Run(async () => { await start.WaitAsync(Ct); return await LeaveAsync(camille, dog.Id); }, Ct);
            var delete = Task.Run(async () => { await start.WaitAsync(Ct); return await victor.DeleteAsync($"/api/v1/dogs/{dog.Id}", Ct); }, Ct);
            start.Release(2);
            await Task.WhenAll(leave, delete);

            Assert.Equal(HttpStatusCode.NoContent, leave.Result.StatusCode);
            var (dogs, links) = await WithDbAsync(async db => (
                await db.Dogs.CountAsync(d => d.Id == dog.Id, Ct),
                await db.DogMembers.CountAsync(m => m.DogId == dog.Id, Ct)));
            if (delete.Result.StatusCode == HttpStatusCode.NoContent)
            {
                Assert.Equal((0, 0), (dogs, links));
            }
            else
            {
                await AssertProblemAsync(delete.Result, HttpStatusCode.Conflict, "dog.has_other_owners");
                Assert.Equal((1, 1), (dogs, links));
            }
        }
    }

    [Fact]
    public async Task A_dog_registered_by_one_owner_cannot_be_registered_again_by_another()
    {
        var (victor, _, camille, camilleId, dog) = await CoupleAsync();
        var (organizer, _) = await SignedInAsync("organizer", "Organizer");
        var walk = await CreateWalkAsync(organizer);
        await RegisterOkAsync(camille, walk.Id, dog.Id);

        await AssertProblemAsync(await RegisterAsync(victor, walk.Id, dog.Id), HttpStatusCode.Conflict, "registration.duplicate");
        await AssertProblemAsync(await RegisterAsync(camille, walk.Id, dog.Id), HttpStatusCode.Conflict, "registration.duplicate");

        // Someone who is not an owner gets a 404, as for a dog that does not exist.
        var (stranger, _) = await SignedInAsync("stranger", "Stranger");
        await AssertProblemAsync(await RegisterAsync(stranger, walk.Id, dog.Id), HttpStatusCode.NotFound, "dog.not_found");

        var participant = Assert.Single((await GetWalkAsync(victor, walk.Id)).Participants);
        Assert.Equal((dog.Id, camilleId), (participant.Dog.Id, participant.Owner.Id));
        Assert.Equal(2, participant.Dog.Members.Count);
    }

    [Fact]
    public async Task The_walk_shows_my_dogs_registrations_with_who_made_them_pending_ones_included()
    {
        var (victor, victorId, camille, camilleId, dog) = await CoupleAsync();
        var mine = await CreateDogOfSizeAsync(victor, "Uzu", "Medium");
        var (organizer, _) = await SignedInAsync("organizer", "Organizer");
        var walk = await CreateWalkAsync(organizer, ("access", "Approval"));
        var byCamille = Assert.Single(await RegisterOkAsync(camille, walk.Id, dog.Id));
        var byVictor = Assert.Single(await RegisterOkAsync(victor, walk.Id, mine.Id));

        var seenByVictor = await GetWalkAsync(victor, walk.Id);

        Assert.Equal(
            [
                new DogRegistrationResponse(byCamille.Id, dog.Id, RegistrationStatus.Pending, new UserSummaryResponse(camilleId, "camille", null)),
                new DogRegistrationResponse(byVictor.Id, mine.Id, RegistrationStatus.Pending, new UserSummaryResponse(victorId, "victor", null)),
            ],
            seenByVictor.MyDogsRegistrations);
        Assert.Equal([byVictor.Id], seenByVictor.MyRegistrations.Select(r => r.Id));
        Assert.Empty(seenByVictor.PendingRegistrations);
        Assert.Empty(seenByVictor.Participants);

        // Camille sees her registration of the shared dog, not Victor's own dog.
        var seenByCamille = await GetWalkAsync(camille, walk.Id);
        Assert.Equal([byCamille.Id], seenByCamille.MyDogsRegistrations.Select(r => r.Id));

        // Someone else sees no pending registration at all.
        var (stranger, _) = await SignedInAsync("stranger", "Stranger");
        var seenByStranger = await GetWalkAsync(stranger, walk.Id);
        Assert.Empty(seenByStranger.MyDogsRegistrations);
        Assert.Empty(seenByStranger.PendingRegistrations);

        // Once accepted, then cancelled: accepted ones are listed too, cancelled ones no more.
        Assert.Equal(HttpStatusCode.OK, (await AcceptAsync(organizer, walk.Id, byCamille.Id)).StatusCode);
        Assert.Equal(RegistrationStatus.Accepted, (await GetWalkAsync(victor, walk.Id)).MyDogsRegistrations.Single(r => r.Id == byCamille.Id).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await CancelRegistrationAsync(victor, walk.Id, byCamille.Id)).StatusCode);
        Assert.DoesNotContain((await GetWalkAsync(victor, walk.Id)).MyDogsRegistrations, r => r.Id == byCamille.Id);
    }

    [Fact]
    public async Task Another_owner_cancels_a_registration_and_its_author_is_told()
    {
        var (victor, _, camille, camilleId, dog) = await CoupleAsync();
        await RegisterDeviceAsync(victor, "victor");
        await RegisterDeviceAsync(camille, "camille");
        var (organizer, _) = await SignedInAsync("organizer", "Organizer");
        var walk = await CreateWalkAsync(organizer, ("title", "Tour du lac"));
        var registration = Assert.Single(await RegisterOkAsync(camille, walk.Id, dog.Id));
        var before = (await SentPushesAsync()).Count;

        Assert.Equal(HttpStatusCode.NoContent, (await CancelRegistrationAsync(victor, walk.Id, registration.Id)).StatusCode);

        var push = Assert.Single((await SentPushesAsync()).Skip(before));
        Assert.Equal((camilleId, "registration_cancelled_by_owner"), (push.UserId, push.Data["type"]));
        Assert.Equal("Inscription annulée", push.Title);
        Assert.Equal($"victor a annulé l'inscription de {dog.Name} à « Tour du lac ».", push.Body);
        Assert.Equal($"uzu://walks/{walk.Id}", push.Data["url"]);
        Assert.Equal(0, (await GetWalkAsync(organizer, walk.Id)).AcceptedDogsCount);

        // Cancelling one's own registration notifies nobody; neither does cancelling again.
        Assert.Equal(HttpStatusCode.NoContent, (await CancelRegistrationAsync(victor, walk.Id, registration.Id)).StatusCode);
        var own = Assert.Single(await RegisterOkAsync(victor, walk.Id, dog.Id));
        before = (await SentPushesAsync()).Count;
        Assert.Equal(HttpStatusCode.NoContent, (await CancelRegistrationAsync(victor, walk.Id, own.Id)).StatusCode);
        Assert.Empty((await SentPushesAsync()).Skip(before));
    }

    [Fact]
    public async Task The_cancellation_notice_follows_the_registrations_preference()
    {
        var (victor, _, camille, _, dog) = await CoupleAsync();
        await RegisterDeviceAsync(camille, "camille");
        await camille.PutAsJsonAsync("/api/v1/me/notification-preferences", new { registrations = false, walkChanges = true, messages = true, reminders = true }, Ct);
        var (organizer, _) = await SignedInAsync("organizer", "Organizer");
        var walk = await CreateWalkAsync(organizer);
        var registration = Assert.Single(await RegisterOkAsync(camille, walk.Id, dog.Id));

        Assert.Equal(HttpStatusCode.NoContent, (await CancelRegistrationAsync(victor, walk.Id, registration.Id)).StatusCode);

        Assert.Empty(await SentPushesAsync());
    }

    [Fact]
    public async Task Someone_neither_author_nor_owner_cannot_cancel()
    {
        var (_, _, camille, _, dog) = await CoupleAsync();
        var (organizer, _) = await SignedInAsync("organizer", "Organizer");
        var walk = await CreateWalkAsync(organizer);
        var registration = Assert.Single(await RegisterOkAsync(camille, walk.Id, dog.Id));
        var (stranger, _) = await SignedInAsync("stranger", "Stranger");

        await AssertProblemAsync(await CancelRegistrationAsync(stranger, walk.Id, registration.Id), HttpStatusCode.Forbidden, "registration.forbidden");
        await AssertProblemAsync(await CancelRegistrationAsync(organizer, walk.Id, registration.Id), HttpStatusCode.Forbidden, "registration.forbidden");
        Assert.Equal(1, (await GetWalkAsync(organizer, walk.Id)).AcceptedDogsCount);
    }

    [Fact]
    public async Task Deleting_an_account_keeps_shared_dogs_with_their_other_owners()
    {
        var (victor, victorId, camille, camilleId, shared) = await CoupleAsync();
        var own = await CreateDogOfSizeAsync(camille, "Pistache", "Small");
        var ownPhoto = await camille.PutAsync($"/api/v1/dogs/{own.Id}/photo", PhotoContent(TestImages.Jpeg()), Ct);
        Assert.Equal(HttpStatusCode.OK, ownPhoto.StatusCode);
        var pending = await InviteOkAsync(camille, shared.Id);

        // Bob registers Camille's own dog while he co-owns it, then leaves it: the registration stays hers to delete.
        var (bob, _) = await SignedInAsync("bob", "Bob");
        await ShareAsync(camille, own.Id, bob);
        var (organizer, _) = await SignedInAsync("organizer", "Organizer");
        var walk = await CreateWalkAsync(organizer);
        var byBob = Assert.Single(await RegisterOkAsync(bob, walk.Id, own.Id));
        Assert.Equal(HttpStatusCode.NoContent, (await LeaveAsync(bob, own.Id)).StatusCode);
        var photoFiles = Directory.GetFiles(Factory.PhotoPath).Length;

        Assert.Equal(HttpStatusCode.NoContent, (await camille.DeleteAsync("/api/v1/me", Ct)).StatusCode);

        // The shared dog stays with Victor; the dog she was the last owner of is gone with its photo.
        Assert.Equal([victorId], (await GetDogAsync(victor, shared.Id)).Members.Select(m => m.User.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await victor.GetAsync($"/api/v1/dogs/{own.Id}", Ct)).StatusCode);
        Assert.Equal(photoFiles - 1, Directory.GetFiles(Factory.PhotoPath).Length);
        var stored = await WithDbAsync(db => db.Registrations.AsNoTracking().SingleAsync(r => r.Id == byBob.Id, Ct));
        Assert.Equal(RegistrationStatus.Cancelled, stored.Status);

        // Invitations she created or accepted stay with the dog, without author.
        var invitations = await WithDbAsync(db => db.DogInvitations.AsNoTracking().ToListAsync(Ct));
        Assert.All(invitations, i => Assert.NotEqual(camilleId, i.CreatedById));
        Assert.All(invitations, i => Assert.NotEqual(camilleId, i.UsedById));
        var kept = Assert.Single(invitations, i => i.Id == pending.Id);
        Assert.Null(kept.CreatedById);
        Assert.Contains(invitations, i => i.DogId == shared.Id && i.UsedAt != null && i.UsedById == null);
    }

    [Fact]
    public async Task A_suspended_owner_keeps_the_link_and_the_other_owner_still_registers_the_dog()
    {
        var (victor, _, camille, camilleId, dog) = await CoupleAsync();
        var (organizer, _) = await SignedInAsync("organizer", "Organizer");
        var walk = await CreateWalkAsync(organizer);
        var byCamille = Assert.Single(await RegisterOkAsync(camille, walk.Id, dog.Id));
        var (admin, _) = await AdminAsync();

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/v1/admin/users/{camilleId}/suspend", null, Ct)).StatusCode);

        Assert.Equal(2, (await GetDogAsync(victor, dog.Id)).Members.Count);
        var stored = await WithDbAsync(db => db.Registrations.AsNoTracking().SingleAsync(r => r.Id == byCamille.Id, Ct));
        Assert.Equal(RegistrationStatus.Cancelled, stored.Status);
        await RegisterOkAsync(victor, walk.Id, dog.Id);
        await AssertProblemAsync(await LeaveAsync(camille, dog.Id), HttpStatusCode.Forbidden, "account.suspended");
    }

    [Fact]
    public async Task An_administrator_sees_every_human_of_a_reported_dog()
    {
        var (_, victorId, _, camilleId, dog) = await CoupleAsync();
        var (reporter, _) = await SignedInAsync("reporter", "Reporter");
        await reporter.PostAsJsonAsync("/api/v1/reports", new { type = "IncidentNoInjury", facts = "Il a bousculé ma chienne.", targetDogId = dog.Id }, Ct);
        var (admin, _) = await AdminAsync();

        var report = Assert.Single((await admin.GetFromJsonAsync<List<AdminReportResponse>>("/api/v1/admin/reports", Json, Ct))!);

        Assert.Equal([victorId, camilleId], report.TargetDog!.Members.Select(m => m.User.Id));
    }
}
