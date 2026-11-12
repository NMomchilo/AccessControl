using AccessControl.Application;
using AccessControl.Contracts;
using AccessControl.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessControl.Tests;

public sealed class EndpointAuthorizationTests
{
    private static readonly Guid Advisor = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OrganizationA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OrganizationB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PolicyAdministration_NormalizesKeyAndEntitlementsAsync(bool isPublic)
    {
        FakeEndpointStore store = new();
        AuthorizationService service = new(store, NullLogger<AuthorizationService>.Instance);
        PolicyRequest request = new(" TENANT-A ", " Customers ", "get", "/Customers/{ID}/", isPublic,
            isPublic ? [] : [" Customers.View ", "customers.view"], OrganizationA);

        await service.UpsertPolicyAsync(request, CancellationToken.None);
        PolicyRequest? saved = await service.GetPolicyAsync("tenant-a", OrganizationA, "customers", "GET",
            "/customers/{id}", CancellationToken.None);
        await service.DeletePolicyAsync(new PolicyKeyRequest("TENANT-A", "customers", "get", "/customers/{id}", OrganizationA),
            CancellationToken.None);

        Assert.NotNull(saved);
        Assert.Equal(isPublic, saved.IsPublic);
        Assert.Equal(isPublic ? 0 : 1, saved.RequiredEntitlements.Count);
        Assert.Empty(store.Policies);
    }

    [Fact]
    public async Task RoleGrantAdministration_UsesOrganizationAndNormalizesEntitlementsAsync()
    {
        FakeEndpointStore store = new();
        AuthorizationService service = new(store, NullLogger<AuthorizationService>.Instance);

        await service.ReplaceRoleEntitlementsAsync(new RoleEntitlementsRequest(" TENANT-A ", Advisor,
            [" Customers.View ", "customers.view"], OrganizationA), CancellationToken.None);
        RoleEntitlementsRequest result = await service.GetRoleEntitlementsAsync("tenant-a", OrganizationA, Advisor,
            CancellationToken.None);

        Assert.Equal(OrganizationA, result.UnitOrganizationId);
        Assert.Equal(["customers.view"], result.Entitlements);
        Assert.Empty(await store.GetEntitlementsAsync("tenant-a", OrganizationB, [Advisor], CancellationToken.None));
    }

    [Fact]
    public async Task InvalidPolicyAndGrantAdministration_DoesNotWriteAsync()
    {
        FakeEndpointStore store = new();
        AuthorizationService service = new(store, NullLogger<AuthorizationService>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpsertPolicyAsync(
            new PolicyRequest("tenant-a", "customers", "GET", "/status", false, ["read"], Guid.Empty), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpsertPolicyAsync(
            new PolicyRequest("tenant-a", "customers", "GET", "/status", true, ["read"], OrganizationA), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ReplaceRoleEntitlementsAsync(
            new RoleEntitlementsRequest("tenant-a", Advisor, ["read"], Guid.Empty), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetRoleEntitlementsAsync("tenant-a", OrganizationA,
            Guid.Empty, CancellationToken.None));

        Assert.Empty(store.Policies);
        Assert.Empty(store.Grants);
    }

    [Fact]
    public async Task InvalidDecisionInput_IsDeniedBeforeStoreLookupAsync()
    {
        FakeEndpointStore store = new();
        AuthorizationService service = new(store, NullLogger<AuthorizationService>.Instance);
        DecisionRequest invalidRole = new("alice", [Guid.Empty], "customers", "GET", "/customers/{id}",
            "tenant-a", OrganizationA);
        DecisionRequest tooManyRoles = invalidRole with
        {
            RoleIds = Enumerable.Repeat(Advisor, 17).ToArray()
        };
        DecisionRequest invalidRoute = invalidRole with { RoleIds = [Advisor], RouteTemplate = " " };

        Assert.False((await service.DecideAsync(invalidRole, CancellationToken.None)).Permit);
        Assert.False((await service.DecideAsync(tooManyRoles, CancellationToken.None)).Permit);
        Assert.False((await service.DecideAsync(invalidRoute, CancellationToken.None)).Permit);
        Assert.Empty(store.Policies);
    }

    [Fact]
    public async Task OversizedDecisionKey_IsDeniedBeforeDatabaseLookupAsync()
    {
        FakeEndpointStore store = new();
        AuthorizationService service = new(store, NullLogger<AuthorizationService>.Instance);
        DecisionRequest oversized = new("alice", [Advisor], new string('m', 129), "GET", "/status",
            "tenant-a", OrganizationA);

        DecisionResponse result = await service.DecideAsync(oversized, CancellationToken.None);

        Assert.False(result.Permit);
        Assert.Equal("Invalid authorization request", result.Reason);
    }

    [Fact]
    public async Task AuthenticatedProtectedDecisionWithoutOrganization_FailsClosedAsync()
    {
        FakeEndpointStore store = new();
        store.Policies.Add(Protected(Guid.Empty));
        store.Grants[Guid.Empty] = new HashSet<string> { "customers.view" };
        AuthorizationService service = new(store, NullLogger<AuthorizationService>.Instance);

        DecisionResponse result = await service.DecideAsync(Decision(Guid.Empty), CancellationToken.None);

        Assert.False(result.Permit);
        Assert.Equal("Unit organization required", result.Reason);
    }

    [Fact]
    public async Task AdminPolicyValidationRejectsOversizedKeysAndBlankEntitlementsAsync()
    {
        FakeEndpointStore store = new();
        AuthorizationService service = new(store, NullLogger<AuthorizationService>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpsertPolicyAsync(
            new PolicyRequest("tenant-a", "customers", "GET", " ", true, [], Guid.Empty), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpsertPolicyAsync(
            new PolicyRequest("tenant-a", "customers", new string('M', 17), "/status", true, [], Guid.Empty),
            CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpsertPolicyAsync(
            new PolicyRequest("tenant-a", "customers", "GET", new string('r', 513), true, [], Guid.Empty),
            CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpsertPolicyAsync(
            new PolicyRequest("tenant-a", "customers", "GET", "/status", false, [" "], OrganizationA),
            CancellationToken.None));
        Assert.Empty(store.Policies);
    }

    [Fact]
    public async Task RoleGrantAndPolicyAreScopedToOrganizationAsync()
    {
        FakeEndpointStore store = new();
        store.Policies.Add(Protected(OrganizationA));
        store.Policies.Add(Protected(OrganizationB));
        store.Grants[OrganizationA] = new HashSet<string> { "customers.view" };
        AuthorizationService service = new(store, NullLogger<AuthorizationService>.Instance);

        Assert.True((await service.DecideAsync(Decision(OrganizationA), CancellationToken.None)).Permit);
        Assert.False((await service.DecideAsync(Decision(OrganizationB), CancellationToken.None)).Permit);
    }

    [Fact]
    public async Task GlobalPublicPolicyPermitsAnonymousAndAuthenticatedUsersAsync()
    {
        FakeEndpointStore store = new();
        store.Policies.Add(new EndpointPolicy("tenant-a", "customers", "GET", "/customers/{id}", true, [], Guid.Empty));
        AuthorizationService service = new(store, NullLogger<AuthorizationService>.Instance);

        Assert.True((await service.DecideAsync(new DecisionRequest(null, [], "customers", "GET", "/customers/{id}", "tenant-a", Guid.Empty),
            CancellationToken.None)).Permit);
        Assert.True((await service.DecideAsync(Decision(OrganizationA), CancellationToken.None)).Permit);
    }

    [Fact]
    public async Task OrganizationProtectedPolicyOverridesGlobalPublicAsync()
    {
        FakeEndpointStore store = new();
        store.Policies.Add(new EndpointPolicy("tenant-a", "customers", "GET", "/customers/{id}", true, [], Guid.Empty));
        store.Policies.Add(Protected(OrganizationA));
        AuthorizationService service = new(store, NullLogger<AuthorizationService>.Instance);

        Assert.False((await service.DecideAsync(Decision(OrganizationA), CancellationToken.None)).Permit);
        Assert.True((await service.DecideAsync(Decision(OrganizationB), CancellationToken.None)).Permit);
    }

    [Fact]
    public async Task MissingPolicyDeniesAndAnonymousProtectedRequiresAuthenticationAsync()
    {
        FakeEndpointStore store = new();
        store.Policies.Add(Protected(Guid.Empty));
        AuthorizationService service = new(store, NullLogger<AuthorizationService>.Instance);

        DecisionResponse missing = await service.DecideAsync(Decision(OrganizationA), CancellationToken.None);
        DecisionResponse anonymous = await service.DecideAsync(
            new DecisionRequest(null, [], "customers", "GET", "/customers/{id}", "tenant-a", Guid.Empty), CancellationToken.None);

        Assert.False(missing.Permit);
        Assert.False(missing.AuthenticationRequired);
        Assert.True(anonymous.AuthenticationRequired);
    }

    [Fact]
    public async Task AnonymousWithoutMatchingPolicyRequiresAuthenticationAsync()
    {
        AuthorizationService service = new(new FakeEndpointStore(), NullLogger<AuthorizationService>.Instance);

        DecisionResponse decision = await service.DecideAsync(
            new DecisionRequest(null, [], "customers", "GET", "/customers/{id}", "tenant-a", Guid.Empty),
            CancellationToken.None);

        Assert.False(decision.Permit);
        Assert.True(decision.AuthenticationRequired);
    }

    private static EndpointPolicy Protected(Guid organizationId) =>
        new("tenant-a", "customers", "GET", "/customers/{id}", false, ["customers.view"], organizationId);

    private static DecisionRequest Decision(Guid organizationId) =>
        new("alice", [Advisor], "customers", "GET", "/customers/{id}", "tenant-a", organizationId);

    private sealed class FakeEndpointStore : IAuthorizationStore
    {
        public List<EndpointPolicy> Policies { get; } = [];
        public Dictionary<Guid, IReadOnlySet<string>> Grants { get; } = [];

        public Task<EndpointPolicy?> GetPolicyAsync(string tenantId, Guid unitOrganizationId, string module,
            string httpMethod, string routeTemplate, CancellationToken cancellationToken) =>
            Task.FromResult(Policies.SingleOrDefault(policy => policy.TenantId == tenantId &&
                policy.UnitOrganizationId == unitOrganizationId && policy.Module == module &&
                policy.HttpMethod == httpMethod && policy.RouteTemplate == routeTemplate));

        public Task<IReadOnlySet<string>> GetEntitlementsAsync(string tenantId, Guid unitOrganizationId,
            IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken) =>
            Task.FromResult(Grants.TryGetValue(unitOrganizationId, out IReadOnlySet<string>? grants)
                ? grants : (IReadOnlySet<string>)new HashSet<string>());

        public Task UpsertPolicyAsync(EndpointPolicy policy, CancellationToken cancellationToken)
        {
            Policies.RemoveAll(existing => existing.TenantId == policy.TenantId &&
                existing.UnitOrganizationId == policy.UnitOrganizationId && existing.Module == policy.Module &&
                existing.HttpMethod == policy.HttpMethod && existing.RouteTemplate == policy.RouteTemplate);
            Policies.Add(policy);
            return Task.CompletedTask;
        }
        public Task DeletePolicyAsync(string tenantId, Guid unitOrganizationId, string module, string httpMethod,
            string routeTemplate, CancellationToken cancellationToken)
        {
            Policies.RemoveAll(policy => policy.TenantId == tenantId && policy.UnitOrganizationId == unitOrganizationId &&
                policy.Module == module && policy.HttpMethod == httpMethod && policy.RouteTemplate == routeTemplate);
            return Task.CompletedTask;
        }
        public Task ReplaceRoleEntitlementsAsync(string tenantId, Guid unitOrganizationId, Guid roleId,
            IReadOnlyList<string> entitlements, CancellationToken cancellationToken)
        {
            Grants[unitOrganizationId] = entitlements.ToHashSet(StringComparer.Ordinal);
            return Task.CompletedTask;
        }
    }
}
