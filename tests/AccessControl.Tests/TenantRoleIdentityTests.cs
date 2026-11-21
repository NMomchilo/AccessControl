using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using AccessControl.Application;
using AccessControl.Contracts;
using AccessControl.Domain;
using AccessControl.Infrastructure.Database;

namespace AccessControl.Tests;

public sealed class TenantRoleIdentityTests
{
    private const string Oid = "9155ff4d-2708-4770-b87e-e238645ec5df";
    private static readonly Guid OrganizationA = Guid.Parse("b7799675-bc96-4d41-b920-c7be28c027da");
    private static readonly Guid OrganizationB = Guid.Parse("b27e8551-69fa-43d9-b0fa-8f3e0a485b42");





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
 DisplayName = "View customers", IsActive = true
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        SqlAuthorizationStore store = new(db, NullLogger<SqlAuthorizationStore>.Instance);
        Guid currentRole = allowed;
        AuthorizationService service = new(store, NullLogger<AuthorizationService>.Instance);
        await store.UpsertPolicyAsync(new EndpointPolicy("tenant-a", "GET", "/customers/{id}",
            false, [TestEntitlements.View], organizationId), CancellationToken.None);
        await store.ReplaceRoleEntitlementsAsync("tenant-a", organizationId, allowed, [TestEntitlements.View], CancellationToken.None);

        Assert.True((await service.DecideAsync(new DecisionRequest(Oid, "GET",
            "/customers/{id}", "tenant-a", organizationId, [currentRole]), CancellationToken.None)).Permit);
        currentRole = denied;
        Assert.False((await service.DecideAsync(new DecisionRequest(Oid, "GET",
            "/customers/{id}", "tenant-a", organizationId, [currentRole]), CancellationToken.None)).Permit);
        currentRole = allowed;
        Assert.False((await service.DecideAsync(new DecisionRequest(Oid, "GET",
            "/customers/{id}", "tenant-b", organizationId, [currentRole]), CancellationToken.None)).Permit);
        Assert.False((await service.DecideAsync(new DecisionRequest(Oid, "GET",
            "/customers/{id}", "tenant-a", OrganizationB, [currentRole]), CancellationToken.None)).Permit);
    }

}
