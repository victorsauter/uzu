using Dogo.Api.Features.Users;
using Dogo.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dogo.Api.Features.Dogs;

/// <summary>
/// Link between a human and a dog, with the human's role (spec §3, ADR 0042). Deleted with the
/// dog or with the user. Every owner has the same rights over the dog.
/// </summary>
public sealed class DogMember : IHasCreatedAt
{
    public Guid DogId { get; init; }

    public Dog Dog { get; init; } = null!;

    public Guid UserId { get; init; }

    public User User { get; init; } = null!;

    public DogMemberRole Role { get; init; }

    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class DogMemberConfiguration : IEntityTypeConfiguration<DogMember>
{
    public void Configure(EntityTypeBuilder<DogMember> builder)
    {
        builder.HasKey(m => new { m.DogId, m.UserId });
        builder.HasIndex(m => m.UserId);
        builder.HasOne(m => m.Dog).WithMany(d => d.Members).HasForeignKey(m => m.DogId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(m => m.User).WithMany(u => u.DogLinks).HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
