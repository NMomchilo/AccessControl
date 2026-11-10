using AccessControl.Application;
using AccessControl.Contracts;
using AccessControl.Domain;
using AccessControl.Infrastructure.Caching;
using AccessControl.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AccessControl.Tests;

public sealed class SqlAuthorizationStoreTests
{
    private static readonly Guid Advisor = Guid.Parse("11111111-1111-1111-1111-111111111111");
    [Fact]
    public async Task MutationsIncrementSharedRevisionAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        DbContextOptions<AccessControlDbContext> options = new DbContextOptionsBuilder<AccessControlDbContext>()
            .UseSqlite(connection).Options;
        await using AccessControlDbContext context = new(options);
        await context.Database.EnsureCreatedAsync();
        SqlAuthorizationStore store = new(context, NullLogger<SqlAuthorizationStore>.Instance);
        Assert.Equal(0, await store.GetRevisionAsync(CancellationToken.None));

        await store.UpsertPolicyAsync(new EndpointPolicy("tenant", "customers", "GET", "/customers/{id}",
            false, ["customers.view"]), CancellationToken.None);
        Assert.Equal(1, await store.GetRevisionAsync(CancellationToken.None));
        await store.ReplaceRoleEntitlementsAsync("tenant", Advisor, ["customers.view"], CancellationToken.None);
        Assert.Equal(2, await store.GetRevisionAsync(CancellationToken.None));
        await store.DeletePolicyAsync("tenant", "customers", "GET", "/customers/{id}", CancellationToken.None);
        Assert.Equal(3, await store.GetRevisionAsync(CancellationToken.None));
    }

    [Fact]
    public async Task OtherInstancePollerInvalidatesCachedPolicyAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        DbContextOptions<AccessControlDbContext> options = new DbContextOptionsBuilder<AccessControlDbContext>()
            .UseSqlite(connection).Options;
        await using AccessControlDbContext writerContext = new(options);
        await writerContext.Database.EnsureCreatedAsync();
        SqlAuthorizationStore writer = new(writerContext, NullLogger<SqlAuthorizationStore>.Instance);
        EndpointPolicy policy = new("tenant", "customers", "GET", "/customers/{id}", false, ["customers.view"]);
        await writer.UpsertPolicyAsync(policy, CancellationToken.None);

        ServiceCollection services = new();
        services.AddLogging();
        services.AddDbContext<AccessControlDbContext>(builder => builder.UseSqlite(connection));
        services.AddScoped<SqlAuthorizationStore>();
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope readerScope = provider.CreateScope();
        SqlAuthorizationStore readerStore = readerScope.ServiceProvider.GetRequiredService<SqlAuthorizationStore>();
        using AuthorizationCache cache = new(Options.Create(new AccessControlCacheOptions()), NullLogger<AuthorizationCache>.Instance);
        AuthorizationCacheInvalidationService poller = new(provider.GetRequiredService<IServiceScopeFactory>(), cache,
            Options.Create(new AccessControlCacheOptions()), NullLogger<AuthorizationCacheInvalidationService>.Instance);
        CachingAuthorizationStore reader = new(readerStore, cache, NullLogger<CachingAuthorizationStore>.Instance);
        await poller.CheckOnceAsync(CancellationToken.None);
        Assert.False((await reader.GetPolicyAsync("tenant", "customers", "GET", "/customers/{id}", CancellationToken.None))?.IsPublic);

        await writer.UpsertPolicyAsync(policy with { IsPublic = true, RequiredEntitlements = [] }, CancellationToken.None);
        Assert.False((await reader.GetPolicyAsync("tenant", "customers", "GET", "/customers/{id}", CancellationToken.None))?.IsPublic);
        await poller.CheckOnceAsync(CancellationToken.None);

        Assert.True((await reader.GetPolicyAsync("tenant", "customers", "GET", "/customers/{id}", CancellationToken.None))?.IsPublic);
    }

    [Fact]
    public async Task UpsertPolicyAsync_ReplacesEntitlementsAndCanBecomePublicAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        DbContextOptions<AccessControlDbContext> options = new DbContextOptionsBuilder<AccessControlDbContext>()
            .UseSqlite(connection).Options;
        await using AccessControlDbContext context = new(options);
        await context.Database.EnsureCreatedAsync();
        SqlAuthorizationStore store = new(context, NullLogger<SqlAuthorizationStore>.Instance);
        EndpointPolicy protectedPolicy = new("tenant-a", "customers", "GET", "/customers/{id}", false, ["customers.view"]);
        await store.UpsertPolicyAsync(protectedPolicy, CancellationToken.None);

        await store.UpsertPolicyAsync(protectedPolicy with { IsPublic = true, RequiredEntitlements = [] }, CancellationToken.None);
        EndpointPolicy? result = await store.GetPolicyAsync("tenant-a", "customers", "GET", "/customers/{id}", CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.IsPublic);
        Assert.Empty(result.RequiredEntitlements);
        await store.DeletePolicyAsync("tenant-a", "customers", "GET", "/customers/{id}", CancellationToken.None);
        Assert.Null(await store.GetPolicyAsync("tenant-a", "customers", "GET", "/customers/{id}", CancellationToken.None));
    }

    [Fact]
    public async Task ReplaceRoleEntitlementsAsync_RevokesPreviousAccessAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        DbContextOptions<AccessControlDbContext> options = new DbContextOptionsBuilder<AccessControlDbContext>()
            .UseSqlite(connection).Options;
        await using AccessControlDbContext context = new(options);
        await context.Database.EnsureCreatedAsync();
        SqlAuthorizationStore store = new(context, NullLogger<SqlAuthorizationStore>.Instance);
        AuthorizationService service = new(store, NullLogger<AuthorizationService>.Instance);
        DecisionRequest request = new("alice", [Advisor], "customers", "GET", "/customers/{id}", "tenant-a");
        await store.UpsertPolicyAsync(new EndpointPolicy("tenant-a", "customers", "GET", "/customers/{id}", false, ["customers.view"]), CancellationToken.None);
        await store.ReplaceRoleEntitlementsAsync("tenant-a", Advisor, ["customers.view"], CancellationToken.None);

        Assert.True((await service.DecideAsync(request, CancellationToken.None)).Permit);

        await store.ReplaceRoleEntitlementsAsync("tenant-a", Advisor, [], CancellationToken.None);

        Assert.False((await service.DecideAsync(request, CancellationToken.None)).Permit);
    }
}
