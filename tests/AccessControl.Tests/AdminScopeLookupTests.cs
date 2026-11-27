using System.Net;
using System.Security.Claims;
using System.Text.Json;
using AccessControl.Service.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AccessControl.Tests;

public sealed class AdminScopeLookupTests
{
    [Fact]
    public async Task Lookup_ForwardsValidatedEntraBearerAndRespectsOrganizationScopeAsync()
    {
        Guid oid = Guid.NewGuid();
        Guid allowed = Guid.NewGuid();
        string? forwardedBearer = null;
        bool lookupKeyPresent = false;
        using HttpClient client = new(new ReplyHandler(request =>
        {
            forwardedBearer = request.Headers.Authorization?.Parameter;
            lookupKeyPresent = request.Headers.Contains("X-User-Management-Key");
            string response = JsonSerializer.Serialize(new
            {
                isActive = true, isSuperAdmin = false,
                managedUnitOrganizationIds = new[] { allowed }, canManage = true
            });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) };
        })) { BaseAddress = new Uri("http://localhost:5220") };
        DefaultHttpContext context = new();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("oid", oid.ToString("D"))], "Bearer"));
        context.Request.Headers.Authorization = "Bearer signed-actor-token";
        HttpContextAccessor accessor = new() { HttpContext = context };
        UserManagementAdminScopeAuthorizer authorizer = new(client, accessor,
            NullLogger<UserManagementAdminScopeAuthorizer>.Instance);

        Assert.True(await authorizer.CanManageAsync(oid.ToString("D"), "default", allowed, false, CancellationToken.None));
        Assert.False(await authorizer.CanManageAsync(oid.ToString("D"), "default", allowed, true, CancellationToken.None));
        Assert.Equal("signed-actor-token", forwardedBearer);
        Assert.False(lookupKeyPresent);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    public async Task Lookup_DeniesInvalidActorAndFailsClosedOnServiceErrorAsync(
        HttpStatusCode serviceStatus, bool throwsUnavailable)
    {
        Guid oid = Guid.NewGuid();
        using HttpClient client = new(new ReplyHandler(_ => new HttpResponseMessage(serviceStatus)))
        { BaseAddress = new Uri("http://localhost:5220") };
        DefaultHttpContext context = new();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("oid", oid.ToString("D"))], "Bearer"));
        HttpContextAccessor accessor = new() { HttpContext = context };
        UserManagementAdminScopeAuthorizer authorizer = new(client, accessor,
            NullLogger<UserManagementAdminScopeAuthorizer>.Instance);

        Assert.False(await authorizer.CanManageAsync(Guid.NewGuid().ToString("D"), "default",
            Guid.NewGuid(), false, CancellationToken.None));
        Assert.False(await authorizer.CanManageAsync(oid.ToString("D"), "default",
            Guid.NewGuid(), false, CancellationToken.None));
        context.Request.Headers.Authorization = "Bearer signed-actor-token";
        if (throwsUnavailable)
            await Assert.ThrowsAsync<AdminLookupUnavailableException>(() => authorizer.CanManageAsync(
                oid.ToString("D"), "default", Guid.NewGuid(), false, CancellationToken.None));
        else
            Assert.False(await authorizer.CanManageAsync(oid.ToString("D"), "default",
                Guid.NewGuid(), false, CancellationToken.None));
    }

    private sealed class ReplyHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(reply(request));
    }
}
