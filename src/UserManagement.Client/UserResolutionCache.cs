using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UserManagement.Contracts;

namespace UserManagement.Client;

public sealed class UserResolutionCache(IUserManagementClient source,
    IOptions<UserManagementClientOptions> options, ILogger<UserResolutionCache> logger) : IUserManagementClient, IDisposable
{
    private readonly MemoryCache _memory = new(new MemoryCacheOptions { SizeLimit = options.Value.MaximumCachedUsers });
    private readonly Dictionary<string, KeyedGate> _gates = new(StringComparer.Ordinal);
    private readonly object _gatesLock = new();
    private long _generation;
    private long _observedRevision = -1;

    public async Task<UserIdentityResponse> ResolveAsync(UserIdentityRequest request,
        CancellationToken cancellationToken)
    {
        string key = $"{request.TenantId.Trim().ToLowerInvariant()}|{request.UnitOrganizationId:D}|{request.ObjectId}";
        if (TryGet(key, out UserIdentityResponse? cached))
        {
            logger.LogDebug("User role cache hit");
            return cached!;
        }

        KeyedGate gate;
        lock (_gatesLock)
        {
            if (!_gates.TryGetValue(key, out gate!))
            {
                gate = new KeyedGate();
                _gates.Add(key, gate);
            }

            gate.Users++;
        }

        try
        {
            await gate.Semaphore.WaitAsync(cancellationToken);
            try
            {
                if (TryGet(key, out cached))
                {
                    logger.LogDebug("User role cache hit after wait");
                    return cached!;
                }

                DateTimeOffset expires = DateTimeOffset.UtcNow.Add(options.Value.CacheLifetime);
                long generation = Volatile.Read(ref _generation);
                logger.LogDebug("User role cache miss");
                UserIdentityResponse resolved = await source.ResolveAsync(request, cancellationToken);
                if (generation == Volatile.Read(ref _generation) && expires > DateTimeOffset.UtcNow)
                {
                    _memory.Set(key, new CacheEntry(resolved, generation),
                        new MemoryCacheEntryOptions { AbsoluteExpiration = expires, Size = 1 });
                }

                return resolved;
            }
            finally
            {
                gate.Semaphore.Release();
            }
        }
        finally
        {
            lock (_gatesLock)
            {
                gate.Users--;
                if (gate.Users == 0)
                {
                    _gates.Remove(key);
                    gate.Semaphore.Dispose();
                }
            }
        }
    }

    public void ObserveRevision(long revision)
    {
        long previous = Interlocked.Exchange(ref _observedRevision, revision);
        if (previous >= 0 && previous != revision)
        {
            Interlocked.Increment(ref _generation);
            _memory.Compact(1.0);
            logger.LogInformation("User role cache invalidated by revision change from {Previous} to {Current}",
                previous, revision);
        }
        else if (previous < 0)
        {
            logger.LogDebug("User role revision baseline is {Revision}", revision);
        }
    }

    private bool TryGet(string key, out UserIdentityResponse? response)
    {
        if (_memory.TryGetValue(key, out CacheEntry? entry) && entry is not null &&
            entry.Generation == Volatile.Read(ref _generation))
        {
            response = entry.Response;
            return true;
        }

        response = null;
        return false;
    }

    public void Dispose()
    {
        _memory.Dispose();
        lock (_gatesLock)
        {
            foreach (KeyedGate gate in _gates.Values)
            {
                gate.Semaphore.Dispose();
            }

            _gates.Clear();
        }
    }

    private sealed record CacheEntry(UserIdentityResponse Response, long Generation);

    private sealed class KeyedGate
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int Users { get; set; }
    }
}
