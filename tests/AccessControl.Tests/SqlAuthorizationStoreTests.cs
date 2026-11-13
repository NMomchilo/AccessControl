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
    private static readonly Guid Organization = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherOrganization = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task RevisionPoller_KeepsShortLivedCacheDuringOutageAndClearsOnRecoveryAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddDbContext<AccessControlDbContext>(builder => builder.UseSqlite(connection));
        services.AddScoped<SqlAuthorizationStore>();
        using ServiceProvider provider = services.BuildServiceProvider();
        using AuthorizationCache cache = new(Options.Create(new AccessControlCacheOptions()), NullLogger<AuthorizationCache>.Instance);
        AuthorizationCacheInvalidationService poller = new(provider.GetRequiredService<IServiceScopeFactory>(), cache,
            Options.Create(new AccessControlCacheOptions()), NullLogger<AuthorizationCacheInvalidationService>.Instance);
        int loads = 0;
        Task<EndpointPolicy?> Load(CancellationToken _)
        {
            loads++;
            return Task.FromResult<EndpointPolicy?>(null);
        }
        await cache.GetPolicyAsync("tenant", Organization, "customers", "GET", "/status", Load, CancellationToken.None);

        await poller.CheckOnceAsync(CancellationToken.None); // revision table does not exist yet
        await cache.GetPolicyAsync("tenant", Organization, "customers", "GET", "/status", Load, CancellationToken.None);
        Assert.Equal(1, loads);

        using (IServiceScope scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AccessControlDbContext>().Database.EnsureCreatedAsync();
        }
        await poller.CheckOnceAsync(CancellationToken.None);
        await cache.GetPolicyAsync("tenant", Organization, "customers", "GET", "/status", Load, CancellationToken.None);
        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task EmptyRolesReturnNoGrantsAndMissingRevisionAbortsGrantWriteAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using AccessControlDbContext context = new(
            new DbContextOptionsBuilder<AccessControlDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        SqlAuthorizationStore store = new(context, NullLogger<SqlAuthorizationStore>.Instance);

        Assert.Empty(await store.GetEntitlementsAsync("tenant", Organization, [], CancellationToken.None));
        await SeedAsync(context, "tenant");
        await context.AuthorizationRevisions.ExecuteDeleteAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReplaceRoleEntitlementsAsync(
            "tenant", Organization, Advisor, [TestEntitlements.View], CancellationToken.None));
        Assert.Empty(await context.RoleEntitlements.ToListAsync());
    }

    [Fact]
    public async Task RevisionPoller_PropagatesRequestedCancellationAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddDbContext<AccessControlDbContext>(builder => builder.UseSqlite(connection));
        services.AddScoped<SqlAuthorizationStore>();
        using ServiceProvider provider = services.BuildServiceProvider();
        using AuthorizationCache cache = new(Options.Create(new AccessControlCacheOptions()), NullLogger<AuthorizationCache>.Instance);
        AuthorizationCacheInvalidationService poller = new(provider.GetRequiredService<IServiceScopeFactory>(), cache,
            Options.Create(new AccessControlCacheOptions()), NullLogger<AuthorizationCacheInvalidationService>.Instance);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => poller.CheckOnceAsync(cancellation.Token));
    }

    [Fact]
    public async Task HostedRevisionPollerInvalidatesInitialCachedEntryAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddDbContext<AccessControlDbContext>(builder => builder.UseSqlite(connection));
        services.AddScoped<SqlAuthorizationStore>();
        using ServiceProvider provider = services.BuildServiceProvider();
        using (IServiceScope scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AccessControlDbContext>().Database.EnsureCreatedAsync();
        }
        using AuthorizationCache cache = new(Options.Create(new AccessControlCacheOptions()), NullLogger<AuthorizationCache>.Instance);
        using AuthorizationCacheInvalidationService poller = new(provider.GetRequiredService<IServiceScopeFactory>(), cache,
            Options.Create(new AccessControlCacheOptions { RevisionPollInterval = TimeSpan.FromMilliseconds(10) }),
            NullLogger<AuthorizationCacheInvalidationService>.Instance);
        int loads = 0;
        Task<EndpointPolicy?> Load(CancellationToken _)
        {
            Interlocked.Increment(ref loads);
            return Task.FromResult<EndpointPolicy?>(null);
        }
        await cache.GetPolicyAsync("tenant", Organization, "customers", "GET", "/status", Load, CancellationToken.None);

        await poller.StartAsync(CancellationToken.None);
        try
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
            while (Volatile.Read(ref loads) == 1)
            {
                await Task.Delay(20, timeout.Token);
                await cache.GetPolicyAsync("tenant", Organization, "customers", "GET", "/status", Load, timeout.Token);
            }
            Assert.Equal(2, loads);
        }
        finally
        {
            await poller.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task SameRoleIdInAnotherOrganizationHasNoGrantAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using AccessControlDbContext context = new(
            new DbContextOptionsBuilder<AccessControlDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        await SeedAsync(context, "tenant");
        SqlAuthorizationStore store = new(context, NullLogger<SqlAuthorizationStore>.Instance);
        await store.ReplaceRoleEntitlementsAsync("tenant", Organization, Advisor, [TestEntitlements.View], CancellationToken.None);

        Assert.Contains(TestEntitlements.View, await store.GetEntitlementsAsync("tenant", Organization, [Advisor], CancellationToken.None));
        Assert.Empty(await store.GetEntitlementsAsync("tenant", OtherOrganization, [Advisor], CancellationToken.None));
    }
    [Fact]
    public async Task MutationsIncrementSharedRevisionAsync()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        DbContextOptions<AccessControlDbContext> options = new DbContextOptionsBuilder<AccessControlDbContext>()
            .UseSqlite(connection).Options;
        await using AccessControlDbContext context = new(options);
        await context.Database.EnsureCreatedAsync();
        await SeedAsync(context, "tenant");
        SqlAuthorizationStore store = new(context, NullLogger<SqlAuthorizationStore>.Instance);
        Assert.Equal(0, await store.GetRevisionAsync(CancellationToken.None));

        await store.UpsertPolicyAsync(new EndpointPolicy("tenant", "customers", "GET", "/customers/{id}",
            false, [TestEntitlements.View], Organization), CancellationToken.None);
        Assert.Equal(1, await store.GetRevisionAsync(CancellationToken.None));
        await store.ReplaceRoleEntitlementsAsync("tenant", Organization, Advisor, [TestEntitlements.View], CancellationToken.None);
        Assert.Equal(2, await store.GetRevisionAsync(CancellationToken.None));
        await store.DeletePolicyAsync("tenant", Organization, "customers", "GET", "/customers/{id}", CancellationToken.None);
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
        await SeedAsync(writerContext, "tenant");
        SqlAuthorizationStore writer = new(writerContext, NullLogger<SqlAuthorizationStore>.Instance);
        EndpointPolicy policy = new("tenant", "customers", "GET", "/customers/{id}", false, [TestEntitlements.View], Organization);
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
        Assert.False((await reader.GetPolicyAsync("tenant", Organization, "customers", "GET", "/customers/{id}", CancellationToken.None))?.IsPublic);

        await writer.UpsertPolicyAsync(policy with { IsPublic = true, RequiredEntitlementIds = [] }, CancellationToken.None);
        Assert.False((await reader.GetPolicyAsync("tenant", Organization, "customers", "GET", "/customers/{id}", CancellationToken.None))?.IsPublic);
        await poller.CheckOnceAsync(CancellationToken.None);

        Assert.True((await reader.GetPolicyAsync("tenant", Organization, "customers", "GET", "/customers/{id}", CancellationToken.None))?.IsPublic);
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
        await SeedAsync(context, "tenant-a");
        SqlAuthorizationStore store = new(context, NullLogger<SqlAuthorizationStore>.Instance);
        EndpointPolicy protectedPolicy = new("tenant-a", "customers", "GET", "/customers/{id}", false, [TestEntitlements.View], Organization);
        await store.UpsertPolicyAsync(protectedPolicy, CancellationToken.None);

        await store.UpsertPolicyAsync(protectedPolicy with { IsPublic = true, RequiredEntitlementIds = [] }, CancellationToken.None);
        EndpointPolicy? result = await store.GetPolicyAsync("tenant-a", Organization, "customers", "GET", "/customers/{id}", CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.IsPublic);
        Assert.Empty(result.RequiredEntitlementIds);
        await store.DeletePolicyAsync("tenant-a", Organization, "customers", "GET", "/customers/{id}", CancellationToken.None);
        Assert.Null(await store.GetPolicyAsync("tenant-a", Organization, "customers", "GET", "/customers/{id}", CancellationToken.None));
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
        await SeedAsync(context, "tenant-a");
        SqlAuthorizationStore store = new(context, NullLogger<SqlAuthorizationStore>.Instance);
        AuthorizationService service = new(store, NullLogger<AuthorizationService>.Instance);
        DecisionRequest request = new("alice", [Advisor], "customers", "GET", "/customers/{id}", "tenant-a", Organization);
        await store.UpsertPolicyAsync(new EndpointPolicy("tenant-a", "customers", "GET", "/customers/{id}", false, [TestEntitlements.View], Organization), CancellationToken.None);
        await store.ReplaceRoleEntitlementsAsync("tenant-a", Organization, Advisor, [TestEntitlements.View], CancellationToken.None);

        Assert.True((await service.DecideAsync(request, CancellationToken.None)).Permit);

        await store.ReplaceRoleEntitlementsAsync("tenant-a", Organization, Advisor, [], CancellationToken.None);

        Assert.False((await service.DecideAsync(request, CancellationToken.None)).Permit);
    }

    private static async Task SeedAsync(AccessControlDbContext context, string tenantId)
    {
        context.Entitlements.Add(new EntitlementEntity
        {
            Id = TestEntitlements.View, TenantId = tenantId, UnitOrganizationId = Organization,
            Code = "customers.view", DisplayName = "View customers", IsActive = true
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }
}
