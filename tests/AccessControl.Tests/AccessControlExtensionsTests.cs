using AccessControl.Client;
using AccessControl.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
