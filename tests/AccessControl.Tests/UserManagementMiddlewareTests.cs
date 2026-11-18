using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UserManagement.Client;
using UserManagement.Contracts;

namespace AccessControl.Tests;

public sealed class UserManagementMiddlewareTests
{
    private const string Oid = "9155ff4d-2708-4770-b87e-e238645ec5df";
    private static readonly Guid Reader = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OrganizationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task InvokeAsync_UnprovisionedUser_Returns403WithoutContinuingAsync()
    {
        bool continued = false;
        UserManagementMiddleware middleware = Create(new UnprovisionedClient(),
            _ => { continued = true; return Task.CompletedTask; });
        DefaultHttpContext context = AuthenticatedContext(new Claim("oid", Oid));

        await middleware.InvokeAsync(context);

        Assert.Equal(403, context.Response.StatusCode);
        Assert.False(continued);
    }

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
        Assert.Equal("default", client.Request?.TenantId);
        Assert.Equal(OrganizationId, client.Request?.UnitOrganizationId);
        Assert.Contains(context.User.Claims, claim => claim.Type == UserManagementClaims.Role && claim.Value == "reader");
        Assert.Contains(context.User.Claims, claim => claim.Type == UserManagementClaims.RoleId && claim.Value == Reader.ToString("D"));
        Assert.True(context.User.IsInRole("reader"));
        Assert.Contains(context.User.Claims, claim => claim.Type == UserManagementClaims.UserId);
        Assert.Contains(context.User.Claims, claim => claim.Type == UserManagementClaims.UnitOrganizationId &&
            claim.Value == OrganizationId.ToString("D"));
        Assert.Contains(context.User.Claims, claim => claim.Type == "user_management_unit_organization_id" &&
            claim.Value == OrganizationId.ToString("D"));
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
    public async Task InvokeAsync_DeletedOrganization_Returns403Async()
    {
        UserManagementMiddleware sut = Create(new RejectedOrganizationClient());
        DefaultHttpContext context = AuthenticatedContext(new Claim("oid", Oid));
        await sut.InvokeAsync(context);
        Assert.Equal(403, context.Response.StatusCode);
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

    [Fact]
    public async Task InvokeAsync_UnrelatedDelegatedScope_DoesNotProvisionUserAsync()
    {
        CapturingClient client = new();
        UserManagementMiddleware sut = Create(client);
        DefaultHttpContext context = new();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("oid", Oid), new Claim("scp", "other_scope")], "jwt"));

        await sut.InvokeAsync(context);

        Assert.Equal(403, context.Response.StatusCode);
        Assert.Null(client.Request);
    }

    [Fact]
    public async Task InvokeAsync_WithoutTrustedGatewayIdentity_DoesNotResolveAsync()
    {
        CapturingClient client = new();
        DefaultHttpContext context = new();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("oid", Oid), new Claim("scp", "user_impersonation"),
            new Claim("unit_organization_id", OrganizationId.ToString("D")),
            new Claim("unit_organization_name", "Alpha")], "jwt"));
        await Create(client).InvokeAsync(context);
        Assert.Equal(403, context.Response.StatusCode);
        Assert.Null(client.Request);
    }

    [Fact]
    public async Task InvokeAsync_InvalidGatewayOrganizationId_DoesNotResolveAsync()
    {
        CapturingClient client = new();
        DefaultHttpContext context = AuthenticatedContext(new Claim("oid", Oid));
        ClaimsIdentity gateway = context.User.Identities.Single(identity => identity.AuthenticationType == "Gateway");
        gateway.RemoveClaim(gateway.FindFirst("unit_organization_id")!);
        gateway.AddClaim(new Claim("unit_organization_id", "invalid"));
        await Create(client).InvokeAsync(context);
        Assert.Equal(403, context.Response.StatusCode);
        Assert.Null(client.Request);
    }

    [Fact]
    public async Task InvokeAsync_InvalidTenant_DeniesBeforeResolutionAsync()
    {
        CapturingClient client = new();
        DefaultHttpContext context = AuthenticatedContext(new Claim("oid", Oid));
        context.User.Identities.First().AddClaim(new Claim("tenant_id", new string('x', 129)));

        await Create(client).InvokeAsync(context);

        Assert.Equal(403, context.Response.StatusCode);
        Assert.Null(client.Request);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvokeAsync_MismatchedResolution_DoesNotAddClaimsOrRunControllerAsync(bool wrongTenant)
    {
        bool ranController = false;
        UserManagementMiddleware sut = Create(new MismatchedClient(wrongTenant),
            _ => { ranController = true; return Task.CompletedTask; });
        DefaultHttpContext context = AuthenticatedContext(new Claim("oid", Oid));

        await sut.InvokeAsync(context);

        Assert.Equal(503, context.Response.StatusCode);
        Assert.False(ranController);
        Assert.DoesNotContain(context.User.Claims, claim => claim.Type == UserManagementClaims.RoleId);
    }

    [Fact]
    public async Task InvokeAsync_DuplicateGatewayOrganizationClaims_DeniesBeforeResolutionAsync()
    {
        CapturingClient client = new();
        DefaultHttpContext context = AuthenticatedContext(new Claim("oid", Oid));
        context.User.Identities.Single(identity => identity.AuthenticationType == "Gateway")
            .AddClaim(new Claim("unit_organization_id", Guid.NewGuid().ToString("D")));

        await Create(client).InvokeAsync(context);

        Assert.Equal(403, context.Response.StatusCode);
        Assert.Null(client.Request);
    }

    private static UserManagementMiddleware Create(IUserManagementClient client, RequestDelegate? next = null) =>
        new(next ?? (_ => Task.CompletedTask), client, Options.Create(new UserManagementClientOptions()),
            NullLogger<UserManagementMiddleware>.Instance);

    private static DefaultHttpContext AuthenticatedContext(Claim claim)
    {
        DefaultHttpContext context = new();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([claim, new Claim("scp", "user_impersonation")], "test"));
        context.User.AddIdentity(new ClaimsIdentity([
            new Claim("unit_organization_id", OrganizationId.ToString("D")),
            new Claim("unit_organization_name", "Alpha")], "Gateway"));
        return context;
    }

    private sealed class CapturingClient(bool active = true) : IUserManagementClient
    {
        public UserIdentityRequest? Request { get; private set; }

        public Task<UserIdentityResponse> ResolveAsync(UserIdentityRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new UserIdentityResponse(Guid.NewGuid(), request.TenantId,
                request.UnitOrganizationId, "Alpha", active,
                [new UserRoleInfo(Reader, "reader", "Reader")]));
        }
    }

    private sealed class FailingClient : IUserManagementClient
    {
        public Task<UserIdentityResponse> ResolveAsync(UserIdentityRequest request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("unavailable");
    }

    private sealed class UnprovisionedClient : IUserManagementClient
    {
        public Task<UserIdentityResponse> ResolveAsync(UserIdentityRequest request, CancellationToken cancellationToken) =>
            throw new UserNotProvisionedException();
    }

    private sealed class RejectedOrganizationClient : IUserManagementClient
    {
        public Task<UserIdentityResponse> ResolveAsync(UserIdentityRequest request,
            CancellationToken cancellationToken) => throw new UnitOrganizationAccessException();
    }

    private sealed class MismatchedClient(bool wrongTenant) : IUserManagementClient
    {
        public Task<UserIdentityResponse> ResolveAsync(UserIdentityRequest request,
            CancellationToken cancellationToken) => Task.FromResult(new UserIdentityResponse(Guid.NewGuid(),
            wrongTenant ? "another-tenant" : request.TenantId,
            wrongTenant ? request.UnitOrganizationId : Guid.NewGuid(), "Alpha", true,
            [new UserRoleInfo(Reader, "reader", "Reader")]));
    }
}
