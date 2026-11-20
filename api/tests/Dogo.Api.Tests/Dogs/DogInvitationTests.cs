using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dogo.Api.Features.Dogs;
using Dogo.Api.Features.Users;
using Dogo.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Dogo.Api.Tests.Dogs;

/// <summary>Spec §4.1, « Inviter un humain » (ADR 0042): a single-use link, valid 7 days, revocable, stored hashed.</summary>
public sealed class DogInvitationTests(ApiFixture fixture) : SharedDogTestBase(fixture)
{
    private static Task<HttpResponseMessage> PreviewAsync(HttpClient client, string token) =>
        client.GetAsync($"/api/v1/dog-invitations/{token}", Ct);

    private Task ExpireAsync(Guid invitationId) =>
        WithDbAsync(db => db.DogInvitations.Where(i => i.Id == invitationId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)), Ct));

    [Fact]
    public async Task An_owner_creates_a_link_whose_token_is_returned_once_and_stored_hashed()
    {
        var (victor, victorId, dog) = await DogOwnerAsync("victor");
        var before = DateTimeOffset.UtcNow;

        var invitation = await InviteOkAsync(victor, dog.Id);

        Assert.Matches("^[A-Za-z0-9_-]{43}$", invitation.Token);
        Assert.Equal($"uzu://invitation/{invitation.Token}", invitation.Link);
        Assert.Equal((dog.Id, DogMemberRole.Owner), (invitation.DogId, invitation.Role));
        Assert.InRange(invitation.ExpiresAt, before.AddDays(7).AddSeconds(-5), DateTimeOffset.UtcNow.AddDays(7));
        var stored = await WithDbAsync(db => db.DogInvitations.AsNoTracking().SingleAsync(Ct));
        Assert.Equal(DogInvitation.Hash(invitation.Token), stored.TokenHash);
        Assert.NotEqual(invitation.Token, stored.TokenHash);
        Assert.Equal((victorId, (Guid?)null, (DateTimeOffset?)null), (stored.CreatedById!.Value, stored.UsedById, stored.RevokedAt));

        // Two invitations never share a token.
        Assert.NotEqual(invitation.Token, (await InviteOkAsync(victor, dog.Id)).Token);
    }

    [Fact]
    public async Task Only_the_owner_role_is_accepted()
    {
        var (victor, _, dog) = await DogOwnerAsync("victor");

        await AssertValidationProblemAsync(await InviteAsync(victor, dog.Id, "Trusted"), "role");
        await AssertValidationProblemAsync(await InviteAsync(victor, dog.Id, "Educator"), "role");
        await AssertValidationProblemAsync(await victor.PostAsJsonAsync($"/api/v1/dogs/{dog.Id}/invitations", new { }, Ct), "role");
        Assert.Equal(0, await WithDbAsync(db => db.DogInvitations.CountAsync(Ct)));
    }

    [Fact]
    public async Task Creating_listing_and_revoking_are_reserved_to_owners()
    {
        var (victor, _, dog) = await DogOwnerAsync("victor");
        var invitation = await InviteOkAsync(victor, dog.Id);
        var (stranger, _) = await SignedInAsync("stranger", "Stranger");

        await AssertProblemAsync(await InviteAsync(stranger, dog.Id), HttpStatusCode.Forbidden, "dog.forbidden");
        await AssertProblemAsync(await stranger.GetAsync($"/api/v1/dogs/{dog.Id}/invitations", Ct), HttpStatusCode.Forbidden, "dog.forbidden");
        await AssertProblemAsync(await stranger.DeleteAsync($"/api/v1/dogs/{dog.Id}/invitations/{invitation.Id}", Ct), HttpStatusCode.Forbidden, "dog.forbidden");
        await AssertProblemAsync(await InviteAsync(victor, Guid.NewGuid()), HttpStatusCode.NotFound, "dog.not_found");
        await AssertProblemAsync(await victor.GetAsync($"/api/v1/dogs/{Guid.NewGuid()}/invitations", Ct), HttpStatusCode.NotFound, "dog.not_found");
        await AssertProblemAsync(await victor.DeleteAsync($"/api/v1/dogs/{dog.Id}/invitations/{Guid.NewGuid()}", Ct), HttpStatusCode.NotFound, "invitation.not_found");
        Assert.Equal(1, await WithDbAsync(db => db.DogInvitations.CountAsync(i => i.RevokedAt == null, Ct)));
    }

    [Fact]
    public async Task The_list_shows_pending_invitations_without_their_token()
    {
        var (victor, victorId, dog) = await DogOwnerAsync("victor");
        var (camille, _) = await SignedInAsync("camille", "Camille");
        var used = await InviteOkAsync(victor, dog.Id);
        await AcceptInvitationAsync(camille, used.Token);
        var revoked = await InviteOkAsync(victor, dog.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await victor.DeleteAsync($"/api/v1/dogs/{dog.Id}/invitations/{revoked.Id}", Ct)).StatusCode);
        var expired = await InviteOkAsync(victor, dog.Id);
        await ExpireAsync(expired.Id);
        var first = await InviteOkAsync(victor, dog.Id);
        var second = await InviteOkAsync(camille, dog.Id);

        var response = await victor.GetAsync($"/api/v1/dogs/{dog.Id}/invitations", Ct);

        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(first.Token, body, StringComparison.Ordinal);
        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
        var list = JsonSerializer.Deserialize<List<DogInvitationResponse>>(body, Json)!;
        Assert.Equal([first.Id, second.Id], list.Select(i => i.Id));
        Assert.Equal((victorId, "victor"), (list[0].CreatedBy!.Id, list[0].CreatedBy!.DisplayName));
        Assert.Equal(first.ExpiresAt, list[0].ExpiresAt);
    }

    [Fact]
    public async Task Revoking_is_idempotent_and_a_used_invitation_cannot_be_revoked()
    {
        var (victor, _, dog) = await DogOwnerAsync("victor");
        var (camille, _) = await SignedInAsync("camille", "Camille");
        var invitation = await InviteOkAsync(victor, dog.Id);
        var used = await InviteOkAsync(victor, dog.Id);
        await AcceptInvitationAsync(camille, used.Token);

        Assert.Equal(HttpStatusCode.NoContent, (await victor.DeleteAsync($"/api/v1/dogs/{dog.Id}/invitations/{invitation.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await victor.DeleteAsync($"/api/v1/dogs/{dog.Id}/invitations/{invitation.Id}", Ct)).StatusCode);
        await AssertProblemAsync(await victor.DeleteAsync($"/api/v1/dogs/{dog.Id}/invitations/{used.Id}", Ct), HttpStatusCode.Conflict, "invitation.already_used");

        // Revoked: unknown for whoever holds the link.
        await AssertProblemAsync(await PreviewAsync(camille, invitation.Token), HttpStatusCode.NotFound, "invitation.not_found");
    }

    [Fact]
    public async Task Looking_at_an_invitation_shows_the_dog_the_inviter_and_the_role_and_gives_no_right()
    {
        var (victor, victorId, dog) = await DogOwnerAsync("victor");
        var invitation = await InviteOkAsync(victor, dog.Id);
        var (camille, _) = await SignedInAsync("camille", "Camille");

        var response = await PreviewAsync(camille, invitation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = (await response.Content.ReadFromJsonAsync<DogInvitationPreviewResponse>(Json, Ct))!;
        AssertSameDog(dog, preview.Dog);
        Assert.Equal(new UserSummaryResponse(victorId, "victor", null), preview.InvitedBy);
        Assert.Equal((DogMemberRole.Owner, invitation.ExpiresAt), (preview.Role, preview.ExpiresAt));
        Assert.Empty(await MyDogsAsync(camille));
        await AssertProblemAsync(await camille.PatchAsJsonAsync($"/api/v1/dogs/{dog.Id}", new { name = "Mine" }, Ct), HttpStatusCode.Forbidden, "dog.forbidden");

        // The owner may look at it too: looking only checks the invitation itself.
        Assert.Equal(HttpStatusCode.OK, (await PreviewAsync(victor, invitation.Token)).StatusCode);
    }

    [Fact]
    public async Task Accepting_links_the_dog_to_its_new_human()
    {
        var (victor, victorId, dog) = await DogOwnerAsync("victor");
        var (camille, camilleSession) = await SignedInAsync("camille", "Camille");
        var invitation = await InviteOkAsync(victor, dog.Id);

        var response = await AcceptInvitationAsync(camille, invitation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var shared = (await response.Content.ReadFromJsonAsync<DogResponse>(Json, Ct))!;
        Assert.Equal(
            [
                new DogMemberResponse(new UserSummaryResponse(victorId, "victor", null), DogMemberRole.Owner),
                new DogMemberResponse(new UserSummaryResponse(camilleSession.User.Id, "Camille", null), DogMemberRole.Owner),
            ],
            shared.Members);
        AssertSameDog(shared, Assert.Single(await MyDogsAsync(camille)));
        AssertSameDog(shared, Assert.Single(await MyDogsAsync(victor)));
        AssertSameDog(shared, Assert.Single((await camille.GetFromJsonAsync<PublicUserResponse>($"/api/v1/users/{victorId}", Json, Ct))!.Dogs));
        AssertSameDog(shared, Assert.Single((await victor.GetFromJsonAsync<PublicUserResponse>($"/api/v1/users/{camilleSession.User.Id}", Json, Ct))!.Dogs));
        var stored = await WithDbAsync(db => db.DogInvitations.AsNoTracking().SingleAsync(Ct));
        Assert.Equal(camilleSession.User.Id, stored.UsedById);
        Assert.NotNull(stored.UsedAt);
    }

    [Fact]
    public async Task Unknown_malformed_revoked_used_and_expired_tokens_are_refused_in_the_order_of_the_spec()
    {
        var (victor, _, dog) = await DogOwnerAsync("victor");
        var (camille, _) = await SignedInAsync("camille", "Camille");
        var (bob, _) = await SignedInAsync("bob", "Bob");

        var unknown = DogInvitation.NewToken();
        await AssertProblemAsync(await AcceptInvitationAsync(camille, unknown), HttpStatusCode.NotFound, "invitation.not_found");
        await AssertProblemAsync(await PreviewAsync(camille, unknown), HttpStatusCode.NotFound, "invitation.not_found");
        await AssertProblemAsync(await AcceptInvitationAsync(camille, "not-a-token"), HttpStatusCode.NotFound, "invitation.not_found");
        await AssertProblemAsync(await PreviewAsync(camille, "not-a-token"), HttpStatusCode.NotFound, "invitation.not_found");

        var revoked = await InviteOkAsync(victor, dog.Id);
        await victor.DeleteAsync($"/api/v1/dogs/{dog.Id}/invitations/{revoked.Id}", Ct);
        await AssertProblemAsync(await AcceptInvitationAsync(camille, revoked.Token), HttpStatusCode.NotFound, "invitation.not_found");

        // Used, and expired since: « already used » comes first.
        var used = await InviteOkAsync(victor, dog.Id);
        Assert.Equal(HttpStatusCode.OK, (await AcceptInvitationAsync(camille, used.Token)).StatusCode);
        await AssertProblemAsync(await AcceptInvitationAsync(bob, used.Token), HttpStatusCode.Conflict, "invitation.already_used");
        await AssertProblemAsync(await PreviewAsync(bob, used.Token), HttpStatusCode.Conflict, "invitation.already_used");
        await ExpireAsync(used.Id);
        await AssertProblemAsync(await AcceptInvitationAsync(bob, used.Token), HttpStatusCode.Conflict, "invitation.already_used");

        // Expired, and accepted by someone already an owner: « expired » comes first.
        var expired = await InviteOkAsync(victor, dog.Id);
        await ExpireAsync(expired.Id);
        await AssertProblemAsync(await AcceptInvitationAsync(bob, expired.Token), HttpStatusCode.Gone, "invitation.expired");
        await AssertProblemAsync(await PreviewAsync(bob, expired.Token), HttpStatusCode.Gone, "invitation.expired");
        await AssertProblemAsync(await AcceptInvitationAsync(camille, expired.Token), HttpStatusCode.Gone, "invitation.expired");

        Assert.Equal(2, (await GetDogAsync(victor, dog.Id)).Members.Count);
    }

    [Fact]
    public async Task An_owner_cannot_accept_an_invitation_to_their_own_dog()
    {
        var (victor, _, dog) = await DogOwnerAsync("victor");
        var invitation = await InviteOkAsync(victor, dog.Id);

        await AssertProblemAsync(await AcceptInvitationAsync(victor, invitation.Token), HttpStatusCode.Conflict, "dog.already_owner");

        // Still unused: someone else can take it.
        var (camille, _) = await SignedInAsync("camille", "Camille");
        Assert.Equal(HttpStatusCode.OK, (await AcceptInvitationAsync(camille, invitation.Token)).StatusCode);
    }

    [Fact]
    public async Task Accepting_counts_against_the_limit_of_twenty_dogs()
    {
        var (victor, _, dog) = await DogOwnerAsync("victor");
        var invitation = await InviteOkAsync(victor, dog.Id);
        var (camille, _) = await SignedInAsync("camille", "Camille");
        for (var i = 0; i < Dog.MaxPerOwner; i++)
        {
            await CreateDogAsync(camille, ValidDog($"Dog {i}"));
        }

        await AssertProblemAsync(await AcceptInvitationAsync(camille, invitation.Token), HttpStatusCode.Conflict, "dog.limit_reached");

        // A shared dog counts for both humans: Victor, at 19 own dogs plus the shared one, is full.
        var (bob, _) = await SignedInAsync("bob", "Bob");
        await ShareAsync(victor, dog.Id, bob);
        for (var i = 1; i < Dog.MaxPerOwner; i++)
        {
            await CreateDogAsync(victor, ValidDog($"Victor {i}"));
        }

        await AssertProblemAsync(await victor.PostAsJsonAsync("/api/v1/dogs", ValidDog("One too many"), Ct), HttpStatusCode.Conflict, "dog.limit_reached");
        var bobDog = await CreateDogAsync(bob, ValidDog("Bob's"));
        var fromBob = await InviteOkAsync(bob, bobDog.Id);
        await AssertProblemAsync(await AcceptInvitationAsync(victor, fromBob.Token), HttpStatusCode.Conflict, "dog.limit_reached");
        Assert.Equal(2, (await MyDogsAsync(bob)).Count);
    }

    [Fact]
    public async Task A_dog_creation_and_an_acceptance_cannot_pass_the_limit_together()
    {
        var (victor, _, dog) = await DogOwnerAsync("victor");
        var (camille, camilleSession) = await SignedInAsync("camille", "Camille");
        var camilleId = camilleSession.User.Id;
        for (var i = 0; i < Dog.MaxPerOwner - 1; i++)
        {
            await CreateDogAsync(camille, ValidDog($"Dog {i}"));
        }

        for (var round = 0; round < 3; round++)
        {
            var invitation = await InviteOkAsync(victor, dog.Id);
            using var start = new SemaphoreSlim(0);
            var create = Task.Run(async () => { await start.WaitAsync(Ct); return await camille.PostAsJsonAsync("/api/v1/dogs", ValidDog("Racer"), Ct); }, Ct);
            var accept = Task.Run(async () => { await start.WaitAsync(Ct); return await AcceptInvitationAsync(camille, invitation.Token); }, Ct);
            start.Release(2);
            var responses = await Task.WhenAll(create, accept);

            Assert.Equal(1, responses.Count(r => r.IsSuccessStatusCode));
            Assert.Equal(Dog.MaxPerOwner, await WithDbAsync(db => db.DogMembers.CountAsync(m => m.UserId == camilleId, Ct)));

            // Back to 19 for the next round.
            var mine = await MyDogsAsync(camille);
            var extra = mine.SingleOrDefault(d => d.Id == dog.Id) ?? mine.Single(d => d.Name == "Racer");
            var response = extra.Id == dog.Id ? await LeaveAsync(camille, dog.Id) : await camille.DeleteAsync($"/api/v1/dogs/{extra.Id}", Ct);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
    }

    [Fact]
    public async Task Two_people_accepting_the_same_invitation_at_once_use_it_only_once()
    {
        var (victor, _, dog) = await DogOwnerAsync("victor");
        var invitation = await InviteOkAsync(victor, dog.Id);
        var (camille, _) = await SignedInAsync("camille", "Camille");
        var (bob, _) = await SignedInAsync("bob", "Bob");

        using var start = new SemaphoreSlim(0);
        var requests = new[] { camille, bob }.Select(client => Task.Run(async () =>
        {
            await start.WaitAsync(Ct);
            return await AcceptInvitationAsync(client, invitation.Token);
        }, Ct)).ToList();
        start.Release(2);
        var responses = await Task.WhenAll(requests);

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        await AssertProblemAsync(Assert.Single(responses, r => r.StatusCode != HttpStatusCode.OK), HttpStatusCode.Conflict, "invitation.already_used");
        Assert.Equal(2, (await GetDogAsync(victor, dog.Id)).Members.Count);
    }

    [Fact]
    public async Task An_invitation_survives_its_author_leaving_the_dog_or_deleting_their_account()
    {
        var (victor, _, camille, _, dog) = await CoupleAsync();
        var fromCamille = await InviteOkAsync(camille, dog.Id);
        var other = await InviteOkAsync(camille, dog.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await LeaveAsync(camille, dog.Id)).StatusCode);

        var (bob, _) = await SignedInAsync("bob", "Bob");
        Assert.Equal(HttpStatusCode.OK, (await AcceptInvitationAsync(bob, fromCamille.Token)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await camille.DeleteAsync("/api/v1/me", Ct)).StatusCode);
        var (zoe, _) = await SignedInAsync("zoe", "Zoé");
        var preview = (await zoe.GetFromJsonAsync<DogInvitationPreviewResponse>($"/api/v1/dog-invitations/{other.Token}", Json, Ct))!;
        Assert.Null(preview.InvitedBy);
        Assert.Equal(HttpStatusCode.OK, (await AcceptInvitationAsync(zoe, other.Token)).StatusCode);
        Assert.Equal(["victor", "Bob", "Zoé"], (await GetDogAsync(victor, dog.Id)).Members.Select(m => m.User.DisplayName));
    }

    [Fact]
    public async Task A_suspended_account_can_neither_invite_nor_accept()
    {
        var (victor, victorId, dog) = await DogOwnerAsync("victor");
        var invitation = await InviteOkAsync(victor, dog.Id);
        var (camille, camilleSession) = await SignedInAsync("camille", "Camille");
        var (admin, _) = await AdminAsync();
        await admin.PostAsync($"/api/v1/admin/users/{victorId}/suspend", null, Ct);
        await admin.PostAsync($"/api/v1/admin/users/{camilleSession.User.Id}/suspend", null, Ct);

        await AssertProblemAsync(await InviteAsync(victor, dog.Id), HttpStatusCode.Forbidden, "account.suspended");
        await AssertProblemAsync(await AcceptInvitationAsync(camille, invitation.Token), HttpStatusCode.Forbidden, "account.suspended");
    }
}
