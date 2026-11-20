using Dogo.Api.Features.Users;

namespace Dogo.Api.Features.Dogs;

/// <summary>Creates an invitation to a dog. Only <c>Owner</c> is accepted in V1.x (ADR 0042).</summary>
public sealed record CreateDogInvitationRequest
{
    public required DogMemberRole Role { get; init; }
}

/// <summary>
/// A new invitation, as returned once by <c>POST /dogs/{id}/invitations</c>: the only response that
/// carries the token. <c>link</c> is <c>uzu://invitation/{token}</c>, to share as the owner likes.
/// </summary>
public sealed record CreatedDogInvitationResponse(
    Guid Id,
    Guid DogId,
    DogMemberRole Role,
    string Token,
    string Link,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt);

/// <summary>A pending invitation of a dog, for its owners; never the token. <c>createdBy</c> is null once that account has been deleted.</summary>
public sealed record DogInvitationResponse(
    Guid Id,
    DogMemberRole Role,
    UserSummaryResponse? CreatedBy,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt)
{
    /// <summary>Requires <see cref="DogInvitation.CreatedBy"/> to be loaded.</summary>
    internal static DogInvitationResponse From(DogInvitation invitation) => new(
        invitation.Id,
        invitation.Role,
        invitation.CreatedBy is null ? null : UserSummaryResponse.From(invitation.CreatedBy),
        invitation.ExpiresAt,
        invitation.CreatedAt);
}

/// <summary>
/// What the invited person sees before accepting (<c>GET /dog-invitations/{token}</c>): the dog, who
/// invited them and the role offered. <c>invitedBy</c> is null once that account has been deleted.
/// </summary>
public sealed record DogInvitationPreviewResponse(
    DogResponse Dog,
    UserSummaryResponse? InvitedBy,
    DogMemberRole Role,
    DateTimeOffset ExpiresAt);
