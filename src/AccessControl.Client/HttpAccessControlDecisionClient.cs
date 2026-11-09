using System.Diagnostics;
using System.Net.Http.Json;
using AccessControl.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AccessControl.Client;

public sealed class HttpAccessControlDecisionClient(IHttpClientFactory clientFactory, IOptions<AccessControlClientOptions> options,
    ILogger<HttpAccessControlDecisionClient> logger) : IAccessControlDecisionClient
{
    public async Task<DecisionResponse> DecideAsync(DecisionRequest request, CancellationToken cancellationToken)
    {
        using HttpClient client = clientFactory.CreateClient(nameof(HttpAccessControlDecisionClient));
        using HttpRequestMessage message = new(HttpMethod.Post, new Uri(new Uri(options.Value.ServiceUrl), "v1/decisions"))
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("X-Access-Control-Key", options.Value.ApiKey);
        Stopwatch timer = Stopwatch.StartNew();
        logger.LogDebug("Sending decision request for {Module} {Method} {Route}", request.Module, request.HttpMethod, request.RouteTemplate);
        using HttpResponseMessage response = await client.SendAsync(message, cancellationToken);
        logger.LogDebug("Decision service responded with {StatusCode} in {ElapsedMs} ms for {Module} {Method} {Route}",
            (int)response.StatusCode, timer.ElapsedMilliseconds, request.Module, request.HttpMethod, request.RouteTemplate);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Decision service HTTP failure {StatusCode} for {Module} {Method} {Route}",
                (int)response.StatusCode, request.Module, request.HttpMethod, request.RouteTemplate);
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DecisionResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Access Control returned an empty decision.");
    }
}
