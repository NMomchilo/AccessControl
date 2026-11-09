using System.Security.Claims;
using AccessControl.Client;
using AccessControl.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AccessControl.Tests;

public sealed class AccessControlMiddlewareTests
{
    [Theory]
    [InlineData(true, false, 200, true)]
    [InlineData(false, false, 403, false)]
    [InlineData(false, true, 401, false)]
    public async Task InvokeAsync_AppliesDecisionAsync(bool permit, bool authenticationRequired, int expectedStatus, bool runs)
    {
        bool ran = false;
        AccessControlMiddleware sut = CreateMiddleware(new FixedDecisionClient(new DecisionResponse(permit, "test", authenticationRequired)),
            _ => { ran = true; return Task.CompletedTask; });
        DefaultHttpContext context = CreateContext();

        await sut.InvokeAsync(context);

        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.Equal(runs, ran);
    }

    [Fact]
    public async Task InvokeAsync_AnonymousRequest_AsksServiceAboutPublicPolicyAsync()
    {
        CapturingDecisionClient client = new();
        AccessControlMiddleware sut = CreateMiddleware(client);
        DefaultHttpContext context = CreateContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity());

        await sut.InvokeAsync(context);

        Assert.Null(client.Request?.SubjectId);
        Assert.Empty(client.Request?.Roles ?? []);
        Assert.Equal("GET", client.Request?.HttpMethod);
        Assert.Equal("/customers/{id}", client.Request?.RouteTemplate);
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedRequest_SendsEndpointIdentityAndRolesAsync()
    {
        CapturingDecisionClient client = new();
        AccessControlMiddleware sut = CreateMiddleware(client);
        DefaultHttpContext context = CreateContext();
        context.User.AddIdentity(new ClaimsIdentity([new Claim("tenant_id", "tenant-a")]));

        await sut.InvokeAsync(context);

        Assert.Equal("alice", client.Request?.SubjectId);
        Assert.Equal("tenant-a", client.Request?.TenantId);
        Assert.Equal("customers", client.Request?.Module);
        Assert.Contains("advisor", client.Request?.Roles ?? []);
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedRequestWithoutOid_Returns401Async()
    {
        CapturingDecisionClient client = new();
        AccessControlMiddleware sut = CreateMiddleware(client);
        DefaultHttpContext context = CreateContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "other-id"), new Claim("sub", "other-sub")], "test"));

        await sut.InvokeAsync(context);

        Assert.Equal(401, context.Response.StatusCode);
        Assert.Null(client.Request);
    }

    [Fact]
    public async Task InvokeAsync_MappedOidClaim_SendsOidAsSubjectAsync()
    {
        CapturingDecisionClient client = new();
        AccessControlMiddleware sut = CreateMiddleware(client);
        DefaultHttpContext context = CreateContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", "mapped-oid"),
                new Claim(ClaimTypes.NameIdentifier, "other-id")], "test"));

        await sut.InvokeAsync(context);

        Assert.Equal("mapped-oid", client.Request?.SubjectId);
    }

    [Fact]
    public async Task InvokeAsync_UnknownRoute_PassesThroughAsync()
    {
        bool ran = false;
        AccessControlMiddleware sut = CreateMiddleware(new CapturingDecisionClient(), _ => { ran = true; return Task.CompletedTask; });
        DefaultHttpContext context = CreateContext();
        context.SetEndpoint(null);

        await sut.InvokeAsync(context);

        Assert.True(ran);
    }

    [Fact]
    public async Task InvokeAsync_ServiceUnavailable_Returns503Async()
    {
        AccessControlMiddleware sut = CreateMiddleware(new FailingDecisionClient());
        DefaultHttpContext context = CreateContext();

        await sut.InvokeAsync(context);

        Assert.Equal(503, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_ExplicitHealthExclusion_BypassesDecisionAsync()
    {
        CapturingDecisionClient client = new();
        bool ran = false;
        AccessControlMiddleware sut = CreateMiddleware(client, _ => { ran = true; return Task.CompletedTask; }, ["/health"]);
        DefaultHttpContext context = CreateContext("/health");

        await sut.InvokeAsync(context);

        Assert.True(ran);
        Assert.Null(client.Request);
    }

    private static AccessControlMiddleware CreateMiddleware(IAccessControlDecisionClient client, RequestDelegate? next = null,
        string[]? excludedPaths = null) =>
        new(next ?? (_ => Task.CompletedTask), client,
            Options.Create(new AccessControlClientOptions { Module = "customers", ExcludedPaths = excludedPaths ?? [] }),
            NullLogger<AccessControlMiddleware>.Instance);

    private static DefaultHttpContext CreateContext(string route = "/customers/{id}")
    {
        DefaultHttpContext context = new();
        context.Request.Method = "GET";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("oid", "alice"), new Claim(ClaimTypes.NameIdentifier, "other-id"),
                new Claim("sub", "other-sub"), new Claim(ClaimTypes.Role, "advisor")], "test"));
        context.SetEndpoint(new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse(route), 0).Build());
        return context;
    }

    private sealed class FixedDecisionClient(DecisionResponse response) : IAccessControlDecisionClient
    {
        public Task<DecisionResponse> DecideAsync(DecisionRequest request, CancellationToken cancellationToken) => Task.FromResult(response);
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

    private sealed class FailingDecisionClient : IAccessControlDecisionClient
    {
        public Task<DecisionResponse> DecideAsync(DecisionRequest request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("unavailable");
    }
}
