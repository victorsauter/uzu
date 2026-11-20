using Dogo.Api.Features.Reports;

namespace Dogo.Api.Features.Notifications;

/// <summary>Preference that governs a notification type (spec §3, §4.4, third column).</summary>
public enum NotificationCategory
{
    Registrations,
    WalkChanges,
    Messages,
    Reminders,

    /// <summary>Notifications to administrators; not a user preference, always sent.</summary>
    Administration,
}

/// <summary>What the texts of a notification can use.</summary>
/// <param name="ActorName">Display name of who performed the action, or a neutral word if unknown.</param>
internal sealed record NotificationTextContext(NotificationSubject Subject, string ActorName, string? Detail)
{
    public string When => Subject.StartsAt is { } startsAt ? ParisTime.DayAndTime(startsAt) : "";

    public string Time => Subject.StartsAt is { } startsAt ? ParisTime.TimeOfDay(startsAt) : "";
}

internal sealed record NotificationText(string Title, string Body);

/// <summary>
/// Every notification type with its preference and its French texts, in one place (shown to
/// users, so in French; see CLAUDE.md). Adding a type is one line here.
/// </summary>
internal static class NotificationTexts
{
    private const int MessageExcerptLength = 120;

    private static readonly Dictionary<NotificationType, (NotificationCategory Category, Func<NotificationTextContext, NotificationText> Text)> Catalog = new()
    {
        [NotificationType.RegistrationCreated] = (NotificationCategory.Registrations, c => new("Nouvelle inscription", $"{c.ActorName} participera à « {c.Subject.Name} ».")),
        [NotificationType.RegistrationRequested] = (NotificationCategory.Registrations, c => new("Nouvelle demande", $"{c.ActorName} demande à participer à « {c.Subject.Name} ».")),
        [NotificationType.RegistrationAccepted] = (NotificationCategory.Registrations, c => new("Inscription acceptée", $"Ta participation à « {c.Subject.Name} » est confirmée.")),
        [NotificationType.RegistrationDeclined] = (NotificationCategory.Registrations, c => new("Inscription refusée", $"Ton inscription à « {c.Subject.Name} » n'a pas été retenue.")),
        [NotificationType.RegistrationCancelledByOwner] = (NotificationCategory.Registrations, c => new("Inscription annulée", $"{c.ActorName} a annulé l'inscription de {c.Detail ?? "ton chien"} à « {c.Subject.Name} ».")),
        [NotificationType.WalkUpdated] = (NotificationCategory.WalkChanges, c => new("Balade modifiée", $"« {c.Subject.Name} » a changé : prévue le {c.When}.")),
        [NotificationType.WalkCancelled] = (NotificationCategory.WalkChanges, c => new("Balade annulée", $"« {c.Subject.Name} », prévue le {c.When}, est annulée.")),
        [NotificationType.MessagePosted] = (NotificationCategory.Messages, c => new(c.Subject.Name, $"{c.ActorName} : {Excerpt(c.Detail)}")),
        [NotificationType.WalkReminder] = (NotificationCategory.Reminders, c => new("Balade bientôt", $"« {c.Subject.Name} » commence à {c.Time}.")),
        [NotificationType.SeriesWalkCreated] = (NotificationCategory.WalkChanges, c => new("Nouvelle date", $"« {c.Subject.Name} » revient le {c.When}. Inscris-toi à nouveau pour y participer.")),
        [NotificationType.MembershipRequested] = (NotificationCategory.Registrations, c => new("Nouvelle demande d'adhésion", $"{c.ActorName} demande à rejoindre « {c.Subject.Name} ».")),
        [NotificationType.MembershipAccepted] = (NotificationCategory.Registrations, c => new("Adhésion acceptée", $"Tu fais maintenant partie de « {c.Subject.Name} ».")),
        [NotificationType.OrganizationApproved] = (NotificationCategory.Registrations, c => new("Organisation validée", $"« {c.Subject.Name} » est validée : tu peux y proposer des balades.")),
        // Shown on the lock screen: the category only, never the facts nor who is involved.
        [NotificationType.ReportCreated] = (NotificationCategory.Administration, c => new("Nouveau signalement", $"Un signalement « {c.Subject.Name} » attend d'être examiné.")),
    };

    /// <summary>French labels of the report categories (spec §2).</summary>
    private static readonly Dictionary<ReportType, string> ReportTypeLabels = new()
    {
        [ReportType.IncidentNoInjury] = "Incident sans blessure",
        [ReportType.Altercation] = "Altercation",
        [ReportType.BiteOrInjury] = "Morsure ou blessure",
        [ReportType.HumanBehavior] = "Comportement d'un humain",
    };

    public static string ReportTypeLabel(ReportType type) => ReportTypeLabels.GetValueOrDefault(type, "Autre");

    /// <summary>Shown when the actor's account no longer exists.</summary>
    public const string UnknownActor = "Quelqu'un";

    public static NotificationCategory CategoryOf(NotificationType type) => Catalog[type].Category;

    public static NotificationText For(NotificationType type, NotificationTextContext context) => Catalog[type].Text(context);

    /// <summary>Types without texts; empty unless a type was added without its line (checked by a test).</summary>
    public static IEnumerable<NotificationType> Missing => Enum.GetValues<NotificationType>().Where(t => !Catalog.ContainsKey(t));

    private static string Excerpt(string? text)
    {
        var value = (text ?? "").ReplaceLineEndings(" ").Trim();
        return value.Length <= MessageExcerptLength ? value : string.Concat(value.AsSpan(0, MessageExcerptLength - 1).TrimEnd(), "…");
    }
}

/// <summary>Dates as shown in notifications, in the pilot's time zone (spec §7), e.g. « jeudi 26 novembre à 10:00 ».</summary>
internal static class ParisTime
{
    private static readonly string[] Days = ["dimanche", "lundi", "mardi", "mercredi", "jeudi", "vendredi", "samedi"];

    private static readonly string[] Months =
        ["janvier", "février", "mars", "avril", "mai", "juin", "juillet", "août", "septembre", "octobre", "novembre", "décembre"];

    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(Walks.WalkSeries.DefaultTimeZone);

    // Built by hand: the API runs with invariant globalization (no French culture data).
    public static string DayAndTime(DateTimeOffset value)
    {
        var local = TimeZoneInfo.ConvertTime(value, Zone);
        return $"{Days[(int)local.DayOfWeek]} {local.Day} {Months[local.Month - 1]} à {TimeOfDay(value)}";
    }

    public static string TimeOfDay(DateTimeOffset value)
    {
        var local = TimeZoneInfo.ConvertTime(value, Zone);
        return $"{local.Hour:00}:{local.Minute:00}";
    }
}
