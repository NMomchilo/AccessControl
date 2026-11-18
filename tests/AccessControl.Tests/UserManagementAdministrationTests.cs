using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UserManagement.Application;
using UserManagement.Infrastructure.Database;

namespace AccessControl.Tests;

public sealed class UserManagementAdministrationTests
{
    private const string SuperOid = "11111111-1111-1111-1111-111111111111";
    private const string AdminOid = "22222222-2222-2222-2222-222222222222";
    private const string MemberOid = "33333333-3333-3333-3333-333333333333";
    private static readonly Guid Alpha = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Beta = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task CreatingUserInMissingOrganizationLeavesNoOrphanAsync()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        var service = CreateService(db);
        Assert.False(await service.CreateUserInOrganizationAsync("tenant", Alpha, MemberOid, "Member", null, default));
        Assert.Empty(await db.Users.ToArrayAsync());
    }

    [Fact]
    public async Task LastActiveSuperAdminCannotBeRevokedAsync()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        var service = CreateService(db);
        await service.CreateUserAsync("tenant", SuperOid, "Super", null, default);
        await service.SetSuperAdminAsync("tenant", SuperOid, true, default);
        await Assert.ThrowsAsync<AdminDutyConflictException>(() => service.SetSuperAdminAsync("tenant", SuperOid, false, default));
        Assert.True((await service.GetAdminAccessAsync("tenant", SuperOid, null, default)).IsSuperAdmin);
    }

    [Fact]
    public async Task DutiesGrantOnlySuperAdminTenantWideAndAdminInAssignedUnitAsync()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        var service = CreateService(db);
        await service.CreateUnitOrganizationAsync(new(Alpha, "tenant", "Alpha"), default);
        await service.CreateUnitOrganizationAsync(new(Beta, "tenant", "Beta"), default);
        await service.CreateUserAsync("tenant", SuperOid, "Super", null, default);
        await service.CreateUserAsync("tenant", AdminOid, "Admin", null, default);
        await service.SetSuperAdminAsync("tenant", SuperOid, true, default);
        await service.AssignMembershipAsync("tenant", Alpha, AdminOid, default);
        await service.SetUnitAdminAsync("tenant", Alpha, AdminOid, true, default);

        Assert.True((await service.GetAdminAccessAsync("tenant", SuperOid, null, default)).IsSuperAdmin);
        Assert.Equal([Alpha], (await service.GetAdminAccessAsync("tenant", AdminOid, null, default)).ManagedUnitOrganizationIds);
        await service.RequireAdminAsync("tenant", AdminOid, Alpha, default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RequireAdminAsync("tenant", AdminOid, Beta, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RequireSuperAdminAsync("tenant", AdminOid, default));
    }

    [Fact]
    public async Task RevokedMembershipDoesNotResolveOrAffectOtherUnitAsync()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        var service = CreateService(db);
        await service.CreateUnitOrganizationAsync(new(Alpha, "tenant", "Alpha"), default);
        await service.CreateUnitOrganizationAsync(new(Beta, "tenant", "Beta"), default);
        await service.CreateUserAsync("tenant", MemberOid, "Member", null, default);
        await service.AssignMembershipAsync("tenant", Alpha, MemberOid, default);
        await service.AssignMembershipAsync("tenant", Beta, MemberOid, default);
        var privileged = await service.CreateRoleAsync(new("tenant", Alpha, "approver", "Approver"), default);
        await service.ReplaceRolesAsync("tenant", Alpha, MemberOid, new([privileged.RoleId]), default);

        Assert.True(await service.RevokeMembershipAsync("tenant", Alpha, MemberOid, default));
        Assert.Null(await service.GetAsync("tenant", Alpha, MemberOid, default));
        Assert.NotNull(await service.GetAsync("tenant", Beta, MemberOid, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.ResolveAsync(new("tenant", Alpha, MemberOid), default));
        Assert.Null(await service.GetAsync("tenant", Alpha, MemberOid, default));
        Assert.True(await service.AssignMembershipAsync("tenant", Alpha, MemberOid, default));
        Assert.Empty((await service.GetAsync("tenant", Alpha, MemberOid, default))!.Roles);
        Assert.NotNull(await service.GetAsync("tenant", Beta, MemberOid, default));
    }

    [Fact]
    public async Task SoftDeletedUserAndRoleDoNotResolveAsync()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        var service = CreateService(db);
        await service.CreateUnitOrganizationAsync(new(Alpha, "tenant", "Alpha"), default);
        await service.CreateUserAsync("tenant", MemberOid, "Member", null, default);
        await service.AssignMembershipAsync("tenant", Alpha, MemberOid, default);
        var role = await service.CreateRoleAsync(new("tenant", Alpha, "editor", "Editor"), default);
        await service.ReplaceRolesAsync("tenant", Alpha, MemberOid, new([role.RoleId]), default);
        Assert.True(await service.DeleteRoleAsync("tenant", Alpha, role.RoleId, default));
        Assert.Empty((await service.GetAsync("tenant", Alpha, MemberOid, default))!.Roles);
        Assert.True(await service.DeleteUserAsync("tenant", MemberOid, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.ResolveAsync(new("tenant", Alpha, MemberOid), default));
    }

    [Fact]
    public async Task DisabledAdministratorLosesPermissionImmediatelyAsync()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        var service = CreateService(db);
        await service.CreateUnitOrganizationAsync(new(Alpha, "tenant", "Alpha"), default);
        await service.CreateUserAsync("tenant", AdminOid, "Admin", null, default);
        await service.AssignMembershipAsync("tenant", Alpha, AdminOid, default);
        await service.SetUnitAdminAsync("tenant", Alpha, AdminOid, true, default);
        Assert.True((await service.GetAdminAccessAsync("tenant", AdminOid, Alpha, default)).CanManage);
        await service.SetActiveAsync("tenant", AdminOid, new(false), default);
        Assert.False((await service.GetAdminAccessAsync("tenant", AdminOid, Alpha, default)).CanManage);
    }

    [Fact]
    public async Task RoleUpdateAndSoftDeleteHideRoleFromLookupsAsync()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        var service = CreateService(db);
        await service.CreateUnitOrganizationAsync(new(Alpha, "tenant", "Alpha"), default);
        var role = await service.CreateRoleAsync(new("tenant", Alpha, "editor", "Editor"), default);
        var updated = await service.UpdateRoleAsync("tenant", Alpha, role.RoleId, "Senior Editor", default);
        Assert.Equal("Senior Editor", updated?.DisplayName);
        Assert.True(await service.DeleteRoleAsync("tenant", Alpha, role.RoleId, default));
        Assert.Null(await service.GetRoleAsync("tenant", Alpha, role.RoleId, default));
        Assert.Empty((await service.GetRolesPageAsync("tenant", Alpha, 1, 10, default))!.Items);
    }

    private static UserManagementDbContext CreateDb(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<UserManagementDbContext>().UseSqlite(connection).Options, TestActorProvider.Instance);

    private static UserManagementService CreateService(UserManagementDbContext db) =>
        new(new SqlUserStore(db, NullLogger<SqlUserStore>.Instance), Options.Create(new UserManagementOptions()),
            NullLogger<UserManagementService>.Instance);
}
