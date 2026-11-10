using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using UserManagement.Client;
using UserManagement.Contracts;

namespace AccessControl.Tests;

public sealed class UserManagementMiddlewareTests
{
    private const string Oid = "9155ff4d-2708-4770-b87e-e238645ec5df";

    [Fact]
    public async Task InvokeAsync_AuthenticatedUser_AddsDedicatedRoleClaimsAsync()
    {
        CapturingClient client = new();
        bool ran = false;
        UserManagementMiddleware sut = Create(client, _ => { ran = true; return Task.CompletedTask; });
        DefaultHttpContext context = AuthenticatedContext(new Claim("oid", Oid));

        await sut.InvokeAsync(context);

        Assert.True(ran);
        Assert.Equal(Oid, client.Request?.ObjectId);
        Assert.Contains(context.User.Claims, claim => claim.Type == UserManagementClaims.Role && claim.Value == "reviewer");
        Assert.Contains(context.User.Claims, claim => claim.Type == UserManagementClaims.UserId);
    }

    [Fact]
    public async Task InvokeAsync_MappedOid_ResolvesUserAsync()
    {
        CapturingClient client = new();
        UserManagementMiddleware sut = Create(client);
        DefaultHttpContext context = AuthenticatedContext(
            new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Oid));

        await sut.InvokeAsync(context);

        Assert.Equal(Oid, client.Request?.ObjectId);
    }

    [Fact]
    public async Task InvokeAsync_EntraV1Upn_SendsProfileEmailAsync()
    {
        CapturingClient client = new();
        UserManagementMiddleware sut = Create(client);
        DefaultHttpContext context = AuthenticatedContext(new Claim("oid", Oid));
        context.User.AddIdentity(new ClaimsIdentity([new Claim("upn", "user@example.com"),
            new Claim("name", "Example User")]));

        await sut.InvokeAsync(context);

        Assert.Equal("user@example.com", client.Request?.Email);
        Assert.Equal("Example User", client.Request?.DisplayName);
    }

    [Fact]
    public async Task InvokeAsync_AnonymousRequest_SkipsResolutionAsync()
    {
        CapturingClient client = new();
        bool ran = false;
        UserManagementMiddleware sut = Create(client, _ => { ran = true; return Task.CompletedTask; });
        DefaultHttpContext context = new();

        await sut.InvokeAsync(context);

        Assert.True(ran);
        Assert.Null(client.Request);
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedWithoutOid_Returns401Async()
    {
        CapturingClient client = new();
        UserManagementMiddleware sut = Create(client);
        DefaultHttpContext context = AuthenticatedContext(new Claim("sub", "not-oid"));

        await sut.InvokeAsync(context);

        Assert.Equal(401, context.Response.StatusCode);
        Assert.Null(client.Request);
    }

    [Fact]
    public async Task InvokeAsync_InactiveUser_Returns403Async()
    {
        UserManagementMiddleware sut = Create(new CapturingClient(false));
        DefaultHttpContext context = AuthenticatedContext(new Claim("oid", Oid));

        await sut.InvokeAsync(context);

        Assert.Equal(403, context.Response.StatusCode);
        Assert.DoesNotContain(context.User.Claims, claim => claim.Type == UserManagementClaims.Role);
    }

    [Fact]
    public async Task InvokeAsync_ResolutionFailure_Returns503Async()
    {
        UserManagementMiddleware sut = Create(new FailingClient());
        DefaultHttpContext context = AuthenticatedContext(new Claim("oid", Oid));

        await sut.InvokeAsync(context);

        Assert.Equal(503, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_AppOnlyPrincipal_DoesNotProvisionUserAsync()
    {
        CapturingClient client = new();
        UserManagementMiddleware sut = Create(client);
        DefaultHttpContext context = new();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", Oid)], "test"));

        await sut.InvokeAsync(context);

        Assert.Equal(403, context.Response.StatusCode);
        Assert.Null(client.Request);
    }

    private static UserManagementMiddleware Create(IUserManagementClient client, RequestDelegate? next = null) =>
        new(next ?? (_ => Task.CompletedTask), client, NullLogger<UserManagementMiddleware>.Instance);

    private static DefaultHttpContext AuthenticatedContext(Claim claim)
    {
        DefaultHttpContext context = new();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([claim, new Claim("scp", "user_impersonation")], "test"));
        return context;
    }

    private sealed class CapturingClient(bool active = true) : IUserManagementClient
    {
        public UserIdentityRequest? Request { get; private set; }

        public Task<UserIdentityResponse> ResolveAsync(UserIdentityRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new UserIdentityResponse(Guid.NewGuid(), active, ["reviewer"]));
        }
    }

    private sealed class FailingClient : IUserManagementClient
    {
        public Task<UserIdentityResponse> ResolveAsync(UserIdentityRequest request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("unavailable");
    }
}
