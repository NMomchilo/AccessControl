using System.Net;
using System.Net.Http.Json;
using AccessControl.Application;
using AccessControl.Contracts;
using AccessControl.Domain;
using AccessControl.Infrastructure.Caching;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AccessControl.Tests;

public sealed class DecisionApiTests
{
    [Fact]
    public async Task PostDecision_ValidDecisionKey_ReturnsPermitAsync()
    {
        await using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage message = new(HttpMethod.Post, "/v1/decisions")
        {
            Content = JsonContent.Create(new DecisionRequest("alice", ["advisor"], "customers", "GET", "/customers/{id}", "tenant-a"))
        };
        message.Headers.Add("X-Access-Control-Key", "decision-test-key");

        using HttpResponseMessage response = await client.SendAsync(message);
        DecisionResponse? result = await response.Content.ReadFromJsonAsync<DecisionResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(result?.Permit);
    }

    [Fact]
    public async Task PostDecision_AdminKey_IsRejectedAsync()
    {
        await using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage message = new(HttpMethod.Post, "/v1/decisions")
        {
            Content = JsonContent.Create(new DecisionRequest("alice", ["advisor"], "customers", "GET", "/customers/{id}", "tenant-a"))
        };
        message.Headers.Add("X-Access-Control-Key", "admin-test-key");

        using HttpResponseMessage response = await client.SendAsync(message);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetPolicy_AdminKey_ReturnsEndpointPolicyAsync()
    {
        await using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage message = new(HttpMethod.Get,
            "/v1/policies?tenantId=tenant-a&module=customers&httpMethod=GET&routeTemplate=%2Fcustomers%2F%7Bid%7D");
        message.Headers.Add("X-Access-Control-Key", "admin-test-key");

        using HttpResponseMessage response = await client.SendAsync(message);
        PolicyRequest? policy = await response.Content.ReadFromJsonAsync<PolicyRequest>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/customers/{id}", policy?.RouteTemplate);
        Assert.Contains("customers.view", policy?.RequiredEntitlements ?? []);
    }

    private sealed class ApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("AccessControlDatabase:ApplyMigrationsOnStartup", "false");
            builder.UseSetting("ConnectionStrings:AccessControl", "Server=localhost;Database=unused;Trusted_Connection=True");
            builder.UseSetting("ApiKeys:DecisionKey", "decision-test-key");
            builder.UseSetting("ApiKeys:AdminKey", "admin-test-key");
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<IAuthorizationStore, FakeStore>();
                foreach (ServiceDescriptor descriptor in services.Where(descriptor =>
                    descriptor.ServiceType == typeof(IHostedService) &&
                    descriptor.ImplementationType == typeof(AuthorizationCacheInvalidationService)).ToArray())
                {
                    services.Remove(descriptor);
                }
            });
        }
    }

    private sealed class FakeStore : IAuthorizationStore
    {
        public Task<EndpointPolicy?> GetPolicyAsync(string tenantId, string module, string httpMethod, string routeTemplate, CancellationToken cancellationToken) =>
            Task.FromResult<EndpointPolicy?>(tenantId == "tenant-a" && module == "customers" && httpMethod == "GET" && routeTemplate == "/customers/{id}"
                ? new EndpointPolicy(tenantId, module, httpMethod, routeTemplate, false, ["customers.view"]) : null);

        public Task<IReadOnlySet<string>> GetEntitlementsAsync(string tenantId, IReadOnlyList<string> roles, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(roles.Contains("advisor") ? new HashSet<string> { "customers.view" } : new HashSet<string>());

        public Task UpsertPolicyAsync(EndpointPolicy policy, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeletePolicyAsync(string tenantId, string module, string httpMethod, string routeTemplate, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ReplaceRoleEntitlementsAsync(string tenantId, string role, IReadOnlyList<string> entitlements, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
