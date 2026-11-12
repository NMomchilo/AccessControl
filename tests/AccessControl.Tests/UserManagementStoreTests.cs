using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UserManagement.Application;
using UserManagement.Contracts;
using UserManagement.Infrastructure.Database;

namespace AccessControl.Tests;

public sealed class UserManagementStoreTests
{
    private const string Oid = "9155ff4d-2708-4770-b87e-e238645ec5df";
    private static readonly Guid OrganizationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherOrganizationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task ResolveAsync_FirstSignIn_CreatesReviewerAndReusesUserAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using UserManagementDbContext db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        UserManagementService service = CreateService(db);

        UserIdentityResponse first = await service.ResolveAsync(new UserIdentityRequest("default", OrganizationId, "Alpha", Oid, "first@example.com", "First"),
            CancellationToken.None);
        UserIdentityResponse second = await service.ResolveAsync(new UserIdentityRequest("default", OrganizationId, "Untrusted rename", Oid, "second@example.com", "Second"),
            CancellationToken.None);

        Assert.Equal(first.UserId, second.UserId);
        Assert.Equal("reviewer", second.Roles.Single().Code);
        Assert.Single(await db.Users.ToListAsync());
        Assert.Equal("second@example.com", (await db.Users.SingleAsync()).Email);
        Assert.Equal("Alpha", second.UnitOrganizationName);
    }

    [Fact]
    public async Task ReplaceRolesAsync_ExistingRoleAndNewRole_ReplacesSetAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using UserManagementDbContext db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        UserManagementService service = CreateService(db);
        UserIdentityResponse reviewer = await service.ResolveAsync(new UserIdentityRequest("default", OrganizationId, "Alpha", Oid, null, null), CancellationToken.None);
        RoleInfo admin = await service.CreateRoleAsync(new CreateRoleRequest("default", OrganizationId, "Admin", "Administrator"), CancellationToken.None);

        bool found = await service.ReplaceRolesAsync("default", OrganizationId, Oid,
            new ReplaceRolesRequest([reviewer.Roles.Single().RoleId, admin.RoleId]), CancellationToken.None);
        UserIdentityResponse? updated = await service.GetAsync("default", OrganizationId, Oid, CancellationToken.None);

        Assert.True(found);
        Assert.NotNull(updated);
        Assert.Equal(["admin", "reviewer"], updated.Roles.Select(role => role.Code).ToArray());
    }

    [Fact]
    public async Task SetActiveAsync_DisabledUser_RemainsDisabledOnResolveAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using UserManagementDbContext db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        UserManagementService service = CreateService(db);
        await service.ResolveAsync(new UserIdentityRequest("default", OrganizationId, "Alpha", Oid, null, null), CancellationToken.None);

        Assert.True(await service.SetActiveAsync("default", Oid, new SetActiveRequest(false), CancellationToken.None));
        UserIdentityResponse resolved = await service.ResolveAsync(new UserIdentityRequest("default", OrganizationId, "Alpha", Oid, null, null),
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
            new UserIdentityRequest("default", OrganizationId, "Alpha", "not-an-oid", null, null), CancellationToken.None));
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task SameOidAcrossOrganizations_HasSeparateReviewerRolesAndAssignmentsAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using UserManagementDbContext db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        UserManagementService service = CreateService(db);
        UserIdentityResponse first = await service.ResolveAsync(new("default", OrganizationId, "Alpha", Oid, null, null), CancellationToken.None);
        UserIdentityResponse second = await service.ResolveAsync(new("default", OtherOrganizationId, "Beta", Oid, null, null), CancellationToken.None);
        Assert.Equal(first.UserId, second.UserId);
        Assert.NotEqual(first.Roles.Single().RoleId, second.Roles.Single().RoleId);
        Assert.Equal(2, await db.UnitOrganizationMemberships.CountAsync());
        Assert.Equal(2, await db.UserRoles.CountAsync());
        await Assert.ThrowsAsync<ArgumentException>(() => service.ReplaceRolesAsync("default", OrganizationId,
            Oid, new ReplaceRolesRequest([second.Roles.Single().RoleId]), CancellationToken.None));
        await service.ReplaceRolesAsync("default", OrganizationId, Oid, new ReplaceRolesRequest([]), CancellationToken.None);
        Assert.Empty((await service.GetAsync("default", OrganizationId, Oid, CancellationToken.None))!.Roles);
        Assert.Single((await service.GetAsync("default", OtherOrganizationId, Oid, CancellationToken.None))!.Roles);
    }

    [Fact]
    public async Task CrossTenantOrganizationId_IsRejectedAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using UserManagementDbContext db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        UserManagementService service = CreateService(db);
        await service.ResolveAsync(new("tenant-a", OrganizationId, "Alpha", Oid, null, null), CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ResolveAsync(
            new("tenant-b", OrganizationId, "Alpha", Oid, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task OrganizationNamesAreUniqueButRenameCannotBeAssertedByGatewayAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using UserManagementDbContext db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        UserManagementService service = CreateService(db);
        await service.ResolveAsync(new("default", OrganizationId, "Alpha", Oid, null, null), CancellationToken.None);

        await Assert.ThrowsAsync<UnitOrganizationConflictException>(() => service.ResolveAsync(
            new("default", OtherOrganizationId, "Alpha", Oid, null, null), CancellationToken.None));
        UserIdentityResponse existing = await service.ResolveAsync(
            new("default", OrganizationId, "Forged rename", Oid, null, null), CancellationToken.None);

        Assert.Equal("Alpha", existing.UnitOrganizationName);
        Assert.Single(await db.UnitOrganizations.ToListAsync());
    }

    [Fact]
    public async Task OrganizationRoleAdministration_EnforcesScopeAndDeletedStateAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using UserManagementDbContext db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        UserManagementService service = CreateService(db);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateRoleAsync(
            new("default", OrganizationId, "admin", "Admin"), CancellationToken.None));
        await service.CreateUnitOrganizationAsync(new(OrganizationId, "default", "Alpha"), CancellationToken.None);
        await service.CreateUnitOrganizationAsync(new(OtherOrganizationId, "default", "Beta"), CancellationToken.None);
        RoleInfo first = await service.CreateRoleAsync(new("default", OrganizationId, "admin", "Admin"), CancellationToken.None);
        RoleInfo second = await service.CreateRoleAsync(new("default", OtherOrganizationId, "admin", "Admin"), CancellationToken.None);
        Assert.NotEqual(first.RoleId, second.RoleId);
        Assert.Single((await service.GetRolesPageAsync("default", OrganizationId, 1, 100,
            CancellationToken.None))!.Items);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateRoleAsync(
            new("default", OrganizationId, "admin", "Duplicate"), CancellationToken.None));

        Assert.True(await service.DeleteUnitOrganizationAsync("default", OrganizationId, CancellationToken.None));
        Assert.Null(await service.GetRolesPageAsync("default", OrganizationId, 1, 100,
            CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateRoleAsync(
            new("default", OrganizationId, "another", "Another"), CancellationToken.None));
        await Assert.ThrowsAsync<UnitOrganizationConflictException>(() => service.CreateUnitOrganizationAsync(
            new(OrganizationId, "default", "Reused"), CancellationToken.None));
    }

    [Fact]
    public async Task UnknownUserOrMembership_DoesNotAcceptAdministrativeChangesAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using UserManagementDbContext db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        UserManagementService service = CreateService(db);

        Assert.False(await service.SetActiveAsync("default", Oid, new(false), CancellationToken.None));
        Assert.False(await service.ReplaceRolesAsync("default", OrganizationId, Oid,
            new ReplaceRolesRequest([]), CancellationToken.None));
        Assert.Null(await service.GetAsync("default", OrganizationId, Oid, CancellationToken.None));

        await service.ResolveAsync(new("default", OtherOrganizationId, "Beta", Oid, null, null), CancellationToken.None);
        Assert.False(await service.ReplaceRolesAsync("default", OrganizationId, Oid,
            new ReplaceRolesRequest([]), CancellationToken.None));
        Assert.Null(await service.GetAsync("default", OrganizationId, Oid, CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentFirstSignIn_ReusesWinningOrganizationUserAndReviewerAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), $"um-concurrency-{Guid.NewGuid():N}.db");
        try
        {
            await using (UserManagementDbContext setup = new(new DbContextOptionsBuilder<UserManagementDbContext>()
                .UseSqlite($"Data Source={path}").Options))
                await setup.Database.EnsureCreatedAsync();
            BlockingFirstSave<UnitOrganizationEntity> gate = new();
            await using UserManagementDbContext firstDb = new(new DbContextOptionsBuilder<UserManagementDbContext>()
                .UseSqlite($"Data Source={path}").AddInterceptors(gate).Options);
            await using UserManagementDbContext secondDb = new(new DbContextOptionsBuilder<UserManagementDbContext>()
                .UseSqlite($"Data Source={path}").Options);
            UserManagementService firstService = CreateService(firstDb);
            UserManagementService secondService = CreateService(secondDb);
            UserIdentityRequest request = new("default", OrganizationId, "Alpha", Oid, null, null);

            Task<UserIdentityResponse> first = firstService.ResolveAsync(request, CancellationToken.None);
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            UserIdentityResponse winner;
            try
            {
                winner = await secondService.ResolveAsync(request, CancellationToken.None);
            }
            finally
            {
                gate.Release();
            }
            UserIdentityResponse loser = await first.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.Equal(winner.UserId, loser.UserId);
            Assert.Equal(winner.Roles.Single().RoleId, loser.Roles.Single().RoleId);
            Assert.Single(await secondDb.UnitOrganizations.AsNoTracking().ToListAsync());
            Assert.Single(await secondDb.Users.AsNoTracking().ToListAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ConcurrentReviewerCreation_ReloadsExistingMembershipInsteadOfReinsertingUserAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), $"um-reviewer-race-{Guid.NewGuid():N}.db");
        try
        {
            await using (UserManagementDbContext setup = new(new DbContextOptionsBuilder<UserManagementDbContext>()
                .UseSqlite($"Data Source={path}").Options))
            {
                await setup.Database.EnsureCreatedAsync();
                UserManagementService service = CreateService(setup);
                await service.ResolveAsync(new("default", OrganizationId, "Alpha", Oid, null, null), CancellationToken.None);
                await service.CreateUnitOrganizationAsync(new(OtherOrganizationId, "default", "Beta"), CancellationToken.None);
            }
            BlockingFirstSave<RoleEntity> gate = new();
            await using UserManagementDbContext firstDb = new(new DbContextOptionsBuilder<UserManagementDbContext>()
                .UseSqlite($"Data Source={path}").AddInterceptors(gate).Options);
            await using UserManagementDbContext secondDb = new(new DbContextOptionsBuilder<UserManagementDbContext>()
                .UseSqlite($"Data Source={path}").Options);
            UserIdentityRequest request = new("default", OtherOrganizationId, "Beta", Oid, null, null);

            Task<UserIdentityResponse> first = CreateService(firstDb).ResolveAsync(request, CancellationToken.None);
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            UserIdentityResponse winner;
            try
            {
                winner = await CreateService(secondDb).ResolveAsync(request, CancellationToken.None);
            }
            finally
            {
                gate.Release();
            }
            UserIdentityResponse loser = await first.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.Equal(winner.UserId, loser.UserId);
            Assert.Equal(winner.Roles.Single().RoleId, loser.Roles.Single().RoleId);
            Assert.Single(await secondDb.UnitOrganizationMemberships.AsNoTracking()
                .Where(value => value.UnitOrganizationId == OtherOrganizationId).ToListAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ConcurrentUserCreation_ReloadsWinningUserAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), $"um-user-race-{Guid.NewGuid():N}.db");
        try
        {
            await using (UserManagementDbContext setup = new(new DbContextOptionsBuilder<UserManagementDbContext>()
                .UseSqlite($"Data Source={path}").Options))
            {
                await setup.Database.EnsureCreatedAsync();
                UserManagementService service = CreateService(setup);
                await service.CreateUnitOrganizationAsync(new(OrganizationId, "default", "Alpha"), CancellationToken.None);
                await service.CreateRoleAsync(new("default", OrganizationId, "reviewer", "Reviewer"), CancellationToken.None);
            }
            BlockingFirstSave<UserEntity> gate = new();
            await using UserManagementDbContext firstDb = new(new DbContextOptionsBuilder<UserManagementDbContext>()
                .UseSqlite($"Data Source={path}").AddInterceptors(gate).Options);
            await using UserManagementDbContext secondDb = new(new DbContextOptionsBuilder<UserManagementDbContext>()
                .UseSqlite($"Data Source={path}").Options);
            UserIdentityRequest request = new("default", OrganizationId, "Alpha", Oid, null, null);

            Task<UserIdentityResponse> first = CreateService(firstDb).ResolveAsync(request, CancellationToken.None);
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            UserIdentityResponse winner;
            try
            {
                winner = await CreateService(secondDb).ResolveAsync(request, CancellationToken.None);
            }
            finally
            {
                gate.Release();
            }
            UserIdentityResponse loser = await first.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.Equal(winner.UserId, loser.UserId);
            Assert.Equal(winner.Roles.Single().RoleId, loser.Roles.Single().RoleId);
            Assert.Single(await secondDb.Users.AsNoTracking().ToListAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task MissingRevisionRow_RollsBackOrganizationRenameAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using UserManagementDbContext db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        UserManagementService service = CreateService(db);
        await service.CreateUnitOrganizationAsync(new(OrganizationId, "default", "Alpha"), CancellationToken.None);
        await db.Revisions.ExecuteDeleteAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateUnitOrganizationAsync(
            "default", OrganizationId, new("Changed"), CancellationToken.None));
        db.ChangeTracker.Clear();
        Assert.Equal("Alpha", (await service.GetUnitOrganizationAsync("default", OrganizationId,
            CancellationToken.None))?.Name);
    }

    [Fact]
    public async Task ConcurrentRoleCreation_ReturnsDuplicateRoleErrorWithoutExtraRowAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), $"um-role-admin-race-{Guid.NewGuid():N}.db");
        try
        {
            await using (UserManagementDbContext setup = new(new DbContextOptionsBuilder<UserManagementDbContext>()
                .UseSqlite($"Data Source={path}").Options))
            {
                await setup.Database.EnsureCreatedAsync();
                await CreateService(setup).CreateUnitOrganizationAsync(new(OrganizationId, "default", "Alpha"),
                    CancellationToken.None);
            }
            BlockingFirstSave<RoleEntity> gate = new();
            await using UserManagementDbContext firstDb = new(new DbContextOptionsBuilder<UserManagementDbContext>()
                .UseSqlite($"Data Source={path}").AddInterceptors(gate).Options);
            await using UserManagementDbContext secondDb = new(new DbContextOptionsBuilder<UserManagementDbContext>()
                .UseSqlite($"Data Source={path}").Options);
            CreateRoleRequest request = new("default", OrganizationId, "admin", "Admin");

            Task<RoleInfo> first = CreateService(firstDb).CreateRoleAsync(request, CancellationToken.None);
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            try
            {
                await CreateService(secondDb).CreateRoleAsync(request, CancellationToken.None);
            }
            finally
            {
                gate.Release();
            }

            await Assert.ThrowsAsync<ArgumentException>(() => first);
            Assert.Single(await secondDb.Roles.AsNoTracking().ToListAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private sealed class BlockingFirstSave<TEntity>(EntityState state = EntityState.Added)
        : SaveChangesInterceptor where TEntity : class
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Entered => _entered;
        public void Release() => _release.TrySetResult();

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<TEntity>()
                    .Any(entry => entry.State == state) == true)
            {
                _entered.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private static UserManagementDbContext CreateDb(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<UserManagementDbContext>().UseSqlite(connection).Options);

    private static UserManagementService CreateService(UserManagementDbContext db) =>
        new(new SqlUserStore(db, NullLogger<SqlUserStore>.Instance),
            Options.Create(new UserManagementOptions()), NullLogger<UserManagementService>.Instance);
}
