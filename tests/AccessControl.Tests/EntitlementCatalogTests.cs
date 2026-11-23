using AccessControl.Application;
using AccessControl.Contracts;
using AccessControl.Domain;
using AccessControl.Infrastructure.Database;
using AccessControl.Infrastructure.Caching;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AccessControl.Tests;

public sealed class EntitlementCatalogTests
{
    private static readonly Guid OrganizationA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OrganizationB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Role = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task EntitlementCodeIsUniqueWithinOrganizationIncludingInactiveEntitlementsAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using AccessControlDbContext db = new(new DbContextOptionsBuilder<AccessControlDbContext>()
            .UseSqlite(connection).Options, TestActorProvider.Instance);
        await db.Database.EnsureCreatedAsync();
        EntitlementCatalogService service = new(new SqlEntitlementCatalog(db), NullLogger<EntitlementCatalogService>.Instance);
        CreateEntitlementRequest firstRequest = System.Text.Json.JsonSerializer.Deserialize<CreateEntitlementRequest>(
            $$"""{"tenantId":"tenant-a","unitOrganizationId":"{{OrganizationA}}","code":"Customers.View","displayName":"View customers"}""",
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        EntitlementInfo first = await service.CreateAsync(firstRequest, CancellationToken.None);
        Assert.Equal("customers.view", first.Code);
        await service.SetActiveAsync("tenant-a", OrganizationA, first.Id, false, CancellationToken.None);
        CreateEntitlementRequest duplicate = System.Text.Json.JsonSerializer.Deserialize<CreateEntitlementRequest>(
            $$"""{"tenantId":"tenant-a","unitOrganizationId":"{{OrganizationA}}","code":"customers.view","displayName":"Another name"}""",
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

        await Assert.ThrowsAsync<EntitlementCodeConflictException>(() => service.CreateAsync(duplicate, CancellationToken.None));
        Assert.Single(await db.Entitlements.ToListAsync());
        EntitlementInfo otherOrganization = await service.CreateAsync(
            new("tenant-a", OrganizationB, "View customers", "customers.view"), CancellationToken.None);
        Assert.Equal("customers.view", otherOrganization.Code);
    }

    [Fact]
    public async Task GetEntitlement_UsesTenantAndOrganizationScopeAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using AccessControlDbContext db = new(new DbContextOptionsBuilder<AccessControlDbContext>().UseSqlite(connection).Options, TestActorProvider.Instance);
        await db.Database.EnsureCreatedAsync();
        EntitlementCatalogService service = new(new SqlEntitlementCatalog(db), NullLogger<EntitlementCatalogService>.Instance);
        EntitlementInfo created = await service.CreateAsync(new("tenant-a", OrganizationA, "View", "customers.view"), CancellationToken.None);

        Assert.Equal(created, await service.GetAsync("tenant-a", OrganizationA, created.Id, CancellationToken.None));
        Assert.Null(await service.GetAsync("tenant-a", OrganizationB, created.Id, CancellationToken.None));
        Assert.Null(await service.GetAsync("tenant-b", OrganizationA, created.Id, CancellationToken.None));
    }

    [Fact]
    public async Task RenameDisplayName_ChangesOnlyMetadataWithinItsScopeAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using AccessControlDbContext db = new(new DbContextOptionsBuilder<AccessControlDbContext>().UseSqlite(connection).Options, TestActorProvider.Instance);
        await db.Database.EnsureCreatedAsync();
        EntitlementCatalogService service = new(new SqlEntitlementCatalog(db), NullLogger<EntitlementCatalogService>.Instance);
        EntitlementInfo created = await service.CreateAsync(new("tenant-a", OrganizationA, "Old name", "customers.view"), CancellationToken.None);
        long revision = (await db.AuthorizationRevisions.SingleAsync()).Version;

        EntitlementInfo? updated = await service.UpdateDisplayNameAsync("TENANT-A", OrganizationA,
            created.Id, "  View customers  ", CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal(created.Code, updated.Code);
        Assert.Equal("View customers", updated.DisplayName);
        Assert.Equal(created.IsActive, updated.IsActive);
        Assert.Equal(revision, (await db.AuthorizationRevisions.SingleAsync()).Version);
        Assert.Equal("View customers", (await service.ListAsync("tenant-a", OrganizationA, 1, 10,
            CancellationToken.None)).Items.Single().DisplayName);
        Assert.Null(await service.UpdateDisplayNameAsync("tenant-a", OrganizationB, created.Id, "Wrong scope", CancellationToken.None));
        Assert.Null(await service.UpdateDisplayNameAsync("tenant-b", OrganizationA, created.Id, "Wrong tenant", CancellationToken.None));
        Assert.Null(await service.UpdateDisplayNameAsync("tenant-a", OrganizationA, Guid.NewGuid(), "Unknown", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateDisplayNameAsync("tenant-a", OrganizationA,
            created.Id, " ", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateDisplayNameAsync("tenant-a", OrganizationA,
            created.Id, new string('x', 201), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateDisplayNameAsync("tenant-a", Guid.Empty,
            created.Id, "Name", CancellationToken.None));
    }

    [Fact]
    public async Task CreatedEntitlement_RemainsListedWhenUnreferencedAndCanBeDisabledAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using AccessControlDbContext db = new(new DbContextOptionsBuilder<AccessControlDbContext>().UseSqlite(connection).Options, TestActorProvider.Instance);
        await db.Database.EnsureCreatedAsync();
        EntitlementCatalogService service = new(new SqlEntitlementCatalog(db), NullLogger<EntitlementCatalogService>.Instance);

        EntitlementInfo created = await service.CreateAsync(new CreateEntitlementRequest("tenant-a", OrganizationA, "Export customers", "customers.export"), CancellationToken.None);
        Assert.Equal("Export customers", created.DisplayName);
        Assert.True(created.IsActive);
        Assert.Contains((await service.ListAsync("tenant-a", OrganizationA, 1, 100, CancellationToken.None)).Items,
            item => item.Id == created.Id);
        Assert.True(await service.SetActiveAsync("tenant-a", OrganizationA, created.Id, false, CancellationToken.None));
        Assert.False((await service.ListAsync("tenant-a", OrganizationA, 1, 100, CancellationToken.None)).Items.Single().IsActive);
    }

    [Fact]
    public async Task DisabledEntitlement_RevokesCachedDecisionAndCannotBeAssignedAcrossScopesAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using AccessControlDbContext db = new(new DbContextOptionsBuilder<AccessControlDbContext>().UseSqlite(connection).Options, TestActorProvider.Instance);
        await db.Database.EnsureCreatedAsync();
        using AuthorizationCache cache = new(Options.Create(new AccessControlCacheOptions()), NullLogger<AuthorizationCache>.Instance);
        EntitlementCatalogService catalog = new(new SqlEntitlementCatalog(db, cache), NullLogger<EntitlementCatalogService>.Instance);
        EntitlementInfo view = await catalog.CreateAsync(new("tenant-a", OrganizationA, "View", "customers.view"), CancellationToken.None);
        EntitlementInfo other = await catalog.CreateAsync(new("tenant-a", OrganizationB, "View", "customers.view"), CancellationToken.None);
        Assert.NotEqual(view.Id, other.Id);
        EntitlementInfo duplicate = await catalog.CreateAsync(
            new("tenant-a", OrganizationA, "View", "customers.view-copy"), CancellationToken.None);
        Assert.NotEqual(view.Id, duplicate.Id);
        SqlAuthorizationStore sql = new(db, NullLogger<SqlAuthorizationStore>.Instance);
        CachingAuthorizationStore cached = new(sql, cache, NullLogger<CachingAuthorizationStore>.Instance);
        AuthorizationService decisions = new(cached, NullLogger<AuthorizationService>.Instance);
        await Assert.ThrowsAsync<ArgumentException>(() => decisions.ReplaceRoleEntitlementsAsync(
            new("tenant-a", Role, [other.Id], OrganizationA), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => decisions.UpsertPolicyAsync(
            new("tenant-a", "GET", "/customers/{id}", false, [other.Id], OrganizationA), CancellationToken.None));
        await decisions.ReplaceRoleEntitlementsAsync(new("tenant-a", Role, [view.Id], OrganizationA), CancellationToken.None);
        await decisions.UpsertPolicyAsync(new("tenant-a", "GET", "/customers/{id}", false,
            [view.Id], OrganizationA), CancellationToken.None);
        DecisionRequest request = new("aaaaaaaa-1111-1111-1111-111111111111", "GET", "/customers/{id}", "tenant-a", OrganizationA, [Role]);
        Assert.True((await decisions.DecideAsync(request, CancellationToken.None)).Permit);
        using AuthorizationCache secondCache = new(Options.Create(new AccessControlCacheOptions()), NullLogger<AuthorizationCache>.Instance);
        AuthorizationService secondInstance = new(new CachingAuthorizationStore(sql, secondCache,
            NullLogger<CachingAuthorizationStore>.Instance), NullLogger<AuthorizationService>.Instance);
        long revision = await sql.GetRevisionAsync(CancellationToken.None);
        secondCache.ObserveRevision(revision);
        Assert.True((await secondInstance.DecideAsync(request, CancellationToken.None)).Permit);
        Assert.True(await catalog.SetActiveAsync("tenant-a", OrganizationA, view.Id, false, CancellationToken.None));
        Assert.Equal(revision + 1, await sql.GetRevisionAsync(CancellationToken.None));
        Assert.False((await decisions.DecideAsync(request, CancellationToken.None)).Permit);
        Assert.True((await secondInstance.DecideAsync(request, CancellationToken.None)).Permit);
        secondCache.ObserveRevision(revision + 1);
        Assert.False((await secondInstance.DecideAsync(request, CancellationToken.None)).Permit);
        Assert.Equal([view.Id], (await decisions.GetRoleEntitlementsAsync("tenant-a", OrganizationA, Role,
            CancellationToken.None)).EntitlementIds);
        await Assert.ThrowsAsync<ArgumentException>(() => decisions.ReplaceRoleEntitlementsAsync(
            new("tenant-a", Role, [view.Id], OrganizationA), CancellationToken.None));
        Assert.True(await catalog.SetActiveAsync("tenant-a", OrganizationA, view.Id, true, CancellationToken.None));
        Assert.True((await decisions.DecideAsync(request, CancellationToken.None)).Permit);
    }

    [Fact]
    public async Task ListAsync_IncludesUnreferencedCatalogEntriesAndRespectsScopeAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using AccessControlDbContext db = new(new DbContextOptionsBuilder<AccessControlDbContext>().UseSqlite(connection).Options, TestActorProvider.Instance);
        await db.Database.EnsureCreatedAsync();
        EntitlementCatalogService service = new(new SqlEntitlementCatalog(db), NullLogger<EntitlementCatalogService>.Instance);
        await service.CreateAsync(new("tenant-a", OrganizationA, "View", "customers.view"), CancellationToken.None);
        await service.CreateAsync(new("tenant-a", OrganizationA, "Edit", "customers.edit"), CancellationToken.None);
        await service.CreateAsync(new("tenant-a", OrganizationA, "Delete", "customers.delete"), CancellationToken.None);
        await service.CreateAsync(new("tenant-a", OrganizationB, "Billing", "billing.view"), CancellationToken.None);
        await service.CreateAsync(new("tenant-b", OrganizationA, "Secret", "secret.view"), CancellationToken.None);

        EntitlementPageResponse all = await service.ListAsync("tenant-a", null, 1, 100, CancellationToken.None);
        EntitlementPageResponse oneOrganization = await service.ListAsync("tenant-a", OrganizationA, 1, 100,
            CancellationToken.None);
        EntitlementPageResponse secondPage = await service.ListAsync("tenant-a", null, 2, 2, CancellationToken.None);

        Assert.Equal(4, all.TotalCount);
        Assert.Equal(["Delete", "Edit", "View", "Billing"], all.Items.Select(item => item.DisplayName));
        Assert.Equal(["Delete", "Edit", "View"], oneOrganization.Items.Select(item => item.DisplayName));
        Assert.Equal(3, oneOrganization.TotalCount);
        Assert.Equal(["View", "Billing"], secondPage.Items.Select(item => item.DisplayName));
        Assert.Equal(4, secondPage.TotalCount);
    }

    [Fact]
    public async Task ListAsync_RejectsInvalidScopeAndPaginationBeforeQueryAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using AccessControlDbContext db = new(new DbContextOptionsBuilder<AccessControlDbContext>().UseSqlite(connection).Options, TestActorProvider.Instance);
        EntitlementCatalogService service = new(new SqlEntitlementCatalog(db), NullLogger<EntitlementCatalogService>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync(" ", null, 1, 10, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync("tenant-a", Guid.Empty, 1, 10, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync("tenant-a", null, 0, 10, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync("tenant-a", null, 1, 101, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync("tenant-a", null, int.MaxValue, 100, CancellationToken.None));
    }
}
