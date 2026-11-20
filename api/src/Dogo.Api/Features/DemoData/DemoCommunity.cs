using Dogo.Api.Features.Organizations;
using Dogo.Api.Features.Reports;

namespace Dogo.Api.Features.DemoData;

/// <summary>
/// A demo organisation. Its members are drawn from the owners (deterministically, see
/// <see cref="DemoDataBuilder"/>); <see cref="ExtraMembers"/> are other people with a <c>Member</c> role.
/// </summary>
internal sealed record DemoOrganization(
    string Key,
    string Name,
    string Description,
    string City,
    OrganizationStatus Status,
    string Owner,
    string[] Educators,
    int MemberCount,
    int PendingRequestCount,
    string[]? ExtraMembers = null);

/// <summary><see cref="Blocker"/> blocked <see cref="Blocked"/> (person keys).</summary>
internal sealed record DemoBlock(string Blocker, string Blocked, int DaysAgo);

/// <summary>A report for the administration. The dog is given as (owner key, dog name), the walk by its template key.</summary>
internal sealed record DemoReport(
    string Reporter,
    ReportType Type,
    ReportStatus Status,
    string Facts,
    int DaysAgo,
    string? TargetUser = null,
    (string Owner, string Name)? TargetDog = null,
    string? Walk = null,
    string? AdminNote = null);

/// <summary>
/// A dog of <see cref="Owner"/> (person key, dog name) that <see cref="CoOwner"/> also owns (ADR 0042),
/// linked <see cref="DaysAgo"/> days ago, as if an invitation had been accepted.
/// </summary>
internal sealed record DemoSharedDog(string Owner, string Name, string CoOwner, int DaysAgo);

internal static class DemoCommunity
{
    /// <summary>
    /// Romain Gasser and Laura Petit share Oscar: Laura brings him to « Cani-balade au lever du
    /// soleil » (DemoWalks), so that Romain sees « Déjà inscrit par Laura » on that walk.
    /// </summary>
    public static readonly IReadOnlyList<DemoSharedDog> SharedDogs =
    [
        new("romain-gasser", "Oscar", "laura-petit", 90),
    ];

    public static readonly IReadOnlyList<DemoOrganization> Organizations =
    [
        new("club-sundgau", "Club canin du Sundgau",
            "Club d'éducation canine associatif. École du chiot, éducation de base et balades collectives encadrées par nos moniteurs bénévoles. Méthodes positives uniquement.",
            "Seppois-le-Haut", OrganizationStatus.Approved, "sandrine-gutzwiller", ["marc-hell", "pauline-dubois"], 18, 3, ["lucie-reinhard"]),
        new("educanine-largue", "Éducanine de la Largue",
            "Cabinet d'éducation et de comportement canin. Balades éducatives en petit groupe pour travailler le rappel, la marche en laisse et la rencontre avec d'autres chiens.",
            "Dannemarie", OrganizationStatus.Approved, "caroline-fuchs", ["jerome-kempf"], 10, 2),
        new("pattes-ill", "Les Pattes de l'Ill",
            "Éducateur canin indépendant : balades encadrées pour chiots et jeunes chiens, conseils personnalisés pour bien démarrer.",
            "Altkirch", OrganizationStatus.Approved, "hugo-lefebvre", [], 7, 1),
        new("cani-sentiers", "Cani'Sentiers du Jura alsacien",
            "Sorties de cani-rando à la demi-journée ou à la journée, entre Sundgau et Jura suisse. Chiens sportifs et marcheurs motivés bienvenus.",
            "Pfetterhouse", OrganizationStatus.Approved, "manon-wagner", ["antoine-riss", "delphine-ott"], 12, 2),
        new("o-chiens-sages", "Ô Chiens Sages",
            "Éducation positive à domicile et en petit groupe, dans tout le Sundgau.",
            "Hirsingue", OrganizationStatus.Pending, "lucie-reinhard", [], 0, 0),
    ];

    /// <summary>Signed in as Thomas Haas, walks where Benoît Horny or Sébastien Stoecklin take part carry the « Participant bloqué » warning.</summary>
    public static readonly IReadOnlyList<DemoBlock> Blocks =
    [
        new("thomas-haas", "benoit-horny", 12),
        new("thomas-haas", "sebastien-stoecklin", 30),
        new("leonie-muller", "benoit-horny", 9),
    ];

    public static readonly IReadOnlyList<DemoReport> Reports =
    [
        new("thomas-haas", ReportType.HumanBehavior, ReportStatus.Reviewed,
            "Lors d'une balade au canal, ce monsieur m'a crié dessus parce que mon chien s'approchait du sien, avec des propos insultants devant des enfants.",
            13, TargetUser: "benoit-horny",
            AdminNote: "Les deux personnes ont été contactées ; rappel des règles de courtoisie envoyé."),
        new("brigitte-faure", ReportType.IncidentNoInjury, ReportStatus.Open,
            "Diesel a bousculé ma chienne à plusieurs reprises pendant le jeu. Aucune blessure, mais elle a eu très peur et le propriétaire ne l'a pas rappelé.",
            4, TargetDog: ("sebastien-stoecklin", "Diesel")),
        new("aurelie-hartmann", ReportType.BiteOrInjury, ReportStatus.Open,
            "Morsure superficielle à l'oreille de mon chien lors d'un croisement sur le chemin des étangs. Soins faits au cabinet, rien de grave mais je préfère le signaler.",
            2, TargetDog: ("kevin-ritter", "Tyson"), Walk: "past-reactive"),
        new("romain-gasser", ReportType.Altercation, ReportStatus.Closed,
            "Deux chiens se sont battus au départ de la balade. Ils ont été séparés rapidement, sans blessure, mais l'ambiance était tendue.",
            22, Walk: "past-bisel",
            AdminNote: "Incident clos après échange avec l'organisateur : chiens séparés au départ désormais."),
    ];
}
