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
    [Fact]
    public async Task DecisionClientAcceptsTenantProvidedByApiAsync()
    {
        bool reached = false;
        ApiKeyMiddleware middleware = new(_ => { reached = true; return Task.CompletedTask; },
            Options.Create(new ApiKeyOptions
            {
                DecisionClients = [new DecisionClientCredential
                {
                    ClientId = "customer", Key = "decision"
                }], AdminKey = "admin"
            }), NullLogger<ApiKeyMiddleware>.Instance);
        DefaultHttpContext context = new();
        context.Request.Path = "/v1/decisions";
        context.Request.Method = "POST";
        context.Request.Headers[ApiKeyMiddleware.HeaderName] = "decision";
        context.Request.Body = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(
            new DecisionRequest(null, "GET", "/payments", "another-tenant", Guid.NewGuid())));

        await middleware.InvokeAsync(context);

        Assert.True(reached);
    }
    [Fact]
    public async Task ExistingDecisionClientKeyCanRegisterOnlyWhenOptedInAsync()
    {
        bool reached = false;
        ApiKeyMiddleware middleware = new(_ => { reached = true; return Task.CompletedTask; },
            Options.Create(new ApiKeyOptions
            {
                DecisionClients = [new DecisionClientCredential
                {
                    ClientId = "customer", Key = "decision",
                    CanRegisterCatalog = true
                }], AdminKey = "admin"
            }), NullLogger<ApiKeyMiddleware>.Instance);
        DefaultHttpContext context = new();
        context.Request.Path = "/v1/registrations/catalog";
        context.Request.Method = "POST";
        context.Request.Headers[ApiKeyMiddleware.HeaderName] = "decision";

        await middleware.InvokeAsync(context);

        Assert.True(reached);
        Assert.Equal("customer", context.User.FindFirst("service_client_id")?.Value);
        Assert.IsType<DecisionClientCredential>(context.Items[nameof(DecisionClientCredential)]);
    }
    [Theory]
    [InlineData("/v1/decisions", "decision", 200)]
    [InlineData("/v1/decisions", "admin", 401)]
    [InlineData("/v1/policies", "admin", 200)]
    [InlineData("/v1/policies", "decision", 401)]
    public async Task InvokeAsync_UsesSeparateKeysAsync(string path, string key, int expectedStatus)
    {
        ApiKeyMiddleware sut = new(_ => Task.CompletedTask, Options.Create(new ApiKeyOptions
        {
            DecisionClients = [new DecisionClientCredential { ClientId = "test", Key = "decision" }],
            AdminKey = "admin"
        }), NullLogger<ApiKeyMiddleware>.Instance);
        DefaultHttpContext context = new();
        context.Request.Path = path;
        context.Request.Method = path == "/v1/decisions" ? "POST" : "GET";
        context.Request.Headers[ApiKeyMiddleware.HeaderName] = key;
        if (path == "/v1/decisions")
            context.Request.Body = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(
                new DecisionRequest(null, "GET", "/customers/{id}", "default", Guid.Empty)));
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
        health.Request.Method = "GET";
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
    [InlineData("GET", "/health", 200)]
    [InlineData("GET", "/ready", 200)]
    [InlineData("POST", "/health", 401)]
    [InlineData("POST", "/ready", 401)]
    public async Task OnlyGetHealthAndReadinessBypassKeyAsync(string method, string path, int expectedStatus)
    {
        int downstreamCalls = 0;
        ApiKeyMiddleware sut = new(_ => { downstreamCalls++; return Task.CompletedTask; }, Options.Create(new ApiKeyOptions
        {
            DecisionClients = [new DecisionClientCredential { ClientId = "test", Key = "decision" }], AdminKey = "admin"
        }), NullLogger<ApiKeyMiddleware>.Instance);
        DefaultHttpContext context = new();
        context.Request.Method = method;
        context.Request.Path = path;

        await sut.InvokeAsync(context);

        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.Equal(expectedStatus == 200 ? 1 : 0, downstreamCalls);
    }

    [Theory]
    [InlineData("{", null, 400)]
    [InlineData("null", null, 400)]
    [InlineData("{}", 20000, 413)]
    public async Task DecisionClientRejectsMalformedAndOversizedPayloadsAsync(
        string body, int? length, int expectedStatus)
    {
        ApiKeyMiddleware middleware = new(_ => Task.CompletedTask, Options.Create(new ApiKeyOptions
        {
            DecisionClients = [new DecisionClientCredential { ClientId = "test", Key = "decision" }],
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
