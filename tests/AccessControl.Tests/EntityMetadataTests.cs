using AccessControl.Infrastructure.Database;
using Common.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AccessControl.Tests;

public sealed class EntityMetadataTests
{
    [Fact]
    public async Task AccessControl_stamps_created_and_modified_entities_with_validated_actor()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AccessControlDbContext>().UseSqlite(connection).Options;
        await using var db = new AccessControlDbContext(options, new StubActor("trusted-oid"));
        await db.Database.EnsureCreatedAsync();
        var entitlement = new EntitlementEntity
        {
            TenantId = "default", UnitOrganizationId = Guid.NewGuid(), Code = "read", DisplayName = "Read"
        };
        db.Entitlements.Add(entitlement);
        await db.SaveChangesAsync();

        Assert.NotEqual(Guid.Empty, entitlement.Id);
        Assert.Equal("trusted-oid", entitlement.LastUpdateUser);
        Assert.Equal(DateTimeKind.Utc, entitlement.LastUpdateTime.Kind);
        var firstUpdate = entitlement.LastUpdateTime;
        entitlement.DisplayName = "Read data";
        await db.SaveChangesAsync();
        Assert.True(entitlement.LastUpdateTime >= firstUpdate);
    }



    [Fact]
    public async Task AccessControl_rejects_writes_without_an_actor()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AccessControlDbContext>().UseSqlite(connection).Options;
        await using var db = new AccessControlDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Entitlements.Add(new EntitlementEntity
        {
            TenantId = "default", UnitOrganizationId = Guid.NewGuid(), Code = "read", DisplayName = "Read"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Empty(await db.Entitlements.ToListAsync());
    }

    private sealed class StubActor(string actor) : ICurrentActorProvider
    {
        public string ActorId => actor;
    }
}
