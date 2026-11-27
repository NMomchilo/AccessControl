using AccessControl.Contracts;
using AccessControl.Application;
using AccessControl.Infrastructure.Caching;
using AccessControl.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AccessControl.Tests;

public sealed class CatalogRegistrationTests
{
    private static readonly Guid Organization = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task RegistrationCreatesProtectedPolicyAndReplayDoesNotOverwriteItAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using AccessControlDbContext db = new(new DbContextOptionsBuilder<AccessControlDbContext>()
            .UseSqlite(connection).Options, TestActorProvider.Instance);
        await db.Database.EnsureCreatedAsync();
        using AuthorizationCache cache = new(Options.Create(new AccessControlCacheOptions()),
            NullLogger<AuthorizationCache>.Instance);
        CatalogRegistrationStore store = new(db, cache, NullLogger<CatalogRegistrationStore>.Instance);
        CatalogRegistrationRequest request = new("default", Organization,
            [new CatalogEndpoint("GET", "/customers/{id}")]);

        Assert.Equal(new CatalogRegistrationResult(1), await store.RegisterAsync(request, default));
        EndpointPolicyEntity policy = await db.EndpointPolicies.SingleAsync();
        Assert.False(policy.IsPublic);
        Assert.Empty(policy.RequiredEntitlements);
        AuthorizationService administration = new(new SqlAuthorizationStore(db,
            NullLogger<SqlAuthorizationStore>.Instance), NullLogger<AuthorizationService>.Instance);
        await administration.UpsertPolicyAsync(new PolicyRequest("default", "GET",
            "/customers/{id}", true, [], Organization), default);
        Assert.Equal(new CatalogRegistrationResult(0), await store.RegisterAsync(request, default));
        Assert.True((await db.EndpointPolicies.SingleAsync()).IsPublic);
        Assert.Equal(new CatalogRegistrationResult(0), await store.RegisterAsync(request, default));
        Assert.True((await db.EndpointPolicies.SingleAsync()).IsPublic);
    }

    [Fact]
    public async Task GlobalPublicRouteStillGetsAnEditableOrganizationPolicyAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using AccessControlDbContext db = new(new DbContextOptionsBuilder<AccessControlDbContext>()
            .UseSqlite(connection).Options, TestActorProvider.Instance);
        await db.Database.EnsureCreatedAsync();
        db.EndpointPolicies.Add(new EndpointPolicyEntity
        {
            TenantId = "default", UnitOrganizationId = Guid.Empty,
            HttpMethod = "GET", RouteTemplate = "/health", IsPublic = true
        });
        await db.SaveChangesAsync();
        using AuthorizationCache cache = new(Options.Create(new AccessControlCacheOptions()),
            NullLogger<AuthorizationCache>.Instance);
        CatalogRegistrationStore store = new(db, cache, NullLogger<CatalogRegistrationStore>.Instance);

        CatalogRegistrationResult result = await store.RegisterAsync(new("default", Organization,
            [new CatalogEndpoint("GET", "/health")]), default);

        Assert.Equal(1, result.PoliciesCreated);
        Assert.Equal(2, await db.EndpointPolicies.CountAsync());
        Assert.False((await db.EndpointPolicies.SingleAsync(value =>
            value.UnitOrganizationId == Organization)).IsPublic);
    }
}
