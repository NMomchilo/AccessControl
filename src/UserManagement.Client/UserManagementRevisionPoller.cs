using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace UserManagement.Client;

public sealed class UserManagementRevisionPoller(HttpUserManagementClient client, UserResolutionCache cache,
    IOptions<UserManagementClientOptions> options, ILogger<UserManagementRevisionPoller> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(options.Value.RevisionPollInterval);
        do
        {
            try
            {
                long revision = await client.GetRevisionAsync(stoppingToken);
                cache.ObserveRevision(revision);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or
                System.Text.Json.JsonException or TaskCanceledException)
            {
                logger.LogWarning(exception, "User role revision poll failed; cached entries will expire by lifetime");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
