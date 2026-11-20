using Dogo.Api.Features.Dogs;
using Dogo.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dogo.Api.Features.Users;

public enum IdentityProvider
{
    Apple,
    Google,
    Dev,
}

public enum UserStatus
{
    Active,
    Suspended,
}

public sealed class User : IHasCreatedAt
{
    public const int DisplayNameMaxLength = 50;
    public const int BioMaxLength = 500;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public IdentityProvider Provider { get; init; }

    public required string ProviderSubject { get; init; }

    /// <summary>Private; only exposed by <c>GET /me</c>. Stored only when verified by the provider.</summary>
    public string? Email { get; set; }

    public required string DisplayName { get; set; }

    public string Bio { get; set; } = "";

    /// <summary>File name in <see cref="Infrastructure.Storage.IPhotoStorage"/>; exposed as <c>photoUrl</c>.</summary>
    public string? PhotoFileName { get; set; }

    public UserStatus Status { get; set; } = UserStatus.Active;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Links to the dogs this user is one of the humans of (ADR 0042).</summary>
    public List<DogMember> DogLinks { get; } = [];
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.Property(u => u.ProviderSubject).HasMaxLength(255);
        builder.Property(u => u.Email).HasMaxLength(320);
        builder.Property(u => u.DisplayName).HasMaxLength(User.DisplayNameMaxLength);
        builder.Property(u => u.Bio).HasMaxLength(User.BioMaxLength);
        builder.Property(u => u.PhotoFileName).HasMaxLength(64);
        builder.HasIndex(u => new { u.Provider, u.ProviderSubject }).IsUnique();
    }
}
