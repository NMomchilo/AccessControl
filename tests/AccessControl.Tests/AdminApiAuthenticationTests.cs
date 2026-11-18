using System.Security.Claims;
using AccessControl.Service.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AccessControl.Tests;

public sealed class AdminApiAuthenticationTests
{
    [Fact]
    public async Task AdministratorKeyWithoutAuthenticatedActor_IsRejectedByAccessControlAsync()
    {
        bool accessControlCalled = false;
        ApiKeyMiddleware access = new(_ =>
        {
            accessControlCalled = true;
            return Task.CompletedTask;
        }, Options.Create(new ApiKeyOptions { DecisionClients = [new DecisionClientCredential { ClientId = "test", Key = "decision-key" }], AdminKey = "admin-key" }),
            NullLogger<ApiKeyMiddleware>.Instance);
        DefaultHttpContext accessContext = new();
        accessContext.Request.Method = "GET";
        accessContext.Request.Path = "/v1/policies/search";
        accessContext.Request.Headers[ApiKeyMiddleware.HeaderName] = "admin-key";
        await access.InvokeAsync(accessContext);
        Assert.Equal(StatusCodes.Status401Unauthorized, accessContext.Response.StatusCode);
        Assert.False(accessControlCalled);
    }

    [Fact]
    public async Task AdministratorKeyWithAuthenticatedActor_ReachesNextMiddlewareAsync()
    {
        bool called = false;
        ApiKeyMiddleware middleware = new(_ =>
        {
            called = true;
            return Task.CompletedTask;
        }, Options.Create(new ApiKeyOptions { DecisionClients = [new DecisionClientCredential { ClientId = "test", Key = "decision-key" }], AdminKey = "admin-key" }),
            NullLogger<ApiKeyMiddleware>.Instance);
        DefaultHttpContext context = new();
        context.Request.Method = "GET";
        context.Request.Path = "/v1/policies/search";
        context.Request.Headers[ApiKeyMiddleware.HeaderName] = "admin-key";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("oid", Guid.NewGuid().ToString("D"))], "TestEntra"));

        await middleware.InvokeAsync(context);
        Assert.True(called);
    }
}
