using System.Net;
using System.Net.Http.Json;
using Dogo.Api.Features.Dogs;
using Dogo.Api.Tests.Infrastructure;
using Dogo.Api.Tests.Walks;

namespace Dogo.Api.Tests.Dogs;

/// <summary>Helpers for dogs shared by several humans (ADR 0042).</summary>
public abstract class SharedDogTestBase(ApiFixture fixture) : WalkTestBase(fixture)
{
    protected static Task<HttpResponseMessage> InviteAsync(HttpClient client, Guid dogId, string role = "Owner") =>
        client.PostAsJsonAsync($"/api/v1/dogs/{dogId}/invitations", new { role }, Ct);

    /// <summary>Creates an invitation and asserts success.</summary>
    protected static async Task<CreatedDogInvitationResponse> InviteOkAsync(HttpClient client, Guid dogId)
    {
        var response = await InviteAsync(client, dogId);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<CreatedDogInvitationResponse>(Json, Ct))!;
    }

    protected static Task<HttpResponseMessage> AcceptInvitationAsync(HttpClient client, string token) =>
        client.PostAsync($"/api/v1/dog-invitations/{token}/accept", null, Ct);

    protected static Task<HttpResponseMessage> LeaveAsync(HttpClient client, Guid dogId) =>
        client.DeleteAsync($"/api/v1/dogs/{dogId}/members/me", Ct);

    /// <summary>The owner of <paramref name="dogId"/> invites <paramref name="invitee"/>, who accepts.</summary>
    protected static async Task<DogResponse> ShareAsync(HttpClient owner, Guid dogId, HttpClient invitee)
    {
        var invitation = await InviteOkAsync(owner, dogId);
        var response = await AcceptInvitationAsync(invitee, invitation.Token);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<DogResponse>(Json, Ct))!;
    }

    protected static async Task<DogResponse> GetDogAsync(HttpClient client, Guid dogId) =>
        (await client.GetFromJsonAsync<DogResponse>($"/api/v1/dogs/{dogId}", Json, Ct))!;

    protected static async Task<List<DogResponse>> MyDogsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<DogResponse>>("/api/v1/me/dogs", Json, Ct))!;

    /// <summary>Two people who share one medium dog, created by the first.</summary>
    protected async Task<(HttpClient Owner, Guid OwnerId, HttpClient CoOwner, Guid CoOwnerId, DogResponse Dog)> CoupleAsync(
        string owner = "victor", string coOwner = "camille")
    {
        var (ownerClient, ownerId, dog) = await DogOwnerAsync(owner);
        var (coOwnerClient, coOwnerSession) = await SignedInAsync(coOwner, coOwner);
        var shared = await ShareAsync(ownerClient, dog.Id, coOwnerClient);
        return (ownerClient, ownerId, coOwnerClient, coOwnerSession.User.Id, shared);
    }
}
