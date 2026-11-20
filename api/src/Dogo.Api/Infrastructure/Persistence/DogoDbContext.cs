using Dogo.Api.Features.Auth;
using Dogo.Api.Features.Blocks;
using Dogo.Api.Features.Dogs;
using Dogo.Api.Features.Messages;
using Dogo.Api.Features.Notifications;
using Dogo.Api.Features.Organizations;
using Dogo.Api.Features.Reports;
using Dogo.Api.Features.Users;
using Dogo.Api.Features.Walks;
using Microsoft.EntityFrameworkCore;

namespace Dogo.Api.Infrastructure.Persistence;

/// <summary>
/// Single EF Core context. Entities live in their feature folder together with their
/// <see cref="IEntityTypeConfiguration{TEntity}"/>, discovered by assembly scanning.
/// </summary>
public sealed class DogoDbContext(DbContextOptions<DogoDbContext> options, TimeProvider timeProvider) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Dog> Dogs => Set<Dog>();

    public DbSet<DogMember> DogMembers => Set<DogMember>();

    public DbSet<DogInvitation> DogInvitations => Set<DogInvitation>();

    public DbSet<Walk> Walks => Set<Walk>();

    public DbSet<Registration> Registrations => Set<Registration>();

    public DbSet<WalkSeries> WalkSeries => Set<WalkSeries>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<Device> Devices => Set<Device>();

    public DbSet<NotificationPreferences> NotificationPreferences => Set<NotificationPreferences>();

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Membership> Memberships => Set<Membership>();

    public DbSet<Block> Blocks => Set<Block>();

    public DbSet<Report> Reports => Set<Report>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // PostGIS is needed from Lot 2 (walk locations); enabled from the first migration.
        modelBuilder.HasPostgresExtension("postgis");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DogoDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums are stored as readable strings, like in the JSON contract.
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampCreatedAt();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampCreatedAt();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampCreatedAt()
    {
        // PostgreSQL stores microseconds: truncate so that the value returned right after
        // a save equals the value read back later (Linux clocks have 100 ns resolution).
        var now = timeProvider.GetUtcNow();
        now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
        foreach (var entry in ChangeTracker.Entries<IHasCreatedAt>().Where(e => e.State == EntityState.Added))
        {
            entry.Entity.CreatedAt = now;
        }
    }
}

/// <summary>Entities whose <c>CreatedAt</c> is set by the server when first saved.</summary>
public interface IHasCreatedAt
{
    DateTimeOffset CreatedAt { get; set; }
}
