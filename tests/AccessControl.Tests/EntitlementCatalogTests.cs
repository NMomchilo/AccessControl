using AccessControl.Application;
using AccessControl.Contracts;
using AccessControl.Domain;
using AccessControl.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessControl.Tests;

public sealed class EntitlementCatalogTests
{
    private static readonly Guid OrganizationA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OrganizationB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Role = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task ListAsync_UnionsAndDeduplicatesPolicyAndGrantNamesWithinScopeAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using AccessControlDbContext db = new(new DbContextOptionsBuilder<AccessControlDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        SqlAuthorizationStore writer = new(db, NullLogger<SqlAuthorizationStore>.Instance);
        await writer.UpsertPolicyAsync(new EndpointPolicy("tenant-a", "customers", "GET", "/customers/{id}",
            false, ["customers.view", "customers.edit"], OrganizationA), CancellationToken.None);
        await writer.ReplaceRoleEntitlementsAsync("tenant-a", OrganizationA, Role,
            ["customers.view", "customers.delete"], CancellationToken.None);
        await writer.ReplaceRoleEntitlementsAsync("tenant-a", OrganizationB, Role,
            ["billing.view"], CancellationToken.None);
        await writer.ReplaceRoleEntitlementsAsync("tenant-b", OrganizationA, Role,
            ["secret.view"], CancellationToken.None);
        EntitlementCatalogService service = new(new SqlEntitlementCatalog(db), NullLogger<EntitlementCatalogService>.Instance);

        EntitlementPageResponse all = await service.ListAsync("tenant-a", null, 1, 100, CancellationToken.None);
        EntitlementPageResponse oneOrganization = await service.ListAsync("tenant-a", OrganizationA, 1, 100,
            CancellationToken.None);
        EntitlementPageResponse secondPage = await service.ListAsync("tenant-a", null, 2, 2, CancellationToken.None);

        Assert.Equal(4, all.TotalCount);
        Assert.Equal(["billing.view", "customers.delete", "customers.edit", "customers.view"], all.Items);
        Assert.Equal(["customers.delete", "customers.edit", "customers.view"], oneOrganization.Items);
        Assert.Equal(3, oneOrganization.TotalCount);
        Assert.Equal(["customers.edit", "customers.view"], secondPage.Items);
        Assert.Equal(4, secondPage.TotalCount);
    }

    [Fact]
    public async Task ListAsync_RejectsInvalidScopeAndPaginationBeforeQueryAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using AccessControlDbContext db = new(new DbContextOptionsBuilder<AccessControlDbContext>().UseSqlite(connection).Options);
        EntitlementCatalogService service = new(new SqlEntitlementCatalog(db), NullLogger<EntitlementCatalogService>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync(" ", null, 1, 10, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync("tenant-a", Guid.Empty, 1, 10, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync("tenant-a", null, 0, 10, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync("tenant-a", null, 1, 101, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync("tenant-a", null, int.MaxValue, 100, CancellationToken.None));
    }
}
