using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dogo.Api.Features.Auth;
using Dogo.Api.Infrastructure.Http;
using Dogo.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Dogo.Api.Tests.Common;

/// <summary>
/// ADR 0031: per-IP limit on <c>/auth</c>, per-user limits on writes, stricter on writes that
/// notify others or fill the disk; 429 <c>rate_limit.exceeded</c> with <c>Retry-After</c>. The
/// rest of the suite runs with rate limiting off; each test here starts its own host.
/// </summary>
public sealed class RateLimitingTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    private DogoApiFactory Limited(params (string Key, string? Value)[] settings)
    {
        var values = new Dictionary<string, string?>
        {
            ["RateLimiting:Enabled"] = "true",
            ["RateLimiting:Auth:PermitLimit"] = "1000",
            ["RateLimiting:Writes:PermitLimit"] = "1000",
            ["RateLimiting:SensitiveWrites:PermitLimit"] = "1000",
        };
        foreach (var (key, value) in settings)
        {
            values[key] = value;
        }

        return new DogoApiFactory(Fixture.ConnectionString, values);
    }

    private static async Task<HttpClient> SignedInAsync(DogoApiFactory factory, string subject)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/dev", new { subject, displayName = subject }, Ct);
        var session = (await response.Content.ReadFromJsonAsync<SessionResponse>(Json, Ct))!;
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return client;
    }

    /// <summary>A refresh attempt from a given connection address, optionally through a proxy header.</summary>
    internal static Task<HttpContext> RefreshFromAsync(DogoApiFactory factory, string remoteAddress, string? forwardedFor = null) =>
        factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(remoteAddress);
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/v1/auth/refresh";
            context.Request.ContentType = "application/json";
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("""{"refreshToken":"unknown"}"""));
            if (forwardedFor is not null)
            {
                context.Request.Headers["X-Forwarded-For"] = forwardedFor;
            }
        }, Ct);

    private static async Task AssertRateLimitedAsync(HttpResponseMessage response)
    {
        var problem = await AssertProblemAsync(response, HttpStatusCode.TooManyRequests, "rate_limit.exceeded");
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("title").GetString()));
        var retryAfter = Assert.Single(response.Headers.GetValues("Retry-After"));
        Assert.True(int.Parse(retryAfter, System.Globalization.CultureInfo.InvariantCulture) >= 1, retryAfter);
    }

    [Fact]
    public void Rate_limiting_is_on_by_default_with_limits_for_normal_use()
    {
        var options = new RateLimitingOptions();

        Assert.True(options.Enabled);
        Assert.True(options.Auth.PermitLimit >= 30 && options.Auth.Window == TimeSpan.FromMinutes(1));
        Assert.True(options.Writes.PermitLimit >= 30 && options.Writes.Window == TimeSpan.FromMinutes(1));
        Assert.True(options.SensitiveWrites.PermitLimit < options.Writes.PermitLimit * options.SensitiveWrites.Window.TotalMinutes);
    }

    [Fact]
    public async Task Auth_routes_are_limited_per_client_address()
    {
        await using var factory = Limited(("RateLimiting:Auth:PermitLimit", "2"));

        Assert.NotEqual(StatusCodes.Status429TooManyRequests, (await RefreshFromAsync(factory, "198.51.100.1")).Response.StatusCode);
        Assert.NotEqual(StatusCodes.Status429TooManyRequests, (await RefreshFromAsync(factory, "198.51.100.1")).Response.StatusCode);
        var rejected = await RefreshFromAsync(factory, "198.51.100.1");

        Assert.Equal(StatusCodes.Status429TooManyRequests, rejected.Response.StatusCode);
        Assert.Equal("application/problem+json", rejected.Response.ContentType);
        Assert.True(int.Parse(rejected.Response.Headers.RetryAfter.ToString(), System.Globalization.CultureInfo.InvariantCulture) >= 1);
        using var body = await JsonDocument.ParseAsync(rejected.Response.Body, cancellationToken: Ct);
        Assert.Equal("rate_limit.exceeded", body.RootElement.GetProperty("code").GetString());
        Assert.NotEqual(StatusCodes.Status429TooManyRequests, (await RefreshFromAsync(factory, "198.51.100.2")).Response.StatusCode);
    }

    [Fact]
    public async Task Ipv6_clients_are_grouped_by_64_bit_prefix()
    {
        await using var factory = Limited(("RateLimiting:Auth:PermitLimit", "1"));

        Assert.NotEqual(StatusCodes.Status429TooManyRequests, (await RefreshFromAsync(factory, "2001:db8:1:2::1")).Response.StatusCode);
        Assert.Equal(StatusCodes.Status429TooManyRequests, (await RefreshFromAsync(factory, "2001:db8:1:2::ffff")).Response.StatusCode);
        Assert.NotEqual(StatusCodes.Status429TooManyRequests, (await RefreshFromAsync(factory, "2001:db8:1:3::1")).Response.StatusCode);
    }

    [Fact]
    public async Task Writes_are_limited_per_user_and_reads_are_not()
    {
        await using var factory = Limited(("RateLimiting:Writes:PermitLimit", "2"));
        var alice = await SignedInAsync(factory, "alice");
        var bob = await SignedInAsync(factory, "bob");

        Assert.Equal(HttpStatusCode.Created, (await alice.PostAsJsonAsync("/api/v1/dogs", ValidDog("Rex"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await alice.PostAsJsonAsync("/api/v1/dogs", ValidDog("Max"), Ct)).StatusCode);

        await AssertRateLimitedAsync(await alice.PostAsJsonAsync("/api/v1/dogs", ValidDog("Rox"), Ct));
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync("/api/v1/me/dogs", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await bob.PostAsJsonAsync("/api/v1/dogs", ValidDog("Rex"), Ct)).StatusCode);
    }

    [Fact]
    public async Task Sensitive_writes_have_a_stricter_limit()
    {
        await using var factory = Limited(("RateLimiting:SensitiveWrites:PermitLimit", "1"));
        var alice = await SignedInAsync(factory, "alice");

        var device = new { expoPushToken = "ExponentPushToken[alice]", platform = "Ios" };
        Assert.Equal(HttpStatusCode.OK, (await alice.PutAsJsonAsync("/api/v1/me/devices", device, Ct)).StatusCode);

        await AssertRateLimitedAsync(await alice.PutAsJsonAsync("/api/v1/me/devices", device, Ct));
        await AssertRateLimitedAsync(await alice.PostAsJsonAsync("/api/v1/reports", new { type = "HumanBehavior", facts = "Des faits précis." }, Ct));
        // Other writes only count against the general limit.
        Assert.Equal(HttpStatusCode.Created, (await alice.PostAsJsonAsync("/api/v1/dogs", ValidDog(), Ct)).StatusCode);
    }

    [Fact]
    public async Task Dog_invitation_tokens_cannot_be_tried_in_bulk()
    {
        // ADR 0042: looking at an invitation is a read, but it takes a token: limited like a sensitive write.
        await using var factory = Limited(("RateLimiting:SensitiveWrites:PermitLimit", "2"));
        var alice = await SignedInAsync(factory, "alice");

        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/v1/dog-invitations/{Guid.NewGuid():N}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.PostAsync($"/api/v1/dog-invitations/{Guid.NewGuid():N}/accept", null, Ct)).StatusCode);

        await AssertRateLimitedAsync(await alice.GetAsync($"/api/v1/dog-invitations/{Guid.NewGuid():N}", Ct));
        // Other reads are not concerned.
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync("/api/v1/me/dogs", Ct)).StatusCode);
    }

    [Fact]
    public void Every_sensitive_endpoint_name_exists()
    {
        // The sensitive writes are matched by operation id: a renamed endpoint must not slip out.
        var names = Factory.Services.GetServices<Microsoft.AspNetCore.Routing.EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .Select(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName)
            .ToHashSet();

        Assert.All(RateLimiting.SensitiveEndpoints, name => Assert.Contains(name, names));
    }
}
