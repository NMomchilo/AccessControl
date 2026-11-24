using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AccessControl.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Infrastructure;

namespace AccessControl.Infrastructure.Caching;

public sealed class RedisAuthorizationCache(IRedisCacheService redis, IOptions<AccessControlCacheOptions> options,
    ILogger<RedisAuthorizationCache> logger) : IAuthorizationCache, IDisposable
{
    private readonly SemaphoreSlim[] _locks = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private long _observedRevision = -1;
    private long _generation;

    public Task<EndpointPolicy?> GetPolicyAsync(string tenantId, Guid unitOrganizationId, string httpMethod,
        string routeTemplate, Func<CancellationToken, Task<EndpointPolicy?>> load, CancellationToken cancellationToken) =>
        GetAsync("policy", $"{tenantId}|{unitOrganizationId:D}|{httpMethod}|{routeTemplate}", load, cancellationToken);

    public async Task<IReadOnlySet<Guid>> GetGrantsAsync(string tenantId, Guid unitOrganizationId,
        IReadOnlyList<Guid> roleIds, Func<CancellationToken, Task<IReadOnlySet<Guid>>> load,
        CancellationToken cancellationToken)
    {
        Guid[] grants = await GetAsync("grants",
            $"{tenantId}|{unitOrganizationId:D}|{string.Join(',', roleIds.Distinct().Order())}",
            async token => (await load(token)).ToArray(), cancellationToken);
        return grants.ToHashSet();
    }

    public void InvalidateAll(string reason)
    {
        Interlocked.Increment(ref _generation);
        logger.LogInformation("Local Redis authorization namespace invalidated: {Reason}", reason);
    }

    public void ObserveRevision(long revision)
    {
        long previous = Interlocked.Exchange(ref _observedRevision, revision);
        if (previous != revision)
            logger.LogInformation("Redis authorization namespace changed from revision {Previous} to {Current}", previous, revision);
    }

    private async Task<T> GetAsync<T>(string kind, string identity, Func<CancellationToken, Task<T>> load,
        CancellationToken cancellationToken)
    {
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        string key = Key(kind, hash);
        (bool found, T? value) = await ReadAsync<T>(key, cancellationToken);
        if (found) return value!;
        SemaphoreSlim gate = _locks[(int)((uint)key.GetHashCode() % (uint)_locks.Length)];
        await gate.WaitAsync(cancellationToken);
        try
        {
            (found, value) = await ReadAsync<T>(key, cancellationToken);
            if (found) return value!;
            DateTimeOffset expires = DateTimeOffset.UtcNow.Add(options.Value.EntryLifetime);
            T loaded = await load(cancellationToken);
            if (key == Key(kind, hash))
                await WriteAsync(key, loaded, expires - DateTimeOffset.UtcNow, cancellationToken);
            return loaded;
        }
        finally { gate.Release(); }
    }

    private string Key(string kind, string hash) =>
        $"ac:{kind}:v1:{Volatile.Read(ref _observedRevision)}:{Volatile.Read(ref _generation)}:{hash}";

    private async Task<(bool Found, T? Value)> ReadAsync<T>(string key, CancellationToken cancellationToken)
    {
        try
        {
            string? json = await redis.GetValueAsync(key, cancellationToken);
            if (json is null) return (false, default);
            CacheEnvelope<T>? entry = JsonSerializer.Deserialize<CacheEnvelope<T>>(json);
            return entry is null ? (false, default) : (true, entry.Value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Redis authorization read failed for {Kind}; loading from SQL Server", key.StartsWith("ac:policy:") ? "policy" : "grants");
            return (false, default);
        }
    }

    private async Task WriteAsync<T>(string key, T value, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        if (lifetime <= TimeSpan.Zero) return;
        try { await redis.SetValueAsync(key, JsonSerializer.Serialize(new CacheEnvelope<T>(value)), lifetime, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { logger.LogWarning(exception, "Redis authorization write failed; request used fresh SQL Server data"); }
    }

    public void Dispose()
    {
        foreach (SemaphoreSlim gate in _locks) gate.Dispose();
    }

    private sealed record CacheEnvelope<T>(T Value);
}
