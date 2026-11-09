using AccessControl.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AccessControl.Infrastructure.Caching;

public sealed class AuthorizationCacheInvalidationService(IServiceScopeFactory scopeFactory, AuthorizationCache cache,
    IOptions<AccessControlCacheOptions> options, ILogger<AuthorizationCacheInvalidationService> logger) : BackgroundService
{
    private bool _pollingFailed;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(options.Value.RevisionPollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CheckOnceAsync(stoppingToken);
        }
    }

    public async Task CheckOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            SqlAuthorizationStore store = scope.ServiceProvider.GetRequiredService<SqlAuthorizationStore>();
            long revision = await store.GetRevisionAsync(cancellationToken);
            cache.ObserveRevision(revision);
            if (_pollingFailed)
            {
                logger.LogInformation("Authorization revision polling recovered at revision {Revision}", revision);
                _pollingFailed = false;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (!_pollingFailed)
            {
                logger.LogWarning(exception, "Authorization revision polling failed; cached entries still expire after {Lifetime}",
                    options.Value.EntryLifetime);
                _pollingFailed = true;
            }
        }
    }
}
