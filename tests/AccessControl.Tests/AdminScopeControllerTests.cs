using System.Security.Claims;
using AccessControl.Application;
using AccessControl.Contracts;
using AccessControl.Domain;
using AccessControl.Service.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessControl.Tests;

public sealed class AdminScopeControllerTests
{
    private static readonly Guid Organization = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherOrganization = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task PolicyMutations_RequireActorAndMatchingScopeAsync()
    {
        FakeScopeAuthorizer scope = new() { AllowedOrganization = Organization };
        FakeStore store = new();
        PoliciesController controller = Policies(store, scope);
        SetActor(controller, "");
        PolicyRequest request = new("tenant-a", "GET", "/status", false,
            [Guid.NewGuid()], Organization);

        Assert.IsType<ForbidResult>(await controller.UpsertAsync(request, CancellationToken.None));
        Assert.Equal(0, store.Writes);
        SetActor(controller, "actor-1");
        Assert.IsType<NoContentResult>(await controller.UpsertAsync(request, CancellationToken.None));
        Assert.Equal(1, store.Writes);
        Assert.Equal(("actor-1", "tenant-a", Organization, false), scope.LastCall);
        Assert.IsType<ForbidResult>(await controller.DeleteAsync(
            new("tenant-a", "GET", "/status", OtherOrganization), CancellationToken.None));
        Assert.Equal(1, store.Writes);
    }

    [Fact]
    public async Task GlobalPublicPolicyAndTenantWideSearch_RequireSuperAdminAsync()
    {
        FakeScopeAuthorizer scope = new() { AllowedOrganization = Organization };
        FakeStore store = new();
        PoliciesController controller = Policies(store, scope);
        SetActor(controller, "actor-1");

        Assert.IsType<ForbidResult>(await controller.UpsertAsync(
            new("tenant-a", "GET", "/status", true, [], Guid.Empty), CancellationToken.None));
        Assert.Equal(("actor-1", "tenant-a", Guid.Empty, true), scope.LastCall);
        Assert.IsType<ForbidResult>(await controller.SearchAsync("tenant-a", null, null,
            cancellationToken: CancellationToken.None));
        Assert.Equal(("actor-1", "tenant-a", (Guid?)null, true), scope.LastCall);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task RoleGrantsAndEntitlementCreation_RequireSelectedOrganizationAsync()
    {
        FakeScopeAuthorizer scope = new() { AllowedOrganization = Organization };
        FakeStore store = new();
        RolesController roles = new(new AuthorizationService(store, NullLogger<AuthorizationService>.Instance),
            scope, NullLogger<RolesController>.Instance);
        SetActor(roles, "actor-1");
        Assert.IsType<ForbidResult>(await roles.ReplaceEntitlementsAsync(
            new("tenant-a", Guid.NewGuid(), [], OtherOrganization), CancellationToken.None));
        Assert.Equal(0, store.Writes);
        Assert.IsType<NoContentResult>(await roles.ReplaceEntitlementsAsync(
            new("tenant-a", Guid.NewGuid(), [], Organization), CancellationToken.None));
        Assert.Equal(1, store.Writes);

        FakeEntitlements entitlements = new();
        EntitlementsController controller = new(new EntitlementCatalogService(entitlements,
            NullLogger<EntitlementCatalogService>.Instance), scope,
            NullLogger<EntitlementsController>.Instance);
        SetActor(controller, "actor-1");
        Assert.IsType<ForbidResult>(await controller.CreateAsync(
            new("tenant-a", OtherOrganization, "Read"), CancellationToken.None));
        Assert.Equal(0, entitlements.Writes);
        Assert.IsType<CreatedResult>(await controller.CreateAsync(
            new("tenant-a", Organization, "Read"), CancellationToken.None));
        Assert.Equal(1, entitlements.Writes);
        Guid entitlementId = Guid.NewGuid();
        Assert.IsType<NotFoundResult>(await controller.SetActiveAsync(entitlementId, "tenant-a", Organization,
            new SetEntitlementActiveRequest(false), CancellationToken.None));
        Assert.IsType<NotFoundResult>(await controller.UpdateDisplayNameAsync(entitlementId, "tenant-a", Organization,
            new UpdateEntitlementDisplayNameRequest("Private display name"), CancellationToken.None));
    }

    private static PoliciesController Policies(FakeStore store, FakeScopeAuthorizer scope) =>
        new(new AuthorizationService(store, NullLogger<AuthorizationService>.Instance),
            new PolicyCatalogService(new FakePolicies(), NullLogger<PolicyCatalogService>.Instance),
            scope, NullLogger<PoliciesController>.Instance);

    private static void SetActor(ControllerBase controller, string oid) => controller.ControllerContext =
        new ControllerContext { HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", oid)], "test"))
        } };

    private sealed class FakeScopeAuthorizer : IAdminScopeAuthorizer
    {
        public Guid AllowedOrganization { get; set; }
        public (string Actor, string Tenant, Guid? Organization, bool SuperAdmin) LastCall { get; private set; }
        public Task<bool> CanManageAsync(string actorObjectId, string tenantId, Guid? unitOrganizationId,
            bool requireSuperAdmin, CancellationToken cancellationToken)
        {
            LastCall = (actorObjectId, tenantId, unitOrganizationId, requireSuperAdmin);
            return Task.FromResult(!requireSuperAdmin && tenantId == "tenant-a" && unitOrganizationId == AllowedOrganization);
        }
    }

    private sealed class FakeStore : IAuthorizationStore
    {
        public int Writes { get; private set; }
        public Task<EndpointPolicy?> GetPolicyAsync(string tenantId, Guid unitOrganizationId,
            string httpMethod, string routeTemplate, CancellationToken cancellationToken) => Task.FromResult<EndpointPolicy?>(null);
        public Task<IReadOnlySet<Guid>> GetEntitlementsAsync(string tenantId, Guid unitOrganizationId,
            IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken) => Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
        public Task<IReadOnlyList<Guid>> GetAssignedEntitlementIdsAsync(string tenantId, Guid unitOrganizationId,
            Guid roleId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>([]);
        public Task UpsertPolicyAsync(EndpointPolicy policy, CancellationToken cancellationToken)
        {
            Writes++;
            return Task.CompletedTask;
        }
        public Task DeletePolicyAsync(string tenantId, Guid unitOrganizationId,
            string httpMethod, string routeTemplate, CancellationToken cancellationToken)
        {
            Writes++;
            return Task.CompletedTask;
        }
        public Task ReplaceRoleEntitlementsAsync(string tenantId, Guid unitOrganizationId, Guid roleId,
            IReadOnlyList<Guid> entitlementIds, CancellationToken cancellationToken)
        {
            Writes++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePolicies : IPolicyCatalog
    {
        public Task<PolicyPageResponse> SearchAsync(string tenantId, Guid? unitOrganizationId,
            string? httpMethod, int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(new PolicyPageResponse([], page, pageSize, 0));
    }

    private sealed class FakeEntitlements : IEntitlementCatalog
    {
        public int Writes { get; private set; }
        public Task<EntitlementInfo> CreateAsync(string tenantId, Guid unitOrganizationId,
            string displayName, CancellationToken cancellationToken)
        {
            Writes++;
            return Task.FromResult(new EntitlementInfo(Guid.NewGuid(), tenantId, unitOrganizationId, displayName, true));
        }
        public Task<bool> SetActiveAsync(string tenantId, Guid unitOrganizationId, Guid entitlementId,
            bool isActive, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<EntitlementInfo?> UpdateDisplayNameAsync(string tenantId, Guid unitOrganizationId,
            Guid entitlementId, string displayName, CancellationToken cancellationToken) => Task.FromResult<EntitlementInfo?>(null);
        public Task<EntitlementPageResponse> ListAsync(string tenantId, Guid? unitOrganizationId, int page,
            int pageSize, CancellationToken cancellationToken) => Task.FromResult(new EntitlementPageResponse([], page, pageSize, 0));
        public Task<EntitlementInfo?> GetAsync(string tenantId, Guid unitOrganizationId, Guid entitlementId,
            CancellationToken cancellationToken) => Task.FromResult<EntitlementInfo?>(null);
    }
}
