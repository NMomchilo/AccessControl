using System.Text.Json;
using AccessControl.Domain;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AccessControl.Infrastructure.Caching;

public sealed class AuthorizationCache : IDisposable
{
    private readonly MemoryCache _memory;
    private readonly SemaphoreSlim[] _locks = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private readonly TimeSpan _entryLifetime;
    private readonly ILogger<AuthorizationCache> _logger;
    private long _generation;
    private long _observedRevision = -1;

    public AuthorizationCache(IOptions<AccessControlCacheOptions> options, ILogger<AuthorizationCache> logger)
    {
        _memory = new MemoryCache(new MemoryCacheOptions { SizeLimit = options.Value.MaximumEntries });
        _entryLifetime = options.Value.EntryLifetime;
        _logger = logger;
        _logger.LogInformation("Authorization cache enabled with {MaximumEntries} entries and {EntryLifetime} lifetime",
            options.Value.MaximumEntries, _entryLifetime);
    }

    public Task<EndpointPolicy?> GetPolicyAsync(string tenantId, string module, string httpMethod, string routeTemplate,
        Func<CancellationToken, Task<EndpointPolicy?>> load, CancellationToken cancellationToken) =>
        GetAsync(new PolicyKey(tenantId, module, httpMethod, routeTemplate), load, cancellationToken);

    public Task<IReadOnlySet<string>> GetGrantsAsync(string tenantId, IReadOnlyList<string> roles,
        Func<CancellationToken, Task<IReadOnlySet<string>>> load, CancellationToken cancellationToken)
    {
        string roleKey = JsonSerializer.Serialize(roles.Select(role => role.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        return GetAsync(new GrantsKey(tenantId, roleKey), load, cancellationToken);
    }

    public void InvalidateAll(string reason)
    {
        Interlocked.Increment(ref _generation);
        _memory.Compact(1.0);
        _logger.LogInformation("Authorization cache invalidated: {Reason}", reason);
    }

    public void ObserveRevision(long revision)
    {
        long previous = Interlocked.Exchange(ref _observedRevision, revision);
        if (previous >= 0 && previous != revision)
        {
            InvalidateAll("database revision changed");
            _logger.LogInformation("Authorization revision changed from {PreviousRevision} to {Revision}", previous, revision);
        }
        else if (previous < 0)
        {
            InvalidateAll("initial database revision observed");
            _logger.LogDebug("Authorization revision baseline is {Revision}", revision);
        }
    }

    public void Dispose()
    {
        _memory.Dispose();
        foreach (SemaphoreSlim gate in _locks)
        {
            gate.Dispose();
        }
    }

    private async Task<T> GetAsync<T>(object key, Func<CancellationToken, Task<T>> load, CancellationToken cancellationToken)
    {
        if (TryGet(key, out T? value))
        {
            _logger.LogDebug("Authorization cache hit for {CacheKind}", key.GetType().Name);
            return value!;
        }

        SemaphoreSlim gate = _locks[(int)((uint)key.GetHashCode() % (uint)_locks.Length)];
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (TryGet(key, out value))
            {
                _logger.LogDebug("Authorization cache hit after waiting for {CacheKind}", key.GetType().Name);
                return value!;
            }

            long generation = Volatile.Read(ref _generation);
            DateTimeOffset expires = DateTimeOffset.UtcNow.Add(_entryLifetime);
            _logger.LogDebug("Authorization cache miss for {CacheKind}", key.GetType().Name);
            T loaded = await load(cancellationToken);
            if (generation == Volatile.Read(ref _generation) && expires > DateTimeOffset.UtcNow)
            {
                _memory.Set(key, new CacheEntry<T>(loaded, generation),
                    new MemoryCacheEntryOptions { AbsoluteExpiration = expires, Size = 1 });
            }
            return loaded;
        }
        finally
        {
            gate.Release();
        }
    }

    private bool TryGet<T>(object key, out T? value)
    {
        if (_memory.TryGetValue(key, out object? raw) && raw is CacheEntry<T> entry &&
            entry.Generation == Volatile.Read(ref _generation))
        {
            value = entry.Value;
            return true;
        }
        value = default;
        return false;
    }

    private sealed record CacheEntry<T>(T Value, long Generation);
    private sealed record PolicyKey(string TenantId, string Module, string HttpMethod, string RouteTemplate);
    private sealed record GrantsKey(string TenantId, string Roles);
}
