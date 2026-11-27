using System.Security.Claims;
using System.Text.Json;
using AccessControl.Contracts;
using AccessControl.Service.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessControl.Tests;

public sealed class AccessControlRequestMiddlewareTests
{
    [Fact]
    public async Task AnonymousDecisionWithoutKeyReachesEndpointAsync()
    {
        bool reached = false;
        AccessControlRequestMiddleware middleware = new(_ => { reached = true; return Task.CompletedTask; },
            NullLogger<AccessControlRequestMiddleware>.Instance);
        DefaultHttpContext context = new();
        context.Request.Path = "/v1/decisions";
        context.Request.Method = "POST";
        context.Request.Body = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(
            new DecisionRequest(null, "GET", "/payments", "default", Guid.Empty)));

        await middleware.InvokeAsync(context);

        Assert.True(reached);
    }

    [Fact]
    public async Task RegistrationWithoutKeyGetsExplicitWriteActorAsync()
    {
        bool reached = false;
        AccessControlRequestMiddleware middleware = new(_ => { reached = true; return Task.CompletedTask; },
            NullLogger<AccessControlRequestMiddleware>.Instance);
        DefaultHttpContext context = new();
        context.Request.Path = "/v1/registrations/catalog";
        context.Request.Method = "POST";

        await middleware.InvokeAsync(context);

        Assert.True(reached);
        Assert.Equal("unattributed-registration", context.User.FindFirst("service_client_id")?.Value);
    }

    [Theory]
    [InlineData("GET", "/health", 200)]
    [InlineData("GET", "/ready", 200)]
    [InlineData("POST", "/health", 401)]
    [InlineData("POST", "/ready", 401)]
    public async Task OnlyGetProbesBypassActorCheckAsync(string method, string path, int status)
    {
        AccessControlRequestMiddleware middleware = new(_ => Task.CompletedTask, NullLogger<AccessControlRequestMiddleware>.Instance);
        DefaultHttpContext context = new();
        context.Request.Path = path;
        context.Request.Method = method;

        await middleware.InvokeAsync(context);

        Assert.Equal(status, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("{", null, 400)]
    [InlineData("null", null, 400)]
    [InlineData("{}", 20000, 413)]
    public async Task DecisionRejectsMalformedAndOversizedPayloadsAsync(string body, int? length, int status)
    {
        AccessControlRequestMiddleware middleware = new(_ => Task.CompletedTask, NullLogger<AccessControlRequestMiddleware>.Instance);
        DefaultHttpContext context = new();
        context.Request.Path = "/v1/decisions";
        context.Request.Method = "POST";
        context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body));
        if (length.HasValue) context.Request.ContentLength = length;

        await middleware.InvokeAsync(context);

        Assert.Equal(status, context.Response.StatusCode);
    }

    [Fact]
    public async Task AdministratorRouteRequiresAuthenticatedOidAsync()
    {
        AccessControlRequestMiddleware middleware = new(_ => Task.CompletedTask, NullLogger<AccessControlRequestMiddleware>.Instance);
        DefaultHttpContext context = new();
        context.Request.Path = "/v1/policies";
        context.Request.Method = "GET";
        await middleware.InvokeAsync(context);
        Assert.Equal(401, context.Response.StatusCode);

        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", Guid.NewGuid().ToString("D"))], "Entra"));
        context.Response.StatusCode = 200;
        await middleware.InvokeAsync(context);
        Assert.Equal(200, context.Response.StatusCode);
    }
}
