using AccessControl.Application;
using AccessControl.Domain;
using AccessControl.Infrastructure.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AccessControl.Tests;

public sealed class AuthorizationCacheTests
{
    [Fact]
    public async Task ConcurrentMisses_UseOneDatabaseLoadAsync()
    {
        using AuthorizationCache cache = CreateCache();
        int reads = 0;
        async Task<EndpointPolicy?> Load(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref reads);
            await Task.Delay(30, cancellationToken);
            return new EndpointPolicy("tenant", "customers", "GET", "/customers/{id}", false, ["customers.view"]);
        }

        Task<EndpointPolicy?>[] tasks = Enumerable.Range(0, 20).Select(_ =>
            cache.GetPolicyAsync("tenant", "customers", "GET", "/customers/{id}", Load, CancellationToken.None)).ToArray();
        await Task.WhenAll(tasks);

        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task InFlightLoad_DoesNotRepopulateAfterInvalidationAsync()
    {
        using AuthorizationCache cache = CreateCache();
        TaskCompletionSource<EndpointPolicy?> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        EndpointPolicy oldPolicy = new("tenant", "customers", "GET", "/customers/{id}", false, ["customers.view"]);
        EndpointPolicy newPolicy = oldPolicy with { IsPublic = true, RequiredEntitlements = [] };
        Task<EndpointPolicy?> first = cache.GetPolicyAsync("tenant", "customers", "GET", "/customers/{id}",
            _ => pending.Task, CancellationToken.None);
        cache.InvalidateAll("test update");
        pending.SetResult(oldPolicy);
        await first;

        EndpointPolicy? current = await cache.GetPolicyAsync("tenant", "customers", "GET", "/customers/{id}",
            _ => Task.FromResult<EndpointPolicy?>(newPolicy), CancellationToken.None);
        Assert.True(current?.IsPublic);
    }

    [Fact]
    public async Task RepeatedReads_UseCachedPolicyAndGrantsAsync()
    {
        FakeStore source = new();
        using AuthorizationCache cache = CreateCache();
        CachingAuthorizationStore store = Wrap(source, cache);

        await store.GetPolicyAsync("tenant", "customers", "GET", "/customers/{id}", CancellationToken.None);
        await store.GetPolicyAsync("tenant", "customers", "GET", "/customers/{id}", CancellationToken.None);
        await store.GetEntitlementsAsync("tenant", ["advisor", "admin"], CancellationToken.None);
        await store.GetEntitlementsAsync("tenant", ["ADMIN", "Advisor"], CancellationToken.None);

        Assert.Equal(1, source.PolicyReads);
        Assert.Equal(1, source.GrantReads);
    }

    [Fact]
    public async Task PolicyWrite_InvalidatesLocalCacheAsync()
    {
        FakeStore source = new();
        using AuthorizationCache cache = CreateCache();
        CachingAuthorizationStore store = Wrap(source, cache);
        await store.GetPolicyAsync("tenant", "customers", "GET", "/customers/{id}", CancellationToken.None);

        await store.UpsertPolicyAsync(source.Policy with { IsPublic = true, RequiredEntitlements = [] }, CancellationToken.None);
        EndpointPolicy? updated = await store.GetPolicyAsync("tenant", "customers", "GET", "/customers/{id}", CancellationToken.None);

        Assert.True(updated?.IsPublic);
        Assert.Equal(2, source.PolicyReads);
    }

    [Fact]
    public async Task RoleGrantWrite_InvalidatesLocalCacheAsync()
    {
        FakeStore source = new();
        using AuthorizationCache cache = CreateCache();
        CachingAuthorizationStore store = Wrap(source, cache);
        await store.GetEntitlementsAsync("tenant", ["advisor"], CancellationToken.None);

        await store.ReplaceRoleEntitlementsAsync("tenant", "advisor", [], CancellationToken.None);
        IReadOnlySet<string> updated = await store.GetEntitlementsAsync("tenant", ["advisor"], CancellationToken.None);

        Assert.Empty(updated);
        Assert.Equal(2, source.GrantReads);
    }

    [Fact]
    public async Task NewRevision_InvalidatesOtherInstanceAsync()
    {
        FakeStore source = new();
        using AuthorizationCache first = CreateCache();
        using AuthorizationCache second = CreateCache();
        CachingAuthorizationStore writer = Wrap(source, first);
        CachingAuthorizationStore reader = Wrap(source, second);
        second.ObserveRevision(0);
        await reader.GetEntitlementsAsync("tenant", ["advisor"], CancellationToken.None);

        await writer.ReplaceRoleEntitlementsAsync("tenant", "advisor", [], CancellationToken.None);
        Assert.Contains("customers.view", await reader.GetEntitlementsAsync("tenant", ["advisor"], CancellationToken.None));
        second.ObserveRevision(1);

        Assert.Empty(await reader.GetEntitlementsAsync("tenant", ["advisor"], CancellationToken.None));
    }

    [Fact]
    public async Task EntryExpiresAndSizeLimitIsRespectedAsync()
    {
        FakeStore source = new();
        using AuthorizationCache cache = CreateCache(maxEntries: 1, lifetime: TimeSpan.FromMilliseconds(30));
        CachingAuthorizationStore store = Wrap(source, cache);
        await store.GetPolicyAsync("tenant", "customers", "GET", "/customers/{id}", CancellationToken.None);
        await store.GetPolicyAsync("another-tenant", "customers", "GET", "/customers/{id}", CancellationToken.None);
        await store.GetPolicyAsync("another-tenant", "customers", "GET", "/customers/{id}", CancellationToken.None);
        Assert.Equal(3, source.PolicyReads);

        await Task.Delay(50);
        await store.GetPolicyAsync("tenant", "customers", "GET", "/customers/{id}", CancellationToken.None);
        Assert.Equal(4, source.PolicyReads);
    }

    private static AuthorizationCache CreateCache(int maxEntries = 10, TimeSpan? lifetime = null) =>
        new(Options.Create(new AccessControlCacheOptions
        {
            MaximumEntries = maxEntries,
            EntryLifetime = lifetime ?? TimeSpan.FromSeconds(5)
        }), NullLogger<AuthorizationCache>.Instance);

    private static CachingAuthorizationStore Wrap(FakeStore source, AuthorizationCache cache) =>
        new(source, cache, NullLogger<CachingAuthorizationStore>.Instance);

    private sealed class FakeStore : IAuthorizationStore
    {
        public EndpointPolicy Policy { get; private set; } = new("tenant", "customers", "GET", "/customers/{id}",
            false, ["customers.view"]);
        public IReadOnlySet<string> Grants { get; private set; } = new HashSet<string> { "customers.view" };
        public int PolicyReads { get; private set; }
        public int GrantReads { get; private set; }

        public Task<EndpointPolicy?> GetPolicyAsync(string tenantId, string module, string httpMethod,
            string routeTemplate, CancellationToken cancellationToken)
        {
            PolicyReads++;
            return Task.FromResult<EndpointPolicy?>(Policy.TenantId == tenantId ? Policy : null);
        }

        public Task<IReadOnlySet<string>> GetEntitlementsAsync(string tenantId, IReadOnlyList<string> roles,
            CancellationToken cancellationToken)
        {
            GrantReads++;
            return Task.FromResult(Grants);
        }

        public Task UpsertPolicyAsync(EndpointPolicy policy, CancellationToken cancellationToken)
        {
            Policy = policy;
            return Task.CompletedTask;
        }

        public Task DeletePolicyAsync(string tenantId, string module, string httpMethod,
            string routeTemplate, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ReplaceRoleEntitlementsAsync(string tenantId, string role, IReadOnlyList<string> entitlements,
            CancellationToken cancellationToken)
        {
            Grants = entitlements.ToHashSet();
            return Task.CompletedTask;
        }
    }
}
