using AccessControl.Application;
using AccessControl.Domain;
using AccessControl.Infrastructure.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AccessControl.Tests;

public sealed class AuthorizationCacheTests
{
    private static readonly Guid Advisor = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Admin = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Organization = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherOrganization = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task OrganizationScopeSeparatesPolicyAndGrantCacheKeysAsync()
    {
        using AuthorizationCache cache = CreateCache();
        int policyLoads = 0;
        int grantLoads = 0;
        async Task<EndpointPolicy?> LoadPolicy(CancellationToken _)
        {
            policyLoads++;
            return await Task.FromResult<EndpointPolicy?>(null);
        }
        async Task<IReadOnlySet<Guid>> LoadGrants(CancellationToken _)
        {
            grantLoads++;
            return await Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
        }

        await cache.GetPolicyAsync("tenant", Organization, "customers", "GET", "/customers/{id}", LoadPolicy, CancellationToken.None);
        await cache.GetPolicyAsync("tenant", OtherOrganization, "customers", "GET", "/customers/{id}", LoadPolicy, CancellationToken.None);
        await cache.GetGrantsAsync("tenant", Organization, [Advisor], LoadGrants, CancellationToken.None);
        await cache.GetGrantsAsync("tenant", OtherOrganization, [Advisor], LoadGrants, CancellationToken.None);

        Assert.Equal(2, policyLoads);
        Assert.Equal(2, grantLoads);
    }
    [Fact]
    public async Task ConcurrentMisses_UseOneDatabaseLoadAsync()
    {
        using AuthorizationCache cache = CreateCache();
        int reads = 0;
        async Task<EndpointPolicy?> Load(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref reads);
            await Task.Delay(30, cancellationToken);
            return new EndpointPolicy("tenant", "customers", "GET", "/customers/{id}", false, [TestEntitlements.View], Organization);
        }

        Task<EndpointPolicy?>[] tasks = Enumerable.Range(0, 20).Select(_ =>
            cache.GetPolicyAsync("tenant", Organization, "customers", "GET", "/customers/{id}", Load, CancellationToken.None)).ToArray();
        await Task.WhenAll(tasks);

        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task InFlightLoad_DoesNotRepopulateAfterInvalidationAsync()
    {
        using AuthorizationCache cache = CreateCache();
        TaskCompletionSource<EndpointPolicy?> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        EndpointPolicy oldPolicy = new("tenant", "customers", "GET", "/customers/{id}", false, [TestEntitlements.View], Organization);
        EndpointPolicy newPolicy = oldPolicy with { IsPublic = true, RequiredEntitlementIds = [] };
        Task<EndpointPolicy?> first = cache.GetPolicyAsync("tenant", Organization, "customers", "GET", "/customers/{id}",
            _ => pending.Task, CancellationToken.None);
        cache.InvalidateAll("test update");
        pending.SetResult(oldPolicy);
        await first;

        EndpointPolicy? current = await cache.GetPolicyAsync("tenant", Organization, "customers", "GET", "/customers/{id}",
            _ => Task.FromResult<EndpointPolicy?>(newPolicy), CancellationToken.None);
        Assert.True(current?.IsPublic);
    }

    [Fact]
    public async Task RepeatedReads_UseCachedPolicyAndGrantsAsync()
    {
        FakeStore source = new();
        using AuthorizationCache cache = CreateCache();
        CachingAuthorizationStore store = Wrap(source, cache);

        await store.GetPolicyAsync("tenant", Organization, "customers", "GET", "/customers/{id}", CancellationToken.None);
        await store.GetPolicyAsync("tenant", Organization, "customers", "GET", "/customers/{id}", CancellationToken.None);
        await store.GetEntitlementsAsync("tenant", Organization, [Advisor, Admin], CancellationToken.None);
        await store.GetEntitlementsAsync("tenant", Organization, [Admin, Advisor], CancellationToken.None);

        Assert.Equal(1, source.PolicyReads);
        Assert.Equal(1, source.GrantReads);
    }

    [Fact]
    public async Task PolicyWrite_InvalidatesLocalCacheAsync()
    {
        FakeStore source = new();
        using AuthorizationCache cache = CreateCache();
        CachingAuthorizationStore store = Wrap(source, cache);
        await store.GetPolicyAsync("tenant", Organization, "customers", "GET", "/customers/{id}", CancellationToken.None);

        await store.UpsertPolicyAsync(source.Policy with { IsPublic = true, RequiredEntitlementIds = [] }, CancellationToken.None);
        EndpointPolicy? updated = await store.GetPolicyAsync("tenant", Organization, "customers", "GET", "/customers/{id}", CancellationToken.None);

        Assert.True(updated?.IsPublic);
        Assert.Equal(2, source.PolicyReads);
    }

    [Fact]
    public async Task PolicyDelete_InvalidatesCachedPermitAsync()
    {
        FakeStore source = new();
        using AuthorizationCache cache = CreateCache();
        CachingAuthorizationStore store = Wrap(source, cache);
        Assert.NotNull(await store.GetPolicyAsync("tenant", Organization, "customers", "GET", "/customers/{id}", CancellationToken.None));

        await store.DeletePolicyAsync("tenant", Organization, "customers", "GET", "/customers/{id}", CancellationToken.None);

        Assert.Null(await store.GetPolicyAsync("tenant", Organization, "customers", "GET", "/customers/{id}", CancellationToken.None));
        Assert.Equal(2, source.PolicyReads);
    }

    [Fact]
    public async Task RoleGrantWrite_InvalidatesLocalCacheAsync()
    {
        FakeStore source = new();
        using AuthorizationCache cache = CreateCache();
        CachingAuthorizationStore store = Wrap(source, cache);
        await store.GetEntitlementsAsync("tenant", Organization, [Advisor], CancellationToken.None);

        await store.ReplaceRoleEntitlementsAsync("tenant", Organization, Advisor, [], CancellationToken.None);
        IReadOnlySet<Guid> updated = await store.GetEntitlementsAsync("tenant", Organization, [Advisor], CancellationToken.None);

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
        await reader.GetEntitlementsAsync("tenant", Organization, [Advisor], CancellationToken.None);

        await writer.ReplaceRoleEntitlementsAsync("tenant", Organization, Advisor, [], CancellationToken.None);
        Assert.Contains(TestEntitlements.View, await reader.GetEntitlementsAsync("tenant", Organization, [Advisor], CancellationToken.None));
        second.ObserveRevision(1);

        Assert.Empty(await reader.GetEntitlementsAsync("tenant", Organization, [Advisor], CancellationToken.None));
    }

    [Fact]
    public async Task EntryExpiresAndSizeLimitIsRespectedAsync()
    {
        FakeStore source = new();
        using AuthorizationCache cache = CreateCache(maxEntries: 1, lifetime: TimeSpan.FromMilliseconds(30));
        CachingAuthorizationStore store = Wrap(source, cache);
        await store.GetPolicyAsync("tenant", Organization, "customers", "GET", "/customers/{id}", CancellationToken.None);
        await store.GetPolicyAsync("another-tenant", Organization, "customers", "GET", "/customers/{id}", CancellationToken.None);
        await store.GetPolicyAsync("another-tenant", Organization, "customers", "GET", "/customers/{id}", CancellationToken.None);
        Assert.Equal(3, source.PolicyReads);

        await Task.Delay(50);
        await store.GetPolicyAsync("tenant", Organization, "customers", "GET", "/customers/{id}", CancellationToken.None);
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
            false, [TestEntitlements.View], Organization);
        public IReadOnlySet<Guid> Grants { get; private set; } = new HashSet<Guid> { TestEntitlements.View };
        public int PolicyReads { get; private set; }
        public int GrantReads { get; private set; }
        public bool PolicyDeleted { get; private set; }

        public Task<EndpointPolicy?> GetPolicyAsync(string tenantId, Guid unitOrganizationId, string module, string httpMethod,
            string routeTemplate, CancellationToken cancellationToken)
        {
            PolicyReads++;
            return Task.FromResult<EndpointPolicy?>(!PolicyDeleted && Policy.TenantId == tenantId ? Policy : null);
        }

        public Task<IReadOnlySet<Guid>> GetEntitlementsAsync(string tenantId, Guid unitOrganizationId, IReadOnlyList<Guid> roleIds,
            CancellationToken cancellationToken)
        {
            GrantReads++;
            return Task.FromResult(Grants);
        }

        public Task<IReadOnlyList<Guid>> GetAssignedEntitlementIdsAsync(string tenantId, Guid unitOrganizationId,
            Guid roleId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>(Grants.ToArray());

        public Task UpsertPolicyAsync(EndpointPolicy policy, CancellationToken cancellationToken)
        {
            Policy = policy;
            return Task.CompletedTask;
        }

        public Task DeletePolicyAsync(string tenantId, Guid unitOrganizationId, string module, string httpMethod,
            string routeTemplate, CancellationToken cancellationToken)
        {
            PolicyDeleted = true;
            return Task.CompletedTask;
        }

        public Task ReplaceRoleEntitlementsAsync(string tenantId, Guid unitOrganizationId, Guid roleId, IReadOnlyList<Guid> entitlements,
            CancellationToken cancellationToken)
        {
            Grants = entitlements.ToHashSet();
            return Task.CompletedTask;
        }
    }
}
