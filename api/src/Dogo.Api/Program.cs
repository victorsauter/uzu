using System.Text.Json.Serialization;
using Dogo.Api.Common;
using Dogo.Api.Features.Admin;
using Dogo.Api.Features.Auth;
using Dogo.Api.Features.DemoData;
using Dogo.Api.Features.Blocks;
using Dogo.Api.Features.Dogs;
using Dogo.Api.Features.Health;
using Dogo.Api.Features.Messages;
using Dogo.Api.Features.Notifications;
using Dogo.Api.Features.Organizations;
using Dogo.Api.Features.Photos;
using Dogo.Api.Features.Reports;
using Dogo.Api.Features.Users;
using Dogo.Api.Features.Walks;
using Dogo.Api.Infrastructure.Auth;
using Dogo.Api.Infrastructure.Hosting;
using Dogo.Api.Infrastructure.Http;
using Dogo.Api.Infrastructure.Jobs;
using Dogo.Api.Infrastructure.Persistence;
using Dogo.Api.Infrastructure.Push;
using Dogo.Api.Infrastructure.Storage;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Web defaults (camelCase) + enums as strings (names only, never integers) + numbers as JSON numbers only.
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});
builder.Services.AddDogoProblemDetails();
builder.Services.AddValidation();
builder.Services.AddDogoOpenApi();
builder.Services.AddSingleton(TimeProvider.System);

// Hardening (ADR 0031): deployment checks, proxy headers, HSTS, body size, rate limits.
builder.Services.AddDogoStartupChecks();
builder.Services.AddDogoHttpHardening();
builder.Services.AddDogoRateLimiting();

builder.Services.AddDogoPersistence();
builder.Services.AddDogoAuth();
builder.Services.AddDogoPhotoStorage();
builder.Services.AddDogoPhotoProcessing();

builder.Services.AddDogoPush();

// Feature services.
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<AccountDeletion>();
builder.Services.AddScoped<AccountSuspension>();

// Demo data, development only (DemoData:Enabled, refused in Production, ADR 0027).
builder.Services.AddDogoDemoData();

// Reports tell the administration through an interface: a log line and a push to the administrators (ADR 0036).
builder.Services.AddScoped<IReportNotifier, AdminReportNotifier>();

// Notifications: in-process queue read by a background dispatcher (ADR 0019).
builder.Services.AddSingleton<NotificationQueue>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddHostedService<NotificationDispatcher>();

// Scheduled jobs: plain services run by a periodic loop (BackgroundJobs:Enabled, BackgroundJobs:Interval).
builder.Services.AddScoped<WalkReminderJob>();
builder.Services.AddScoped<WalkSeriesJob>();
builder.Services.AddScoped<RefreshTokenPurgeJob>();
builder.Services.AddOptions<BackgroundJobsOptions>().BindConfiguration(BackgroundJobsOptions.SectionName);
builder.Services.AddHostedService<BackgroundJobsService>();

var app = builder.Build();

app.UseDogoHttpHardening();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseDogoRequestBodyLimit();
app.UseAuthentication();
// After authentication (limits per user) and before the account lookup of AccountStatusMiddleware.
app.UseRateLimiter();
app.UseAuthorization();
app.UseMiddleware<AccountStatusMiddleware>();

if (app.Environment.IsDevelopment())
{
    // Live document at /openapi/v1.json; the committed api/openapi.json is generated at build time.
    app.MapOpenApi().AllowAnonymous();
}

// Enum query parameters take member names only, and date ones an explicit offset, like in bodies.
var api = app.MapGroup("/api/v1").RequireDefinedEnumQueryValues().RequireExplicitOffsetDateQueryValues();
api.MapHealthEndpoints();

// Development routes exist only where the environment allows them and their option is on (ADR 0031); everywhere
// else they are not declared and answer 404 like any unknown route, whatever the body or the caller.
// openapi.json is generated in Development, where both are on.
var devTools = app.Environment.AllowsDevTools();
var mapDevSignIn = BuildContext.IsOpenApiGeneration || devTools && app.Services.GetRequiredService<IOptions<AuthOptions>>().Value.AllowDevLogin;
var mapDemoData = BuildContext.IsOpenApiGeneration || devTools && app.Services.GetRequiredService<IOptions<DemoDataOptions>>().Value.Enabled;
api.MapAuthEndpoints(mapDevSignIn);
if (mapDemoData)
{
    api.MapDemoDataEndpoints();
}

// Everything else requires a valid access token (fallback authorization policy).
var authenticated = api.MapGroup("").ProducesProblem(StatusCodes.Status401Unauthorized);
authenticated.MapUserEndpoints();
authenticated.MapDogEndpoints();
authenticated.MapDogInvitationEndpoints();
authenticated.MapPhotoEndpoints();
authenticated.MapWalkEndpoints();
authenticated.MapRegistrationEndpoints();
authenticated.MapMessageEndpoints();
authenticated.MapNotificationEndpoints();
authenticated.MapOrganizationEndpoints();
authenticated.MapMembershipEndpoints();
authenticated.MapBlockEndpoints();
authenticated.MapReportEndpoints();
authenticated.MapAdminEndpoints();

app.MapPhotoFiles();

// Migrations at startup in Development and in the hosted Demo (ADR 0043: the free host has no pre-deploy step);
// elsewhere the migration bundle runs at each deployment (ADR 0038).
if ((app.Environment.IsDevelopment() || app.Environment.IsEnvironment(DogoEnvironments.Demo)) && !BuildContext.IsOpenApiGeneration)
{
    // Reads the deployment checks first (they otherwise run when the host starts, after this): an empty connection
    // string is then reported by name instead of as a null argument of the migration.
    _ = app.Services.GetRequiredService<IOptions<StartupChecksOptions>>().Value;
    await app.MigrateDatabaseAsync();
}

await app.RunAsync();
