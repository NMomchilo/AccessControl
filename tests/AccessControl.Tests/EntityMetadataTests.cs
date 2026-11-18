using AccessControl.Infrastructure.Database;
using Common.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UserManagement.Infrastructure.Database;

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
    public async Task UserManagement_rejects_writes_without_an_actor()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<UserManagementDbContext>().UseSqlite(connection).Options;
        await using var db = new UserManagementDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var organization = new UnitOrganizationEntity { Id = Guid.NewGuid(), TenantId = "default", Name = "Unit" };
        var user = new UserEntity
        {
            Id = Guid.NewGuid(), TenantId = "default", ObjectId = Guid.NewGuid().ToString("D"),
            DisplayName = "Person", CreatedAt = DateTimeOffset.UtcNow
        };
        var membership = new UnitOrganizationMembershipEntity
        { TenantId = "default", UserId = user.Id, UnitOrganizationId = organization.Id };
        db.AddRange(organization, user, membership);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Empty(await db.Users.ToListAsync());
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
