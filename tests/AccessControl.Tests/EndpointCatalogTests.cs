using AccessControl.Client;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace AccessControl.Tests;

public sealed class EndpointCatalogTests
{
    [Fact]
    public void Build_ExportsMethodsAndCanonicalRouteTemplates()
    {
        RouteEndpointBuilder builder = new(_ => Task.CompletedTask,
            RoutePatternFactory.Parse("/Customers/{Id}"), 0);
        builder.Metadata.Add(new HttpMethodMetadata(["GET", "POST"]));

        RouteEndpointBuilder health = new(_ => Task.CompletedTask, RoutePatternFactory.Parse("/health"), 0);
        health.Metadata.Add(new HttpMethodMetadata(["GET"]));
        IReadOnlyList<EndpointCatalogEntry> entries = EndpointCatalog.Build([builder.Build(), health.Build()],
            "customers", ["/health"]);

        Assert.Equal(2, entries.Count);
        Assert.Contains(new EndpointCatalogEntry("customers", "GET", "/customers/{id}"), entries);
        Assert.Contains(new EndpointCatalogEntry("customers", "POST", "/customers/{id}"), entries);
    }

    [Fact]
    public void Build_DeduplicatesRoutesAndOmitsNonHttpEndpoints()
    {
        RouteEndpointBuilder first = new(_ => Task.CompletedTask, RoutePatternFactory.Parse("/Customers/{Id}"), 0);
        first.Metadata.Add(new HttpMethodMetadata(["GET"]));
        RouteEndpointBuilder duplicate = new(_ => Task.CompletedTask, RoutePatternFactory.Parse("/customers/{id}"), 0);
        duplicate.Metadata.Add(new HttpMethodMetadata(["GET"]));
        RouteEndpointBuilder methodless = new(_ => Task.CompletedTask, RoutePatternFactory.Parse("/internal"), 0);

        IReadOnlyList<EndpointCatalogEntry> result = EndpointCatalog.Build(
            [first.Build(), duplicate.Build(), methodless.Build()], "customers");

        Assert.Equal([new EndpointCatalogEntry("customers", "GET", "/customers/{id}")], result);
    }

    [Fact]
    public void Export_WritesMatchingEndpointCatalogAsync()
    {
        string output = Path.Combine(Path.GetTempPath(), $"access-control-catalog-{Guid.NewGuid():N}.json");
        try
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.Services.AddSingleton<IOptions<AccessControlClientOptions>>(Options.Create(new AccessControlClientOptions
            {
                Module = "customers", CatalogOutputPath = output, ExcludedPaths = ["/health"]
            }));
            using WebApplication app = builder.Build();
            app.MapGet("/customers/{id}", () => "ok");
            app.MapGet("/health", () => "ok");

            Assert.Same(app, app.ExportAccessControlCatalog());

            Assert.True(File.Exists(output));
            string json = File.ReadAllText(output);
            Assert.Contains("/customers/{id}", json);
            Assert.DoesNotContain("/health", json);
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }
}
