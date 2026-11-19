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
    public void Build_RejectsRoutesWithoutHttpMethodMetadata()
    {
        RouteEndpointBuilder first = new(_ => Task.CompletedTask, RoutePatternFactory.Parse("/Customers/{Id}"), 0);
        first.Metadata.Add(new HttpMethodMetadata(["GET"]));
        RouteEndpointBuilder duplicate = new(_ => Task.CompletedTask, RoutePatternFactory.Parse("/customers/{id}"), 0);
        duplicate.Metadata.Add(new HttpMethodMetadata(["GET"]));
        RouteEndpointBuilder methodless = new(_ => Task.CompletedTask, RoutePatternFactory.Parse("/internal"), 0);

        Assert.Throws<InvalidOperationException>(() => EndpointCatalog.Build(
            [first.Build(), duplicate.Build(), methodless.Build()], "customers"));
        RouteEndpointBuilder emptyMethods = new(_ => Task.CompletedTask, RoutePatternFactory.Parse("/empty"), 0);
        emptyMethods.Metadata.Add(new HttpMethodMetadata([]));
        Assert.Throws<InvalidOperationException>(() => EndpointCatalog.Build([emptyMethods.Build()], "customers"));
    }

    [Fact]
    public async Task Register_UsesExistingClientKeysAndRegistersOrganizationFirstAsync()
    {
        List<string> calls = [];
        using HttpClient users = new(new RecordingHandler(request =>
        {
            calls.Add($"users:{request.Headers.GetValues("X-User-Management-Key").Single()}");
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        })) { BaseAddress = new Uri("https://users.example/") };
        using HttpClient access = new(new RecordingHandler(request =>
        {
            calls.Add($"access:{request.Headers.GetValues("X-Access-Control-Key").Single()}");
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        })) { BaseAddress = new Uri("https://access.example/") };
        RouteEndpointBuilder route = new(_ => Task.CompletedTask, RoutePatternFactory.Parse("/customers/{id}"), 0);
        route.Metadata.Add(new HttpMethodMetadata(["GET"]));
        await EndpointCatalog.RegisterAsync([route.Build()], new AccessControlClientOptions
        {
            Module = "customers", ApiKey = "existing-access-key"
        }, new AccessControlCatalogRegistrationOptions
        {
            TenantId = "default", UnitOrganizationId = Guid.NewGuid(),
            UnitOrganizationName = "Customers", ModuleName = "Customers"
        }, "existing-user-key", users, access);

        Assert.Equal(["users:existing-user-key", "access:existing-access-key"], calls);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
