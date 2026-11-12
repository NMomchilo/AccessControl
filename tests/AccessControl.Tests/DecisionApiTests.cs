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
    private static readonly Guid Advisor = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Organization = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    [Fact]
    public async Task PostDecision_ValidDecisionKey_ReturnsPermitAsync()
    {
        await using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage message = new(HttpMethod.Post, "/v1/decisions")
        {
            Content = JsonContent.Create(new DecisionRequest("alice", [Advisor], "customers", "GET", "/customers/{id}", "tenant-a", Organization))
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
            Content = JsonContent.Create(new DecisionRequest("alice", [Advisor], "customers", "GET", "/customers/{id}", "tenant-a", Organization))
        };
        message.Headers.Add("X-Access-Control-Key", "admin-test-key");

        using HttpResponseMessage response = await client.SendAsync(message);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostDecision_WithoutOrganizationId_ReturnsBadRequestAsync()
    {
        await using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage message = new(HttpMethod.Post, "/v1/decisions")
        {
            Content = JsonContent.Create(new
            {
                subjectId = "alice", roleIds = new[] { Advisor }, module = "customers",
                httpMethod = "GET", routeTemplate = "/customers/{id}", tenantId = "tenant-a"
            })
        };
        message.Headers.Add("X-Access-Control-Key", "decision-test-key");

        using HttpResponseMessage response = await client.SendAsync(message);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutPublicPolicy_WithoutOrganizationId_ReturnsBadRequestAsync()
    {
        await using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage message = new(HttpMethod.Put, "/v1/policies")
        {
            Content = JsonContent.Create(new
            {
                tenantId = "tenant-a", module = "customers", httpMethod = "GET",
                routeTemplate = "/customers/{id}", isPublic = true,
                requiredEntitlements = Array.Empty<string>()
            })
        };
        message.Headers.Add("X-Access-Control-Key", "admin-test-key");

        using HttpResponseMessage response = await client.SendAsync(message);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetPolicy_AdminKey_ReturnsEndpointPolicyAsync()
    {
        await using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage message = new(HttpMethod.Get,
            $"/v1/policies?tenantId=tenant-a&unitOrganizationId={Organization:D}&module=customers&httpMethod=GET&routeTemplate=%2Fcustomers%2F%7Bid%7D");
        message.Headers.Add("X-Access-Control-Key", "admin-test-key");

        using HttpResponseMessage response = await client.SendAsync(message);
        PolicyRequest? policy = await response.Content.ReadFromJsonAsync<PolicyRequest>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/customers/{id}", policy?.RouteTemplate);
        Assert.Contains("customers.view", policy?.RequiredEntitlements ?? []);
    }

    [Fact]
    public async Task GetPolicy_WithoutOrganizationId_ReturnsBadRequestAsync()
    {
        await using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage message = new(HttpMethod.Get,
            "/v1/policies?tenantId=tenant-a&module=customers&httpMethod=GET&routeTemplate=%2Fcustomers%2F%7Bid%7D");
        message.Headers.Add("X-Access-Control-Key", "admin-test-key");

        using HttpResponseMessage response = await client.SendAsync(message);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AdminPolicyAndGrantMutations_RequireValidScopeAsync()
    {
        await using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage policySaved = await SendAdminAsync(client, HttpMethod.Put, "/v1/policies",
            new PolicyRequest("tenant-a", "customers", "GET", "/status", true, [], Guid.Empty));
        using HttpResponseMessage protectedWithoutOrganization = await SendAdminAsync(client, HttpMethod.Put, "/v1/policies",
            new PolicyRequest("tenant-a", "customers", "GET", "/customers/{id}", false,
                ["customers.view"], Guid.Empty));
        using HttpResponseMessage policyDeleted = await SendAdminAsync(client, HttpMethod.Delete, "/v1/policies",
            new PolicyKeyRequest("tenant-a", "customers", "GET", "/status", Guid.Empty));
        using HttpResponseMessage grantSaved = await SendAdminAsync(client, HttpMethod.Put, "/v1/roles/entitlements",
            new RoleEntitlementsRequest("tenant-a", Advisor, ["customers.view"], Organization));
        using HttpResponseMessage grantWithoutOrganization = await SendAdminAsync(client, HttpMethod.Put,
            "/v1/roles/entitlements", new RoleEntitlementsRequest("tenant-a", Advisor, ["customers.view"], Guid.Empty));

        Assert.Equal(HttpStatusCode.NoContent, policySaved.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, protectedWithoutOrganization.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, policyDeleted.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, grantSaved.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, grantWithoutOrganization.StatusCode);
    }

    [Fact]
    public async Task AdminGrantRead_UsesOrganizationQueryAsync()
    {
        await using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage message = new(HttpMethod.Get,
            $"/v1/roles/entitlements?tenantId=tenant-a&unitOrganizationId={Organization:D}&roleId={Advisor:D}");
        message.Headers.Add("X-Access-Control-Key", "admin-test-key");

        using HttpResponseMessage response = await client.SendAsync(message);
        RoleEntitlementsRequest? grants = await response.Content.ReadFromJsonAsync<RoleEntitlementsRequest>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Organization, grants?.UnitOrganizationId);
        Assert.Contains("customers.view", grants?.Entitlements ?? []);
    }

    [Fact]
    public async Task EntitlementLists_RequireAdminKeyAndReturnScopedPagesAsync()
    {
        await using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage missingKey = await client.GetAsync("/v1/entitlements?tenantId=tenant-a");
        using HttpRequestMessage decisionKeyRequest = new(HttpMethod.Get, "/v1/entitlements?tenantId=tenant-a");
        decisionKeyRequest.Headers.Add("X-Access-Control-Key", "decision-test-key");
        using HttpResponseMessage decisionKey = await client.SendAsync(decisionKeyRequest);
        using HttpRequestMessage allRequest = new(HttpMethod.Get, "/v1/entitlements?tenantId=tenant-a&page=1&pageSize=1");
        allRequest.Headers.Add("X-Access-Control-Key", "admin-test-key");
        using HttpResponseMessage all = await client.SendAsync(allRequest);
        EntitlementPageResponse? allPage = await all.Content.ReadFromJsonAsync<EntitlementPageResponse>();
        using HttpRequestMessage scopedRequest = new(HttpMethod.Get,
            $"/v1/unit-organizations/{Organization:D}/entitlements?tenantId=tenant-a");
        scopedRequest.Headers.Add("X-Access-Control-Key", "admin-test-key");
        using HttpResponseMessage scoped = await client.SendAsync(scopedRequest);
        EntitlementPageResponse? scopedPage = await scoped.Content.ReadFromJsonAsync<EntitlementPageResponse>();

        Assert.Equal(HttpStatusCode.Unauthorized, missingKey.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, decisionKey.StatusCode);
        Assert.Equal(HttpStatusCode.OK, all.StatusCode);
        Assert.Equal(["customers.view"], allPage?.Items);
        Assert.Equal(2, allPage?.TotalCount);
        Assert.Equal(1, allPage?.PageSize);
        Assert.Equal(HttpStatusCode.OK, scoped.StatusCode);
        Assert.Equal(["customers.view"], scopedPage?.Items);
        Assert.Equal(1, scopedPage?.TotalCount);
    }

    [Theory]
    [InlineData("/v1/entitlements?tenantId=tenant-a&page=0")]
    [InlineData("/v1/entitlements?tenantId=tenant-a&pageSize=101")]
    [InlineData("/v1/entitlements?tenantId=tenant-a&page=2147483647&pageSize=100")]
    [InlineData("/v1/unit-organizations/00000000-0000-0000-0000-000000000000/entitlements?tenantId=tenant-a")]
    public async Task EntitlementLists_InvalidQuery_ReturnsBadRequestAsync(string path)
    {
        await using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage message = new(HttpMethod.Get, path);
        message.Headers.Add("X-Access-Control-Key", "admin-test-key");

        using HttpResponseMessage response = await client.SendAsync(message);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendAdminAsync<T>(HttpClient client, HttpMethod method, string path, T body)
    {
        using HttpRequestMessage message = new(method, path) { Content = JsonContent.Create(body) };
        message.Headers.Add("X-Access-Control-Key", "admin-test-key");
        return await client.SendAsync(message);
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
                services.AddScoped<IEntitlementCatalog, FakeEntitlementCatalog>();
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
        public Task<EndpointPolicy?> GetPolicyAsync(string tenantId, Guid unitOrganizationId, string module, string httpMethod, string routeTemplate, CancellationToken cancellationToken) =>
            Task.FromResult<EndpointPolicy?>(tenantId == "tenant-a" && unitOrganizationId == Organization && module == "customers" && httpMethod == "GET" && routeTemplate == "/customers/{id}"
                ? new EndpointPolicy(tenantId, module, httpMethod, routeTemplate, false, ["customers.view"], Organization) : null);

        public Task<IReadOnlySet<string>> GetEntitlementsAsync(string tenantId, Guid unitOrganizationId, IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(unitOrganizationId == Organization && roleIds.Contains(Advisor) ? new HashSet<string> { "customers.view" } : new HashSet<string>());

        public Task UpsertPolicyAsync(EndpointPolicy policy, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeletePolicyAsync(string tenantId, Guid unitOrganizationId, string module, string httpMethod, string routeTemplate, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ReplaceRoleEntitlementsAsync(string tenantId, Guid unitOrganizationId, Guid roleId, IReadOnlyList<string> entitlements, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeEntitlementCatalog : IEntitlementCatalog
    {
        public Task<EntitlementPageResponse> ListAsync(string tenantId, Guid? unitOrganizationId, int page,
            int pageSize, CancellationToken cancellationToken)
        {
            string[] values = unitOrganizationId.HasValue ? ["customers.view"] : ["customers.view", "orders.view"];
            return Task.FromResult(new EntitlementPageResponse(values.Skip((page - 1) * pageSize).Take(pageSize).ToArray(),
                page, pageSize, values.Length));
        }
    }
}
