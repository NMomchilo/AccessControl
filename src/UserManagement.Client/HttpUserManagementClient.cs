using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UserManagement.Contracts;

namespace UserManagement.Client;

public sealed class HttpUserManagementClient(IHttpClientFactory clientFactory,
    IOptions<UserManagementClientOptions> options, ILogger<HttpUserManagementClient> logger) : IUserManagementClient
{
    public async Task<UserIdentityResponse> ResolveAsync(UserIdentityRequest request,
        CancellationToken cancellationToken)
    {
        using HttpClient client = clientFactory.CreateClient(nameof(HttpUserManagementClient));
        using HttpRequestMessage message = new(HttpMethod.Post,
            new Uri(new Uri(options.Value.ServiceUrl), "v1/users/resolve"))
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("X-User-Management-Key", options.Value.ApiKey);
        Stopwatch timer = Stopwatch.StartNew();
        using HttpResponseMessage response = await client.SendAsync(message, cancellationToken);
        logger.LogDebug("User resolution HTTP {StatusCode} in {ElapsedMs} ms",
            (int)response.StatusCode, timer.ElapsedMilliseconds);
        response.EnsureSuccessStatusCode();
        UserIdentityResponse resolved = await response.Content.ReadFromJsonAsync<UserIdentityResponse>(cancellationToken)
            ?? throw new InvalidOperationException("User Management returned an empty response.");
        if (resolved.UserId == Guid.Empty || resolved.Roles is null ||
            resolved.Roles.Any(role => string.IsNullOrWhiteSpace(role) || role.Length > 64))
        {
            throw new InvalidOperationException("User Management returned an invalid response.");
        }

        return resolved;
    }

    public async Task<long> GetRevisionAsync(CancellationToken cancellationToken)
    {
        using HttpClient client = clientFactory.CreateClient(nameof(HttpUserManagementClient));
        using HttpRequestMessage message = new(HttpMethod.Get,
            new Uri(new Uri(options.Value.ServiceUrl), "v1/users/revision"));
        message.Headers.Add("X-User-Management-Key", options.Value.ApiKey);
        using HttpResponseMessage response = await client.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<long>(cancellationToken);
    }
}
