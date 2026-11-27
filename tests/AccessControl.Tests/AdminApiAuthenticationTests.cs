using System.Security.Claims;
using AccessControl.Service.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AccessControl.Tests;

public sealed class AdminApiAuthenticationTests
{
    [Fact]
    public async Task AdministratorWithoutAuthenticatedActor_IsRejectedByAccessControlAsync()
    {
        bool accessControlCalled = false;
        AccessControlRequestMiddleware access = new(_ =>
        {
            accessControlCalled = true;
            return Task.CompletedTask;
        }, NullLogger<AccessControlRequestMiddleware>.Instance);
        DefaultHttpContext accessContext = new();
        accessContext.Request.Method = "GET";
        accessContext.Request.Path = "/v1/policies/search";
        await access.InvokeAsync(accessContext);
        Assert.Equal(StatusCodes.Status401Unauthorized, accessContext.Response.StatusCode);
        Assert.False(accessControlCalled);
    }

    [Fact]
    public async Task AdministratorWithAuthenticatedActor_ReachesNextMiddlewareAsync()
    {
        bool called = false;
        AccessControlRequestMiddleware middleware = new(_ =>
        {
            called = true;
            return Task.CompletedTask;
        }, NullLogger<AccessControlRequestMiddleware>.Instance);
        DefaultHttpContext context = new();
        context.Request.Method = "GET";
        context.Request.Path = "/v1/policies/search";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("oid", Guid.NewGuid().ToString("D"))], "TestEntra"));

        await middleware.InvokeAsync(context);
        Assert.True(called);
    }
}
