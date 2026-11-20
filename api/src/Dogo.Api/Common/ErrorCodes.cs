namespace Dogo.Api.Common;

/// <summary>
/// Stable values of the <c>code</c> extension carried by every problem details response.
/// Clients branch on these codes, never on titles. Feature-specific codes are named
/// <c>{feature}.{reason}</c>.
/// </summary>
public static class ErrorCodes
{
    // Generic codes, also used as defaults by ProblemDetailsSetup.
    public const string ValidationFailed = "validation.failed";
    public const string InvalidRequest = "request.invalid";
    public const string RequestTooLarge = "request.too_large";
    public const string UnsupportedMediaType = "request.unsupported_media_type";
    public const string NotFound = "not_found";
    public const string MethodNotAllowed = "method_not_allowed";
    public const string RateLimitExceeded = "rate_limit.exceeded";
    public const string Unauthenticated = "auth.unauthenticated";
    public const string Forbidden = "auth.forbidden";
    public const string ServerError = "server.error";
    public const string Error = "error";

    // Authentication and accounts.
    public const string InvalidToken = "auth.invalid_token";
    public const string AccountSuspended = "account.suspended";

    // Health.
    public const string DatabaseUnavailable = "health.database_unavailable";

    // Users and dogs.
    public const string UserNotFound = "user.not_found";
    public const string DogNotFound = "dog.not_found";
    public const string DogForbidden = "dog.forbidden";
    public const string DogSizeInUse = "dog.size_in_use";
    public const string DogLimitReached = "dog.limit_reached";

    // Dogs shared by several humans and invitations (spec §4.1, ADR 0042).
    public const string DogHasOtherOwners = "dog.has_other_owners";
    public const string DogLastOwner = "dog.last_owner";
    public const string DogAlreadyOwner = "dog.already_owner";
    public const string InvitationNotFound = "invitation.not_found";
    public const string InvitationAlreadyUsed = "invitation.already_used";
    public const string InvitationExpired = "invitation.expired";

    // Walks (spec §4.2) and registrations (spec §4.3).
    public const string WalkNotFound = "walk.not_found";
    public const string WalkForbidden = "walk.forbidden";
    public const string WalkNotOpen = "walk.not_open";
    public const string WalkFull = "walk.full";
    public const string WalkSizeNotAllowed = "walk.size_not_allowed";
    public const string WalkCapacityBelowRegistered = "walk.capacity_below_registered";
    public const string WalkSizeInUse = "walk.size_in_use";
    public const string RegistrationNotFound = "registration.not_found";
    public const string RegistrationForbidden = "registration.forbidden";
    public const string RegistrationDuplicate = "registration.duplicate";
    public const string RegistrationNotActive = "registration.not_active";

    // Weekly series (spec §4.5) and walk threads (spec §4.4).
    public const string WalkNotInSeries = "walk.not_in_series";
    public const string MessageForbidden = "message.forbidden";

    // Registration rules of Lot 4 (spec §4.3) and professional walks (spec §4.6).
    public const string WalkMembersOnly = "walk.members_only";
    public const string WalkOrganizationForbidden = "walk.organization_forbidden";

    // Organisations and memberships (spec §4.6).
    public const string OrganizationNotFound = "organization.not_found";
    public const string OrganizationForbidden = "organization.forbidden";
    public const string OrganizationTooManyPending = "organization.too_many_pending";
    public const string OrganizationNotApproved = "organization.not_approved";
    public const string OrganizationClosed = "organization.closed";
    public const string MembershipNotFound = "membership.not_found";
    public const string MembershipDuplicate = "membership.duplicate";
    public const string MembershipNotPending = "membership.not_pending";
    public const string MembershipNotAccepted = "membership.not_accepted";
    public const string MembershipOwnerRole = "membership.owner_role";
    public const string MembershipSoleOwner = "membership.sole_owner";

    // Blocks, reports and administration (spec §4.7).
    public const string BlockSelf = "block.self";
    public const string ReportSelf = "report.self";
    public const string ReportNotFound = "report.not_found";
    public const string AdminForbidden = "admin.forbidden";
    public const string AdminSelfSuspension = "admin.self_suspension";

    // Photos.
    public const string PhotoMissing = "photo.missing";
    public const string PhotoTooLarge = "photo.too_large";
    public const string PhotoUnsupportedType = "photo.unsupported_type";
    public const string PhotoNotFound = "photo.not_found";
}
