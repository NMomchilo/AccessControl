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
    private static readonly Guid Reviewer = Guid.Parse("22222222-2222-2222-2222-222222222222");
    [Fact]
    public async Task InvokeAsync_ResolvedReviewer_ReachesControllerOnlyAfterPermitAsync()
    {
        bool controllerRan = false;
        CapturingDecisionClient decisionClient = new();
        AccessControlMiddleware accessControl = new(_ =>
        {
            controllerRan = true;
            return Task.CompletedTask;
        }, decisionClient, Options.Create(new AccessControlClientOptions
        {
            Module = "customers",
            RoleIdClaimType = UserManagementClaims.RoleId,
            RoleIdentityAuthenticationType = UserManagementClaims.IdentityAuthenticationType
        }), NullLogger<AccessControlMiddleware>.Instance);
        UserManagementMiddleware userManagement = new(accessControl.InvokeAsync,
            new ReviewerClient(), Options.Create(new UserManagementClientOptions()), NullLogger<UserManagementMiddleware>.Instance);
        DefaultHttpContext context = new();
        context.Request.Method = "GET";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("oid", "9155ff4d-2708-4770-b87e-e238645ec5df"),
                new Claim("scp", "user_impersonation"), new Claim(ClaimTypes.Role, "untrusted-other-role"),
                new Claim(UserManagementClaims.RoleId, Guid.NewGuid().ToString("D"))], "jwt"));
        context.SetEndpoint(new RouteEndpointBuilder(_ => Task.CompletedTask,
            RoutePatternFactory.Parse("/customers/{id}"), 0).Build());

        await userManagement.InvokeAsync(context);

        Assert.True(controllerRan);
        Assert.Equal([Reviewer], decisionClient.Request?.RoleIds);
        Assert.Equal("9155ff4d-2708-4770-b87e-e238645ec5df", decisionClient.Request?.SubjectId);
    }

    private sealed class ReviewerClient : IUserManagementClient
    {
        public Task<UserIdentityResponse> ResolveAsync(UserIdentityRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new UserIdentityResponse(Guid.NewGuid(), request.TenantId, true,
                [new UserRoleInfo(Reviewer, "reviewer", "Reviewer")]));
    }

    private sealed class CapturingDecisionClient : IAccessControlDecisionClient
    {
        public DecisionRequest? Request { get; private set; }

        public Task<DecisionResponse> DecideAsync(DecisionRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new DecisionResponse(request.RoleIds.Contains(Reviewer), "Permit"));
        }
    }
}
