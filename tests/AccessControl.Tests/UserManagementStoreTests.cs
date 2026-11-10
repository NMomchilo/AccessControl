using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UserManagement.Application;
using UserManagement.Contracts;
using UserManagement.Infrastructure.Database;

namespace AccessControl.Tests;

public sealed class UserManagementStoreTests
{
    private const string Oid = "9155ff4d-2708-4770-b87e-e238645ec5df";

    [Fact]
    public async Task ResolveAsync_FirstSignIn_CreatesReviewerAndReusesUserAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using UserManagementDbContext db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        UserManagementService service = CreateService(db);

        UserIdentityResponse first = await service.ResolveAsync(new UserIdentityRequest(Oid, "first@example.com", "First"),
            CancellationToken.None);
        UserIdentityResponse second = await service.ResolveAsync(new UserIdentityRequest(Oid, "second@example.com", "Second"),
            CancellationToken.None);

        Assert.Equal(first.UserId, second.UserId);
        Assert.Equal(["reviewer"], second.Roles);
        Assert.Single(await db.Users.ToListAsync());
        Assert.Equal("second@example.com", (await db.Users.SingleAsync()).Email);
    }

    [Fact]
    public async Task ReplaceRolesAsync_ExistingRoleAndNewRole_ReplacesSetAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using UserManagementDbContext db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        UserManagementService service = CreateService(db);
        await service.ResolveAsync(new UserIdentityRequest(Oid, null, null), CancellationToken.None);

        bool found = await service.ReplaceRolesAsync(Oid,
            new ReplaceRolesRequest(["Reviewer", "Admin"]), CancellationToken.None);
        UserIdentityResponse? updated = await service.GetAsync(Oid, CancellationToken.None);

        Assert.True(found);
        Assert.Equal(["admin", "reviewer"], updated?.Roles);
    }

    [Fact]
    public async Task SetActiveAsync_DisabledUser_RemainsDisabledOnResolveAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using UserManagementDbContext db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        UserManagementService service = CreateService(db);
        await service.ResolveAsync(new UserIdentityRequest(Oid, null, null), CancellationToken.None);

        Assert.True(await service.SetActiveAsync(Oid, new SetActiveRequest(false), CancellationToken.None));
        UserIdentityResponse resolved = await service.ResolveAsync(new UserIdentityRequest(Oid, null, null),
            CancellationToken.None);

        Assert.False(resolved.IsActive);
    }

    [Fact]
    public async Task ResolveAsync_InvalidOid_RejectsWithoutWritingAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using UserManagementDbContext db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        UserManagementService service = CreateService(db);

        await Assert.ThrowsAsync<ArgumentException>(() => service.ResolveAsync(
            new UserIdentityRequest("not-an-oid", null, null), CancellationToken.None));
        Assert.Empty(await db.Users.ToListAsync());
    }

    private static UserManagementDbContext CreateDb(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<UserManagementDbContext>().UseSqlite(connection).Options);

    private static UserManagementService CreateService(UserManagementDbContext db) =>
        new(new SqlUserStore(db, NullLogger<SqlUserStore>.Instance),
            Options.Create(new UserManagementOptions()), NullLogger<UserManagementService>.Instance);
}
