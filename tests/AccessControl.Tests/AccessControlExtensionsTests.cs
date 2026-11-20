using AccessControl.Client;
using AccessControl.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AccessControl.Tests;

public sealed class AccessControlExtensionsTests
{
    [Fact]
    public void AddAccessControl_BindsConfiguredClientAndRejectsInsecureEndpoint()
    {
        IConfiguration valid = Configuration("https://access.example/");
        using ServiceProvider provider = new ServiceCollection().AddAccessControl(valid).BuildServiceProvider();

        AccessControlClientOptions options = provider.GetRequiredService<IOptions<AccessControlClientOptions>>().Value;
        Assert.Equal("customers", options.Module);
        Assert.Equal("decision-key", options.ApiKey);
        Assert.Equal("UserManagement", options.RoleIdentityAuthenticationType);
        Assert.IsType<HttpAccessControlDecisionClient>(provider.GetRequiredService<IAccessControlDecisionClient>());

        using ServiceProvider insecure = new ServiceCollection().AddAccessControl(Configuration("http://access.example/"))
            .BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => insecure.GetRequiredService<IOptions<AccessControlClientOptions>>().Value);
    }

    [Fact]
    public async Task UseAccessControl_WiresMiddlewareBeforeEndpointAsync()
    {
        CapturingDecisionClient decisionClient = new();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IAccessControlDecisionClient>(decisionClient);
        services.AddSingleton<IOptions<AccessControlClientOptions>>(Options.Create(new AccessControlClientOptions
        {
            Module = "customers"
        }));
        using ServiceProvider provider = services.BuildServiceProvider();
        ApplicationBuilder app = new(provider);
        bool endpointRan = false;
        app.UseAccessControl();
        app.Run(_ => { endpointRan = true; return Task.CompletedTask; });
        RequestDelegate pipeline = app.Build();
        DefaultHttpContext context = new();
        context.RequestServices = provider;
        context.Request.Method = "GET";
        context.SetEndpoint(new RouteEndpointBuilder(_ => Task.CompletedTask,
            RoutePatternFactory.Parse("/status"), 0).Build());

        await pipeline(context);

        Assert.True(endpointRan);
        Assert.Equal(Guid.Empty, decisionClient.Request?.UnitOrganizationId);
        Assert.Equal("/status", decisionClient.Request?.RouteTemplate);
    }

    [Fact]
    public async Task RegisterAccessControlCatalog_UsesConfiguredServicesAndExistingKeysAsync()
    {
        List<string> calls = [];
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["UserManagement:ServiceUrl"] = "http://localhost:5220/",
            ["UserManagement:ApiKey"] = new string('u', 32),
            ["UserManagement:AllowInsecureHttpForDevelopment"] = "true"
        }).Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddSingleton<IOptions<AccessControlClientOptions>>(Options.Create(new AccessControlClientOptions
        {
            ServiceUrl = "https://access.example/", ApiKey = "access-key", Module = "customers"
        }));
        services.AddSingleton<IHttpClientFactory>(new RecordingHttpClientFactory(calls));
        using ServiceProvider provider = services.BuildServiceProvider();
        RouteEndpointBuilder route = new(_ => Task.CompletedTask, RoutePatternFactory.Parse("/customers/{id}"), 0);
        route.Metadata.Add(new HttpMethodMetadata(["GET"]));
        TestRouteBuilder routes = new(provider, [route.Build()]);

        await routes.RegisterAccessControlCatalogAsync(new AccessControlCatalogRegistrationOptions
        {
            TenantId = "default", UnitOrganizationId = Guid.NewGuid(),
            UnitOrganizationName = "Customers", ModuleName = "Customers"
        });

        Assert.Equal(["http://localhost:5220/v1/registrations/unit-organizations",
            "https://access.example/v1/registrations/catalog"], calls);
    }

    [Fact]
    public async Task RegisterAccessControlCatalog_RejectsMissingUserManagementConfigurationAsync()
    {
        ServiceCollection services = new();
        services.AddSingleton<IOptions<AccessControlClientOptions>>(Options.Create(new AccessControlClientOptions()));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        using ServiceProvider provider = services.BuildServiceProvider();
        TestRouteBuilder routes = new(provider, []);
        await Assert.ThrowsAsync<ArgumentException>(() => routes.RegisterAccessControlCatalogAsync(
            new AccessControlCatalogRegistrationOptions()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => routes.RegisterAccessControlCatalogAsync(
            new AccessControlCatalogRegistrationOptions
            {
                TenantId = "default", UnitOrganizationId = Guid.NewGuid(),
                UnitOrganizationName = "Customers", ModuleName = "Customers"
            }));
    }

    private sealed class TestRouteBuilder(IServiceProvider serviceProvider, IEnumerable<Endpoint> endpoints)
        : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider => serviceProvider;
        public ICollection<EndpointDataSource> DataSources { get; } =
            [new DefaultEndpointDataSource(endpoints)];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(serviceProvider);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "AccessControl.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class RecordingHttpClientFactory(List<string> calls) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new RecordingHandler(calls));
    }

    private sealed class RecordingHandler(List<string> calls) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            calls.Add(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    private sealed class CapturingDecisionClient : IAccessControlDecisionClient
    {
        public DecisionRequest? Request { get; private set; }
        public Task<DecisionResponse> DecideAsync(DecisionRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new DecisionResponse(true, "public"));
        }
    }

    private static IConfiguration Configuration(string url) => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["AccessControl:ServiceUrl"] = url,
            ["AccessControl:ApiKey"] = "decision-key",
            ["AccessControl:Module"] = "customers"
        }).Build();
}
