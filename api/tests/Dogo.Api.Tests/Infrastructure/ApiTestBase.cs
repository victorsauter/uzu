using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dogo.Api.Features.Auth;
using Dogo.Api.Features.Dogs;
using Dogo.Api.Features.Notifications;
using Dogo.Api.Features.Users;
using Dogo.Api.Infrastructure.Push;
using Dogo.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dogo.Api.Tests.Infrastructure;

/// <summary>Base class of integration tests: fresh database per test and HTTP helpers.</summary>
public abstract class ApiTestBase(ApiFixture fixture) : IAsyncLifetime
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    protected ApiFixture Fixture => fixture;

    protected DogoApiFactory Factory => fixture.Factory;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        // Notifications of the previous test must not leak into this one.
        await WaitForNotificationsAsync();
        await fixture.ResetAsync();
        Factory.PushSender.Clear();
        Factory.ReportNotifier.Clear();
        Factory.DemoPhotos.Reset();
    }

    /// <summary>Push notifications that would have been sent, in order.</summary>
    protected IReadOnlyList<PushMessage> Pushes => Factory.PushSender.Sent;

    /// <summary>Waits until the background dispatcher has processed every queued notification.</summary>
    protected Task WaitForNotificationsAsync() =>
        Factory.Services.GetRequiredService<NotificationQueue>().WaitUntilIdleAsync(TimeSpan.FromSeconds(10), Ct);

    /// <summary>Notifications sent once the queue is drained.</summary>
    protected async Task<IReadOnlyList<PushMessage>> SentPushesAsync()
    {
        await WaitForNotificationsAsync();
        return Pushes;
    }

    /// <summary>Registers a push device for the client's user; the token defaults to one derived from <paramref name="name"/>.</summary>
    protected static async Task<string> RegisterDeviceAsync(HttpClient client, string name, string platform = "Ios")
    {
        var token = $"ExponentPushToken[{name}]";
        var response = await client.PutAsJsonAsync("/api/v1/me/devices", new { expoPushToken = token, platform }, Ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return token;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    protected HttpClient CreateClient(string? accessToken = null)
    {
        var client = Factory.CreateClient();
        if (accessToken is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return client;
    }

    protected async Task<SessionResponse> SignInDevAsync(string subject = "alice", string displayName = "Alice", string? email = null)
    {
        var response = await CreateClient().PostAsJsonAsync("/api/v1/auth/dev", new { subject, displayName, email }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SessionResponse>(Json, Ct))!;
    }

    /// <summary>Signs in a dev account whose e-mail is in <c>Admin:Emails</c>.</summary>
    protected async Task<(HttpClient Client, SessionResponse Session)> AdminAsync(string subject = "admin")
    {
        var session = await SignInDevAsync(subject, "Admin", DogoApiFactory.AdminEmail);
        return (CreateClient(session.AccessToken), session);
    }

    /// <summary>Signs in a dev account and returns a client carrying its access token.</summary>
    protected async Task<(HttpClient Client, SessionResponse Session)> SignedInAsync(string subject = "alice", string displayName = "Alice", string? email = null)
    {
        var session = await SignInDevAsync(subject, displayName, email);
        return (CreateClient(session.AccessToken), session);
    }

    protected static object ValidDog(string name = "Rex") => new
    {
        name,
        size = "Medium",
        birthDate = "2021-04-12",
        breed = "Border collie",
        sociability = "Easygoing",
        reactivity = "None",
        leashHabit = "OnLeash",
        description = "Loves sticks.",
    };

    protected static async Task<DogResponse> CreateDogAsync(HttpClient client, object? body = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/dogs", body ?? ValidDog(), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DogResponse>(Json, Ct))!;
    }

    /// <summary>
    /// Same dog, same humans. <see cref="DogResponse"/> is a record, but its <c>members</c> list is
    /// compared by reference, so two equal responses are not <c>Equal</c> as a whole.
    /// </summary>
    protected static void AssertSameDog(DogResponse expected, DogResponse? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Members, actual.Members);
        Assert.Equal(expected with { Members = [] }, actual with { Members = [] });
    }

    /// <summary>The dog has exactly one human, its owner, with the <c>Owner</c> role (ADR 0042).</summary>
    protected static void AssertOwnedBy(DogResponse dog, Guid userId, string displayName, string? photoUrl = null)
    {
        var member = Assert.Single(dog.Members);
        Assert.Equal(new DogMemberResponse(new UserSummaryResponse(userId, displayName, photoUrl), DogMemberRole.Owner), member);
    }

    /// <summary>Gives a user a profile photo without going through an upload; the file need not exist.</summary>
    protected Task SetUserPhotoAsync(Guid userId, string fileName) =>
        WithDbAsync(db => db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.PhotoFileName, fileName), Ct));

    protected async Task WithDbAsync(Func<DogoDbContext, Task> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<DogoDbContext>());
    }

    protected async Task<T> WithDbAsync<T>(Func<DogoDbContext, Task<T>> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<DogoDbContext>());
    }

    protected static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);

    /// <summary>Asserts an RFC 9457 problem with the given status and code, and returns it.</summary>
    protected static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(status == response.StatusCode, $"Expected {status}, got {response.StatusCode}: {body}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = JsonDocument.Parse(body).RootElement;
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        return problem;
    }

    /// <summary>Asserts a 400 <c>validation.failed</c> problem whose errors include each field.</summary>
    protected static async Task AssertValidationProblemAsync(HttpResponseMessage response, params string[] fields)
    {
        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation.failed");
        var errors = problem.GetProperty("errors");
        foreach (var field in fields)
        {
            Assert.True(errors.TryGetProperty(field, out _), $"Expected an error for '{field}' in {errors}");
        }
    }
}
