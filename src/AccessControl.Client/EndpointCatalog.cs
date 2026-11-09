using System.Text.Json;
using AccessControl.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
            .Where(endpoint => !string.IsNullOrWhiteSpace(endpoint.RoutePattern.RawText))
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(method => new EndpointCatalogEntry(module, EndpointKey.NormalizeMethod(method),
                    EndpointKey.NormalizeRoute(endpoint.RoutePattern.RawText!))))
            .Where(entry => !exclusions.Contains(entry.RouteTemplate))
            .Distinct()
            .OrderBy(entry => entry.RouteTemplate, StringComparer.Ordinal)
            .ThenBy(entry => entry.HttpMethod, StringComparer.Ordinal)
            .ToList();
    }

    public static void Export(this IEndpointRouteBuilder app)
    {
        AccessControlClientOptions options = app.ServiceProvider.GetRequiredService<IOptions<AccessControlClientOptions>>().Value;
        if (string.IsNullOrWhiteSpace(options.CatalogOutputPath))
        {
            return;
        }

        ILogger logger = app.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AccessControl.EndpointCatalog");
        IReadOnlyList<EndpointCatalogEntry> entries = Build(app.DataSources.SelectMany(source => source.Endpoints),
            options.Module, options.ExcludedPaths);
        string path = Path.GetFullPath(options.CatalogOutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
        logger.LogInformation("Exported {EndpointCount} endpoint catalog entries for module {Module} to {CatalogPath}",
            entries.Count, options.Module, path);
    }
}
