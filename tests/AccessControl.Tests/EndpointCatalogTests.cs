using AccessControl.Client;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

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
}
