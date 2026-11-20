using System.Text.Json;
using Dogo.Api.Features.Organizations;
using Dogo.Api.Features.Reports;
using Dogo.Api.Features.Walks;

namespace Dogo.Api.Features.Notifications;

/// <summary>
/// Events that notify someone (spec §4.4). Sent to the app as <c>data.type</c>, in snake_case
/// (<see cref="NotificationTypes.WireName"/>). Each value has its preference and French texts
/// in <see cref="NotificationTexts"/>.
/// </summary>
public enum NotificationType
{
    /// <summary>To the organizer: a dog was registered to an <c>Open</c> walk.</summary>
    RegistrationCreated,

    /// <summary>To the organizer: a registration request on an <c>Approval</c> walk.</summary>
    RegistrationRequested,

    /// <summary>To the author of the registration.</summary>
    RegistrationAccepted,

    /// <summary>To the author of the registration (request declined or participant removed).</summary>
    RegistrationDeclined,

    /// <summary>
    /// To the author of the registration: another owner of the dog cancelled it (ADR 0042). The
    /// detail is the dog's name.
    /// </summary>
    RegistrationCancelledByOwner,

    /// <summary>To accepted and pending registrants.</summary>
    WalkUpdated,

    /// <summary>To accepted and pending registrants.</summary>
    WalkCancelled,

    /// <summary>To the organizer and accepted registrants, except the author.</summary>
    MessagePosted,

    /// <summary>To the organizer and accepted registrants, 2 hours before the start.</summary>
    WalkReminder,

    /// <summary>To the accepted registrants of the previous walk of the series.</summary>
    SeriesWalkCreated,

    /// <summary>To the owner and educators of an organisation: someone asks to join.</summary>
    MembershipRequested,

    /// <summary>To the user whose join request was accepted.</summary>
    MembershipAccepted,

    /// <summary>To the owner of an organisation an administrator approved.</summary>
    OrganizationApproved,

    /// <summary>
    /// To the administrators (except the reporter): a report was created (ADR 0036). Not a walk
    /// notification: sent whatever the preferences, without deep link (no screen in V1).
    /// </summary>
    ReportCreated,
}

/// <summary>What a notification is about; becomes <c>data</c> and the deep link opened by the app.</summary>
/// <param name="IdKey">Name of the id in <c>data</c>, e.g. <c>walkId</c>.</param>
/// <param name="Path">Deep link path after <c>uzu://</c>, e.g. <c>walks/{id}</c>; null for no link (no <c>url</c> in <c>data</c>).</param>
/// <param name="Name">Shown in the texts, e.g. the walk title.</param>
/// <param name="StartsAt">Start of the walk, for texts that show it.</param>
public sealed record NotificationSubject(string IdKey, Guid Id, string? Path, string Name, DateTimeOffset? StartsAt = null)
{
    public static NotificationSubject ForWalk(Walk walk) => new("walkId", walk.Id, $"walks/{walk.Id}", walk.Title, walk.StartsAt);

    public static NotificationSubject ForOrganization(Organization organization) =>
        new("organizationId", organization.Id, $"organizations/{organization.Id}", organization.Name);

    /// <summary>A report: no link, and only its category in the texts (shown on the lock screen).</summary>
    public static NotificationSubject ForReport(Report report) =>
        new("reportId", report.Id, null, NotificationTexts.ReportTypeLabel(report.Type));
}

/// <summary>
/// An event to notify: <see cref="NotificationService"/> drops the actor, duplicates and users
/// whose preferences refuse this type, then sends to every device of the others.
/// </summary>
/// <param name="ActorId">Who performed the action (never notified); null for scheduled events.</param>
/// <param name="Detail">Extra text for some types, e.g. the message for <see cref="NotificationType.MessagePosted"/>.</param>
public sealed record NotificationRequest(
    NotificationType Type,
    NotificationSubject Subject,
    Guid? ActorId,
    IReadOnlyCollection<Guid> RecipientIds,
    string? Detail = null);

public static class NotificationTypes
{
    /// <summary><c>data.type</c> value, e.g. <c>message_posted</c>.</summary>
    public static string WireName(this NotificationType type) => JsonNamingPolicy.SnakeCaseLower.ConvertName(type.ToString());
}
