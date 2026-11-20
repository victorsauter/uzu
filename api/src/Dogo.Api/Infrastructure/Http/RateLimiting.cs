using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Dogo.Api.Common;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Dogo.Api.Infrastructure.Http;

/// <summary>One fixed-window limit: <see cref="PermitLimit"/> requests per <see cref="Window"/>.</summary>
public sealed class RateLimit
{
    public int PermitLimit { get; set; }

    public TimeSpan Window { get; set; }
}

/// <summary>Request rate limits (<c>RateLimiting</c> section, ADR 0031). Counters are kept in memory, per instance.</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; set; } = true;

    /// <summary><c>/api/v1/auth/*</c>, per client IP address (IPv6: per /64): sign-in calls Apple and Google.</summary>
    public RateLimit Auth { get; set; } = new() { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) };

    /// <summary>Every write (POST, PUT, PATCH, DELETE) outside <c>/auth</c>, per user (per IP address when anonymous).</summary>
    public RateLimit Writes { get; set; } = new() { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) };

    /// <summary>
    /// Writes that notify other people or fill the disk, and routes that take a dog invitation token
    /// (<see cref="RateLimiting.SensitiveEndpoints"/>), per user, on top of <see cref="Writes"/>.
    /// </summary>
    public RateLimit SensitiveWrites { get; set; } = new() { PermitLimit = 30, Window = TimeSpan.FromMinutes(10) };
}

public static class RateLimiting
{
    /// <summary>
    /// Endpoint names (operation ids) of the sensitive requests: registrations, messages, membership
    /// requests and reports notify other people; photos fill the disk; devices are stored per user;
    /// dog invitations hand out a token, and the routes taking a token (reading included) are
    /// limited so that tokens cannot be tried in bulk (ADR 0042). Matched by name so that feature
    /// files stay free of rate limiting concerns.
    /// </summary>
    public static readonly IReadOnlySet<string> SensitiveEndpoints = new HashSet<string>(StringComparer.Ordinal)
    {
        "createRegistrations",
        "postMessage",
        "requestMembership",
        "createReport",
        "uploadMyPhoto",
        "uploadDogPhoto",
        "registerDevice",
        "createDogInvitation",
        "getDogInvitation",
        "acceptDogInvitation",
    };

    private const string AuthPathPrefix = "/api/v1/auth";

    public static IServiceCollection AddDogoRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimitingOptions>().BindConfiguration(RateLimitingOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<RateLimitingOptions>, RateLimitingOptionsValidator>();
        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<RateLimitingOptions>>((limiter, dogoOptions) =>
        {
            var limits = dogoOptions.Value;
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = (context, _) => RejectAsync(context, limits);
            if (!limits.Enabled)
            {
                return;
            }

            // A request needs a permit from every limiter that applies to it.
            limiter.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    IsAuth(context.Request) ? Fixed("auth:" + ClientAddress(context), limits.Auth) : NoLimit),
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    IsWrite(context.Request) && !IsAuth(context.Request) ? Fixed("write:" + Caller(context), limits.Writes) : NoLimit),
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    IsSensitive(context) ? Fixed("sensitive:" + Caller(context), limits.SensitiveWrites) : NoLimit));
        });
        return services;
    }

    private static readonly RateLimitPartition<string> NoLimit = RateLimitPartition.GetNoLimiter("none");

    private static RateLimitPartition<string> Fixed(string key, RateLimit limit) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limit.PermitLimit,
            Window = limit.Window,
            QueueLimit = 0,
            AutoReplenishment = true,
        });

    private static bool IsAuth(HttpRequest request) =>
        request.Path.StartsWithSegments(AuthPathPrefix, StringComparison.OrdinalIgnoreCase);

    private static bool IsWrite(HttpRequest request) =>
        HttpMethods.IsPost(request.Method) || HttpMethods.IsPut(request.Method)
        || HttpMethods.IsPatch(request.Method) || HttpMethods.IsDelete(request.Method);

    /// <summary>Listed in <see cref="SensitiveEndpoints"/>, whatever the method (a token lookup is a read).</summary>
    private static bool IsSensitive(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName is { } name
        && SensitiveEndpoints.Contains(name);

    /// <summary>The signed-in user (authentication runs first), or the client address.</summary>
    private static string Caller(HttpContext context) =>
        context.User.TryGetUserId() is { } userId ? "user:" + userId.ToString("N") : "ip:" + ClientAddress(context);

    /// <summary>
    /// The client address, as restored by the forwarded headers middleware from trusted proxies only.
    /// IPv6 clients are grouped by /64, the block usually given to one subscriber.
    /// </summary>
    internal static string ClientAddress(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            Array.Clear(bytes, 8, 8);
            return new IPAddress(bytes) + "/64";
        }

        return address.ToString();
    }

    /// <summary>429 <c>rate_limit.exceeded</c> problem with a <c>Retry-After</c> header in seconds.</summary>
    private static async ValueTask RejectAsync(OnRejectedContext context, RateLimitingOptions limits)
    {
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var value) && value > TimeSpan.Zero
            ? value
            : new[] { limits.Auth.Window, limits.Writes.Window, limits.SensitiveWrites.Window }.Max();
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        var http = context.HttpContext;
        http.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        await Problems.Create(
                StatusCodes.Status429TooManyRequests,
                ErrorCodes.RateLimitExceeded,
                "Too many requests.",
                $"Retry in {seconds} seconds.")
            .ExecuteAsync(http);
    }

    private sealed class RateLimitingOptionsValidator : IValidateOptions<RateLimitingOptions>
    {
        public ValidateOptionsResult Validate(string? name, RateLimitingOptions options)
        {
            var failures = new[] { ("Auth", options.Auth), ("Writes", options.Writes), ("SensitiveWrites", options.SensitiveWrites) }
                .Where(l => l.Item2.PermitLimit < 1 || l.Item2.Window <= TimeSpan.Zero)
                .Select(l => $"RateLimiting:{l.Item1} needs a PermitLimit of at least 1 and a positive Window.")
                .ToList();
            return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
        }
    }
}
