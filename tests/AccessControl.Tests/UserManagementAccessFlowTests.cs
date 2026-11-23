using System.Security.Claims;
using AccessControl.Client;
using AccessControl.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UserManagement.Client;
using UserManagement.Contracts;

namespace AccessControl.Tests;

public sealed class UserManagementAccessFlowTests
{
    private static readonly Guid Reader = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Organization = Guid.Parse("33333333-3333-3333-3333-333333333333");
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task InvokeAsync_ResolvedDutiesReachAccessControlDecisionAsync(bool isSuperAdmin, bool isUnitAdmin)
    {
        bool controllerRan = false;
        CapturingDecisionClient decisionClient = new();
        AccessControlMiddleware accessControl = new(_ =>
        {
            controllerRan = true;
            return Task.CompletedTask;
        }, decisionClient, Options.Create(new AccessControlClientOptions
        {
            RoleIdClaimType = UserManagementClaims.RoleId,
            RoleIdentityAuthenticationType = UserManagementClaims.IdentityAuthenticationType
        }), NullLogger<AccessControlMiddleware>.Instance);
        UserManagementMiddleware userManagement = new(accessControl.InvokeAsync,
            new ReaderClient(isSuperAdmin, isUnitAdmin), Options.Create(new UserManagementClientOptions()),
            NullLogger<UserManagementMiddleware>.Instance);
        DefaultHttpContext context = new();
        context.Request.Method = "GET";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("oid", "9155ff4d-2708-4770-b87e-e238645ec5df"),
                new Claim("scp", "user_impersonation"), new Claim(ClaimTypes.Role, "untrusted-other-role"),
                new Claim(UserManagementClaims.RoleId, Guid.NewGuid().ToString("D"))], "jwt"));
        context.User.AddIdentity(new ClaimsIdentity(
            [new Claim("unit_organization_id", Organization.ToString("D")),
                new Claim("unit_organization_name", "Customer Operations")], "Gateway"));
        context.SetEndpoint(new RouteEndpointBuilder(_ => Task.CompletedTask,
            RoutePatternFactory.Parse("/customers/{id}"), 0).Build());

        await userManagement.InvokeAsync(context);

        Assert.True(controllerRan);
        Assert.Equal(Organization, decisionClient.Request?.UnitOrganizationId);
        Assert.Equal("9155ff4d-2708-4770-b87e-e238645ec5df", decisionClient.Request?.SubjectId);
        Assert.Equal([Reader], decisionClient.Request?.RoleIds);
        Assert.Equal(isSuperAdmin, decisionClient.Request?.IsSuperAdmin);
        Assert.Equal(isUnitAdmin, decisionClient.Request?.IsUnitAdmin);
    }

    private sealed class ReaderClient(bool isSuperAdmin, bool isUnitAdmin) : IUserManagementClient
    {
        public Task<UserIdentityResponse> ResolveAsync(UserIdentityRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new UserIdentityResponse(Guid.NewGuid(), request.TenantId,
                request.UnitOrganizationId, "Alpha", true,
                [new UserRoleInfo(Reader, "Reader", "reader")], isSuperAdmin, isUnitAdmin));
    }

    private sealed class CapturingDecisionClient : IAccessControlDecisionClient
    {
        public DecisionRequest? Request { get; private set; }

        public Task<DecisionResponse> DecideAsync(DecisionRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new DecisionResponse(true, "Permit"));
        }
    }
}
