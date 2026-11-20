using System.Security.Cryptography;
using System.Text;
using Dogo.Api.Features.Users;
using Dogo.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dogo.Api.Features.Dogs;

/// <summary>
/// An invitation to become one of a dog's humans (spec §3, §4.1, ADR 0042): a link
/// <c>uzu://invitation/{token}</c>, single use, valid <see cref="Lifetime"/>, revocable. Only the
/// SHA-256 of the token is stored. It belongs to the dog: it stays valid if its author leaves the
/// dog or deletes their account.
/// </summary>
public sealed class DogInvitation : IHasCreatedAt
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    /// <summary>Scheme and path of the link shared by the owner; the token follows.</summary>
    public const string LinkPrefix = "uzu://invitation/";

    /// <summary>Length of a token: 32 random bytes (256 bits) in base64url without padding.</summary>
    public const int TokenLength = 43;

    private const int TokenBytes = 32;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid DogId { get; init; }

    public Dog Dog { get; init; } = null!;

    /// <summary>Null once the author's account has been deleted.</summary>
    public Guid? CreatedById { get; set; }

    public User? CreatedBy { get; init; }

    public DogMemberRole Role { get; init; }

    /// <summary>Lowercase hex SHA-256 of the token.</summary>
    public required string TokenHash { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset? UsedAt { get; set; }

    /// <summary>Who accepted the invitation; null before, or once that account has been deleted.</summary>
    public Guid? UsedById { get; set; }

    public User? UsedBy { get; init; }

    public DateTimeOffset? RevokedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>A new random token: 256 bits from a cryptographic generator, URL-safe.</summary>
    public static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>Whether a value could be a token at all; anything else is answered as an unknown invitation.</summary>
    public static bool IsWellFormed(string token) =>
        token.Length == TokenLength && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}

internal sealed class DogInvitationConfiguration : IEntityTypeConfiguration<DogInvitation>
{
    public void Configure(EntityTypeBuilder<DogInvitation> builder)
    {
        builder.Property(i => i.TokenHash).HasMaxLength(64);
        builder.HasIndex(i => i.TokenHash).IsUnique();
        builder.HasIndex(i => i.DogId);
        builder.HasIndex(i => i.CreatedById);
        builder.HasIndex(i => i.UsedById);
        builder.HasOne(i => i.Dog).WithMany().HasForeignKey(i => i.DogId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(i => i.CreatedBy).WithMany().HasForeignKey(i => i.CreatedById).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(i => i.UsedBy).WithMany().HasForeignKey(i => i.UsedById).OnDelete(DeleteBehavior.SetNull);
    }
}
