using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using AccessControl.Application;
using AccessControl.Contracts;
using AccessControl.Domain;
using AccessControl.Infrastructure.Database;
using UserManagement.Application;
using UserManagement.Contracts;
using UserManagement.Infrastructure.Database;

namespace AccessControl.Tests;

public sealed class TenantRoleIdentityTests
{
    private const string Oid = "9155ff4d-2708-4770-b87e-e238645ec5df";
    private static readonly Guid OrganizationA = Guid.Parse("b7799675-bc96-4d41-b920-c7be28c027da");
    private static readonly Guid OrganizationB = Guid.Parse("b27e8551-69fa-43d9-b0fa-8f3e0a485b42");

    [Fact]
    public async Task SameOidInDifferentTenants_HasSeparateUsersWithoutDefaultRolesAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using UserManagementDbContext db = new(
            new DbContextOptionsBuilder<UserManagementDbContext>().UseSqlite(connection).Options, TestActorProvider.Instance);
        await db.Database.EnsureCreatedAsync();
        UserManagementService service = new(new SqlUserStore(db, NullLogger<SqlUserStore>.Instance),
            Options.Create(new UserManagementOptions()), NullLogger<UserManagementService>.Instance);

        await service.CreateUnitOrganizationAsync(new(OrganizationA, "tenant-a", "Organization A"), default);
        await service.CreateUnitOrganizationAsync(new(OrganizationB, "tenant-b", "Organization B"), default);
        Assert.True(await service.CreateUserInOrganizationAsync("tenant-a", OrganizationA, Oid, "User", null, default));
        Assert.True(await service.CreateUserInOrganizationAsync("tenant-b", OrganizationB, Oid, "User", null, default));

        UserIdentityResponse first = await service.ResolveAsync(
            new UserIdentityRequest("tenant-a", OrganizationA, Oid), CancellationToken.None);
        UserIdentityResponse second = await service.ResolveAsync(
            new UserIdentityRequest("tenant-b", OrganizationB, Oid), CancellationToken.None);

        Assert.NotEqual(first.UserId, second.UserId);
        Assert.All([first, second], value => Assert.Empty(value.Roles));
        Assert.Equal(2, await db.Users.CountAsync());
        Assert.Empty(await db.Roles.ToListAsync());
    }

    [Fact]
    public async Task RolesWithSameDisplayName_HaveDistinctIdsAndCannotCrossTenantsAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using UserManagementDbContext db = new(
            new DbContextOptionsBuilder<UserManagementDbContext>().UseSqlite(connection).Options, TestActorProvider.Instance);
        await db.Database.EnsureCreatedAsync();
        UserManagementService service = new(new SqlUserStore(db, NullLogger<SqlUserStore>.Instance),
            Options.Create(new UserManagementOptions()), NullLogger<UserManagementService>.Instance);
        await service.CreateUnitOrganizationAsync(new(OrganizationA, "tenant-a", "Organization A"), default);
        await service.CreateUnitOrganizationAsync(new(OrganizationB, "tenant-b", "Organization B"), default);
        Assert.True(await service.CreateUserInOrganizationAsync("tenant-a", OrganizationA, Oid, "User", null, default));
        Assert.True(await service.CreateUserInOrganizationAsync("tenant-b", OrganizationB, Oid, "User", null, default));
        await service.ResolveAsync(new UserIdentityRequest("tenant-a", OrganizationA, Oid), CancellationToken.None);
        await service.ResolveAsync(new UserIdentityRequest("tenant-b", OrganizationB, Oid), CancellationToken.None);
        RoleInfo first = await service.CreateRoleAsync(new CreateRoleRequest("tenant-a", OrganizationA, "reader-one", "Reader"), CancellationToken.None);
        RoleInfo second = await service.CreateRoleAsync(new CreateRoleRequest("tenant-a", OrganizationA, "reader-two", "Reader"), CancellationToken.None);
        RoleInfo otherTenant = await service.CreateRoleAsync(new CreateRoleRequest("tenant-b", OrganizationB, "reader-one", "Reader"), CancellationToken.None);

        Assert.NotEqual(first.RoleId, second.RoleId);
        Assert.Equal(first.DisplayName, second.DisplayName);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ReplaceRolesAsync("tenant-a", OrganizationA, Oid,
            new ReplaceRolesRequest([otherTenant.RoleId]), CancellationToken.None));
        Assert.True(await service.ReplaceRolesAsync("tenant-a", OrganizationA, Oid,
            new ReplaceRolesRequest([second.RoleId]), CancellationToken.None));
        UserIdentityResponse? user = await service.GetAsync("tenant-a", OrganizationA, Oid, CancellationToken.None);
        Assert.Equal(second.RoleId, Assert.Single(user!.Roles).RoleId);
    }

    [Fact]
    public async Task SameDisplayName_OnlyGrantedRoleIdPermitsAsync()
    {
        Guid organizationId = OrganizationA;
        Guid allowed = Guid.NewGuid();
        Guid denied = Guid.NewGuid();
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using AccessControlDbContext db = new(
            new DbContextOptionsBuilder<AccessControlDbContext>().UseSqlite(connection).Options, TestActorProvider.Instance);
        await db.Database.EnsureCreatedAsync();
        db.Entitlements.Add(new EntitlementEntity
        {
            Id = TestEntitlements.View, TenantId = "tenant-a", UnitOrganizationId = organizationId,
            Code = "customers.view", DisplayName = "View customers", IsActive = true
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        SqlAuthorizationStore store = new(db, NullLogger<SqlAuthorizationStore>.Instance);
        Guid currentRole = allowed;
        AuthorizationService service = new(store, NullLogger<AuthorizationService>.Instance);
        await store.UpsertPolicyAsync(new EndpointPolicy("tenant-a", "customers", "GET", "/customers/{id}",
            false, [TestEntitlements.View], organizationId), CancellationToken.None);
        await store.ReplaceRoleEntitlementsAsync("tenant-a", organizationId, allowed, [TestEntitlements.View], CancellationToken.None);

        Assert.True((await service.DecideAsync(new DecisionRequest(Oid, "customers", "GET",
            "/customers/{id}", "tenant-a", organizationId, [currentRole]), CancellationToken.None)).Permit);
        currentRole = denied;
        Assert.False((await service.DecideAsync(new DecisionRequest(Oid, "customers", "GET",
            "/customers/{id}", "tenant-a", organizationId, [currentRole]), CancellationToken.None)).Permit);
        currentRole = allowed;
        Assert.False((await service.DecideAsync(new DecisionRequest(Oid, "customers", "GET",
            "/customers/{id}", "tenant-b", organizationId, [currentRole]), CancellationToken.None)).Permit);
        Assert.False((await service.DecideAsync(new DecisionRequest(Oid, "customers", "GET",
            "/customers/{id}", "tenant-a", OrganizationB, [currentRole]), CancellationToken.None)).Permit);
    }

}
