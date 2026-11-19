using System.Net.Http.Json;
using AccessControl.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;

namespace AccessControl.Client;

public sealed record EndpointCatalogEntry(string Module, string HttpMethod, string RouteTemplate);

public static class EndpointCatalog
{
    public static IReadOnlyList<EndpointCatalogEntry> Build(IEnumerable<Endpoint> endpoints, string module,
        IEnumerable<string>? excludedPaths = null)
    {
        HashSet<string> exclusions = (excludedPaths ?? []).Select(EndpointKey.NormalizeRoute)
            .ToHashSet(StringComparer.Ordinal);
        return endpoints.OfType<RouteEndpoint>()
            .SelectMany(endpoint => GetMethods(endpoint)
                .Select(method => new EndpointCatalogEntry(module, EndpointKey.NormalizeMethod(method),
                    EndpointKey.NormalizeRoute(endpoint.RoutePattern.RawText ??
                        throw new InvalidOperationException("Route has no template.")))))
            .Where(entry => !exclusions.Contains(entry.RouteTemplate))
            .Distinct()
            .OrderBy(entry => entry.RouteTemplate, StringComparer.Ordinal)
            .ThenBy(entry => entry.HttpMethod, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<string> GetMethods(RouteEndpoint endpoint)
    {
        IReadOnlyList<string>? methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
        return methods is { Count: > 0 } ? methods :
            throw new InvalidOperationException($"Route {endpoint.RoutePattern.RawText} has no HTTP-method metadata.");
    }

    public static async Task RegisterAsync(IEnumerable<Endpoint> endpoints, AccessControlClientOptions options,
        AccessControlCatalogRegistrationOptions registration, string userManagementKey,
        HttpClient userManagementClient, HttpClient accessControlClient,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<EndpointCatalogEntry> entries = Build(endpoints, options.Module, options.ExcludedPaths);
        if (entries.Count > 1000) throw new InvalidOperationException("Endpoint catalog exceeds 1000 routes.");

        using HttpRequestMessage organizationRequest = new(HttpMethod.Post, "v1/registrations/unit-organizations")
        {
            Content = JsonContent.Create(new
            {
                Id = registration.UnitOrganizationId, registration.TenantId,
                Name = registration.UnitOrganizationName
            })
        };
        organizationRequest.Headers.Add("X-User-Management-Key", userManagementKey);
        using HttpResponseMessage organizationResponse = await userManagementClient.SendAsync(organizationRequest,
            cancellationToken);
        if (!organizationResponse.IsSuccessStatusCode)
            throw new HttpRequestException($"Unit organization registration failed with HTTP {(int)organizationResponse.StatusCode}.");

        using HttpRequestMessage catalogRequest = new(HttpMethod.Post, "v1/registrations/catalog")
        {
            Content = JsonContent.Create(new CatalogRegistrationRequest(registration.TenantId,
                registration.UnitOrganizationId, options.Module, registration.ModuleName,
                entries.Select(entry => new CatalogEndpoint(entry.HttpMethod, entry.RouteTemplate)).ToArray()))
        };
        catalogRequest.Headers.Add("X-Access-Control-Key", options.ApiKey);
        using HttpResponseMessage catalogResponse = await accessControlClient.SendAsync(catalogRequest,
            cancellationToken);
        if (!catalogResponse.IsSuccessStatusCode)
            throw new HttpRequestException($"Access Control catalog registration failed with HTTP {(int)catalogResponse.StatusCode}.");
    }
}
