using AccessControl.Domain;
using AccessControl.Infrastructure.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.Infrastructure;

namespace AccessControl.Tests;

public sealed class RedisAuthorizationCacheTests
{
    [Fact]
    public async Task PolicyCacheIsSharedAndRevisionChangeForcesReload()
    {
        FakeRedis redis = new();
        RedisAuthorizationCache first = Create(redis);
        RedisAuthorizationCache second = Create(redis);
        Guid organization = Guid.NewGuid();
        int loads = 0;
        Task<EndpointPolicy?> Load(CancellationToken _) { loads++; return Task.FromResult<EndpointPolicy?>(null); }

        await first.GetPolicyAsync("default", organization, "GET", "/customers", Load, default);
        await second.GetPolicyAsync("default", organization, "GET", "/customers", Load, default);
        Assert.Equal(1, loads);

        first.ObserveRevision(1);
        second.ObserveRevision(1);
        await second.GetPolicyAsync("default", organization, "GET", "/customers", Load, default);
        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task RedisOutageLoadsPolicyFromSqlSource()
    {
        FakeRedis redis = new() { Fail = true };
        RedisAuthorizationCache cache = Create(redis);
        int loads = 0;
        Task<EndpointPolicy?> Load(CancellationToken _) { loads++; return Task.FromResult<EndpointPolicy?>(null); }

        await cache.GetPolicyAsync("default", Guid.NewGuid(), "GET", "/customers", Load, default);
        Assert.Equal(1, loads);
    }

    [Fact]
    public async Task PolicyAndGrantsAreDeserializedForAnotherInstance()
    {
        FakeRedis redis = new();
        RedisAuthorizationCache first = Create(redis);
        RedisAuthorizationCache second = Create(redis);
        Guid organization = Guid.NewGuid();
        Guid entitlement = Guid.NewGuid();
        Guid role = Guid.NewGuid();
        EndpointPolicy policy = new("default", "GET", "/customers", false, [entitlement], organization);
        int policyLoads = 0;
        int grantLoads = 0;
        Task<EndpointPolicy?> LoadPolicy(CancellationToken _) { policyLoads++; return Task.FromResult<EndpointPolicy?>(policy); }
        Task<IReadOnlySet<Guid>> LoadGrants(CancellationToken _)
        {
            grantLoads++;
            return Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid> { entitlement });
        }

        await first.GetPolicyAsync("default", organization, "GET", "/customers", LoadPolicy, default);
        await first.GetGrantsAsync("default", organization, [role], LoadGrants, default);
        EndpointPolicy? reusedPolicy = await second.GetPolicyAsync("default", organization, "GET", "/customers", LoadPolicy, default);
        IReadOnlySet<Guid> reusedGrants = await second.GetGrantsAsync("default", organization, [role], LoadGrants, default);

        Assert.Equal(1, policyLoads);
        Assert.Equal(1, grantLoads);
        Assert.Equal(entitlement, Assert.Single(reusedPolicy!.RequiredEntitlementIds));
        Assert.Contains(entitlement, reusedGrants);
    }

    private static RedisAuthorizationCache Create(FakeRedis redis) => new(redis,
        Options.Create(new AccessControlCacheOptions()), NullLogger<RedisAuthorizationCache>.Instance);

    private sealed class FakeRedis : IRedisCacheService
    {
        private readonly Dictionary<string, string> _values = new();
        public bool Fail { get; set; }
        public Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default) =>
            Fail ? throw new InvalidOperationException("Redis unavailable") : Task.FromResult(_values.GetValueOrDefault(key));
        public Task SetValueAsync(string key, string value, TimeSpan lifetime, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new InvalidOperationException("Redis unavailable");
            _values[key] = value;
            return Task.CompletedTask;
        }
        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }
    }
}
