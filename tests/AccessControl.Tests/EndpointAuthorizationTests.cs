using AccessControl.Application;
using AccessControl.Contracts;
using AccessControl.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessControl.Tests;

public sealed class EndpointAuthorizationTests
{
    [Fact]
    public async Task DecideAsync_RoleHasEndpointEntitlement_PermitsAsync()
    {
        FakeEndpointStore store = new(new EndpointPolicy("tenant-a", "customers", "GET", "/customers/{id}", false, ["customers.view"]));
        store.Entitlements.Add("customers.view");
        AuthorizationService sut = new(store, NullLogger<AuthorizationService>.Instance);

        DecisionResponse result = await sut.DecideAsync(
            new DecisionRequest("alice", ["advisor"], "customers", "GET", "/customers/{id}", "tenant-a"),
            CancellationToken.None);

        Assert.True(result.Permit);
        Assert.False(result.AuthenticationRequired);
    }

    [Fact]
    public async Task DecideAsync_AnonymousProtectedEndpoint_RequiresAuthenticationAsync()
    {
        FakeEndpointStore store = new(new EndpointPolicy("tenant-a", "customers", "GET", "/customers/{id}", false, ["customers.view"]));
        AuthorizationService sut = new(store, NullLogger<AuthorizationService>.Instance);

        DecisionResponse result = await sut.DecideAsync(
            new DecisionRequest(null, [], "customers", "GET", "/customers/{id}", "tenant-a"),
            CancellationToken.None);

        Assert.False(result.Permit);
        Assert.True(result.AuthenticationRequired);
    }

    [Fact]
    public async Task DecideAsync_PublicEndpoint_PermitsAnonymousAsync()
    {
        FakeEndpointStore store = new(new EndpointPolicy("tenant-a", "customers", "GET", "/status", true, []));
        AuthorizationService sut = new(store, NullLogger<AuthorizationService>.Instance);

        DecisionResponse result = await sut.DecideAsync(
            new DecisionRequest(null, [], "customers", "GET", "/status", "tenant-a"),
            CancellationToken.None);

        Assert.True(result.Permit);
    }

    [Fact]
    public async Task DecideAsync_MissingEndpointPolicy_DeniesAsync()
    {
        AuthorizationService sut = new(new FakeEndpointStore(null), NullLogger<AuthorizationService>.Instance);

        DecisionResponse result = await sut.DecideAsync(
            new DecisionRequest("alice", ["advisor"], "customers", "GET", "/new-route", "tenant-a"),
            CancellationToken.None);

        Assert.False(result.Permit);
    }

    private sealed class FakeEndpointStore(EndpointPolicy? policy) : IAuthorizationStore
    {
        public HashSet<string> Entitlements { get; } = [];

        public Task<EndpointPolicy?> GetPolicyAsync(string tenantId, string module, string httpMethod, string routeTemplate, CancellationToken cancellationToken) =>
            Task.FromResult(policy is not null && policy.TenantId == tenantId && policy.Module == module &&
                policy.HttpMethod == httpMethod && policy.RouteTemplate == routeTemplate ? policy : null);

        public Task<IReadOnlySet<string>> GetEntitlementsAsync(string tenantId, IReadOnlyList<string> roles, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(Entitlements);

        public Task UpsertPolicyAsync(EndpointPolicy endpointPolicy, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeletePolicyAsync(string tenantId, string module, string httpMethod, string routeTemplate, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ReplaceRoleEntitlementsAsync(string tenantId, string role, IReadOnlyList<string> entitlements, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
