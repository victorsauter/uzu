using Dogo.Api.Features.Dogs;
using Dogo.Api.Infrastructure.Persistence;
using Dogo.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Dogo.Api.Tests.Dogs;

/// <summary>
/// The migration to <c>dog_members</c> (ADR 0042) takes over existing local databases: each
/// <c>dogs.owner_id</c> becomes an <c>Owner</c> link dated like the dog. Runs on its own database
/// in the shared container, so the database of the other tests is left alone.
/// </summary>
public sealed class DogMembersMigrationTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    private const string BeforeDogMembers = "20261118225549_AddRefreshTokenFamilies";

    [Fact]
    public async Task Existing_owners_become_owner_links_and_owner_id_disappears()
    {
        var database = $"migration_{Guid.NewGuid():N}";
        var admin = new NpgsqlConnectionStringBuilder(Fixture.ConnectionString);
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", connection);
            await create.ExecuteNonQueryAsync(Ct);
        }

        var connectionString = new NpgsqlConnectionStringBuilder(Fixture.ConnectionString) { Database = database, Pooling = false }.ConnectionString;
        var options = new DbContextOptionsBuilder<DogoDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var db = new DogoDbContext(options, TimeProvider.System);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeDogMembers, Ct);

        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var rex = Guid.NewGuid();
        var max = Guid.NewGuid();
        var created = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO users (id, provider, provider_subject, display_name, bio, status, created_at)
            VALUES ({alice}, 'Dev', 'alice', 'Alice', '', 'Active', {created}), ({bob}, 'Dev', 'bob', 'Bob', '', 'Active', {created});
            INSERT INTO dogs (id, owner_id, name, size, sociability, reactivity, leash_habit, description, created_at)
            VALUES ({rex}, {alice}, 'Rex', 'Medium', 'Easygoing', 'None', 'OnLeash', '', {created.AddDays(1)}),
                   ({max}, {bob}, 'Max', 'Small', 'Easygoing', 'None', 'OnLeash', '', {created.AddDays(2)});
            """,
            Ct);

        await migrator.MigrateAsync(cancellationToken: Ct);

        var links = await db.DogMembers.AsNoTracking().OrderBy(m => m.CreatedAt).ToListAsync(Ct);
        Assert.Equal(
            [(rex, alice, DogMemberRole.Owner, created.AddDays(1)), (max, bob, DogMemberRole.Owner, created.AddDays(2))],
            links.Select(m => (m.DogId, m.UserId, m.Role, m.CreatedAt)));
        var ownerColumns = await db.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM information_schema.columns WHERE table_name = 'dogs' AND column_name = 'owner_id'")
            .SingleAsync(Ct);
        Assert.Equal(0, ownerColumns);

        // And back: the oldest owner is restored.
        await migrator.MigrateAsync(BeforeDogMembers, Ct);
        var owners = await db.Database
            .SqlQuery<Guid>($"SELECT owner_id AS \"Value\" FROM dogs ORDER BY created_at")
            .ToListAsync(Ct);
        Assert.Equal([alice, bob], owners);

        await db.Database.EnsureDeletedAsync(Ct);
    }
}
