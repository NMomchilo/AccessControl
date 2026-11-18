using AccessControl.Service.Security;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using AccessControl.Contracts;
using System.Text.Json;

namespace AccessControl.Tests;

public sealed class ApiKeyMiddlewareTests
{
    [Theory]
    [InlineData("/v1/decisions", "decision", 200)]
    [InlineData("/v1/decisions", "admin", 401)]
    [InlineData("/v1/policies", "admin", 200)]
    [InlineData("/v1/policies", "decision", 401)]
    public async Task InvokeAsync_UsesSeparateKeysAsync(string path, string key, int expectedStatus)
    {
        ApiKeyMiddleware sut = new(_ => Task.CompletedTask, Options.Create(new ApiKeyOptions
        {
            DecisionClients = [new DecisionClientCredential { ClientId = "test", Key = "decision",
                Modules = ["customers"], Tenants = ["default"], AllowAnyUnitOrganization = true }],
            AdminKey = "admin"
        }), NullLogger<ApiKeyMiddleware>.Instance);
        DefaultHttpContext context = new();
        context.Request.Path = path;
        context.Request.Method = path == "/v1/decisions" ? "POST" : "GET";
        context.Request.Headers[ApiKeyMiddleware.HeaderName] = key;
        if (path == "/v1/decisions")
            context.Request.Body = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(
                new DecisionRequest(null, "customers", "GET", "/customers/{id}", "default", Guid.Empty)));
        if (path == "/v1/policies" && key == "admin")
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("oid", Guid.NewGuid().ToString("D"))], "verified-actor"));

        await sut.InvokeAsync(context);

        Assert.Equal(expectedStatus, context.Response.StatusCode);
    }

    [Fact]
    public async Task HealthBypassesKeyWhileMissingKeyOnDecisionIsRejectedAsync()
    {
        int downstreamCalls = 0;
        ApiKeyMiddleware sut = new(_ => { downstreamCalls++; return Task.CompletedTask; }, Options.Create(new ApiKeyOptions
        {
            DecisionClients = [new DecisionClientCredential { ClientId = "test", Key = "decision" }], AdminKey = "admin"
        }), NullLogger<ApiKeyMiddleware>.Instance);
        DefaultHttpContext health = new();
        health.Request.Path = "/health";
        DefaultHttpContext decision = new();
        decision.Request.Path = "/v1/decisions";

        await sut.InvokeAsync(health);
        await sut.InvokeAsync(decision);

        Assert.Equal(1, downstreamCalls);
        Assert.Equal(200, health.Response.StatusCode);
        Assert.Equal(401, decision.Response.StatusCode);
    }

    [Theory]
    [InlineData("{", null, 400)]
    [InlineData("{\"module\":\"other\",\"tenantId\":\"default\",\"unitOrganizationId\":\"00000000-0000-0000-0000-000000000000\"}", null, 403)]
    [InlineData("{}", 20000, 413)]
    public async Task DecisionClientRejectsMalformedOutOfScopeAndOversizedPayloadsAsync(
        string body, int? length, int expectedStatus)
    {
        ApiKeyMiddleware middleware = new(_ => Task.CompletedTask, Options.Create(new ApiKeyOptions
        {
            DecisionClients = [new DecisionClientCredential { ClientId = "test", Key = "decision",
                Modules = ["customers"], Tenants = ["default"], AllowAnyUnitOrganization = true }],
            AdminKey = "admin"
        }), NullLogger<ApiKeyMiddleware>.Instance);
        DefaultHttpContext context = new();
        context.Request.Path = "/v1/decisions";
        context.Request.Method = "POST";
        context.Request.Headers[ApiKeyMiddleware.HeaderName] = "decision";
        context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body));
        if (length.HasValue) context.Request.ContentLength = length;

        await middleware.InvokeAsync(context);

        Assert.Equal(expectedStatus, context.Response.StatusCode);
    }
}
