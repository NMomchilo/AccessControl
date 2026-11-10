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

    [Fact]
    public async Task SameOidInDifferentTenants_HasSeparateUsersAndReviewerRolesAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using UserManagementDbContext db = new(
            new DbContextOptionsBuilder<UserManagementDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        UserManagementService service = new(new SqlUserStore(db, NullLogger<SqlUserStore>.Instance),
            Options.Create(new UserManagementOptions()), NullLogger<UserManagementService>.Instance);

        UserIdentityResponse first = await service.ResolveAsync(
            new UserIdentityRequest("tenant-a", Oid, null, null), CancellationToken.None);
        UserIdentityResponse second = await service.ResolveAsync(
            new UserIdentityRequest("tenant-b", Oid, null, null), CancellationToken.None);

        Assert.NotEqual(first.UserId, second.UserId);
        Assert.NotEqual(first.Roles.Single().RoleId, second.Roles.Single().RoleId);
        Assert.All([first, second], value => Assert.Equal("reviewer", value.Roles.Single().Code));
        Assert.Equal(2, await db.Users.CountAsync());
        Assert.Equal(2, await db.Roles.CountAsync());
    }

    [Fact]
    public async Task RolesWithSameDisplayName_HaveDistinctIdsAndCannotCrossTenantsAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using UserManagementDbContext db = new(
            new DbContextOptionsBuilder<UserManagementDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        UserManagementService service = new(new SqlUserStore(db, NullLogger<SqlUserStore>.Instance),
            Options.Create(new UserManagementOptions()), NullLogger<UserManagementService>.Instance);
        await service.ResolveAsync(new UserIdentityRequest("tenant-a", Oid, null, null), CancellationToken.None);
        RoleInfo first = await service.CreateRoleAsync(new CreateRoleRequest("tenant-a", "reviewer-one", "Reviewer"), CancellationToken.None);
        RoleInfo second = await service.CreateRoleAsync(new CreateRoleRequest("tenant-a", "reviewer-two", "Reviewer"), CancellationToken.None);
        RoleInfo otherTenant = await service.CreateRoleAsync(new CreateRoleRequest("tenant-b", "reviewer-one", "Reviewer"), CancellationToken.None);

        Assert.NotEqual(first.RoleId, second.RoleId);
        Assert.Equal(first.DisplayName, second.DisplayName);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ReplaceRolesAsync("tenant-a", Oid,
            new ReplaceRolesRequest([otherTenant.RoleId]), CancellationToken.None));
        Assert.True(await service.ReplaceRolesAsync("tenant-a", Oid,
            new ReplaceRolesRequest([second.RoleId]), CancellationToken.None));
        UserIdentityResponse? user = await service.GetAsync("tenant-a", Oid, CancellationToken.None);
        Assert.Equal(second.RoleId, Assert.Single(user!.Roles).RoleId);
    }

    [Fact]
    public async Task SameDisplayName_OnlyGrantedRoleIdPermitsAsync()
    {
        Guid allowed = Guid.NewGuid();
        Guid denied = Guid.NewGuid();
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using AccessControlDbContext db = new(
            new DbContextOptionsBuilder<AccessControlDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        SqlAuthorizationStore store = new(db, NullLogger<SqlAuthorizationStore>.Instance);
        AuthorizationService service = new(store, NullLogger<AuthorizationService>.Instance);
        await store.UpsertPolicyAsync(new EndpointPolicy("tenant-a", "customers", "GET", "/customers/{id}",
            false, ["customers.view"]), CancellationToken.None);
        await store.ReplaceRoleEntitlementsAsync("tenant-a", allowed, ["customers.view"], CancellationToken.None);

        Assert.True((await service.DecideAsync(new DecisionRequest(Oid, [allowed], "customers", "GET",
            "/customers/{id}", "tenant-a"), CancellationToken.None)).Permit);
        Assert.False((await service.DecideAsync(new DecisionRequest(Oid, [denied], "customers", "GET",
            "/customers/{id}", "tenant-a"), CancellationToken.None)).Permit);
        Assert.False((await service.DecideAsync(new DecisionRequest(Oid, [allowed], "customers", "GET",
            "/customers/{id}", "tenant-b"), CancellationToken.None)).Permit);
    }
}
