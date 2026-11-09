using AccessControl.Service.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
            DecisionKey = "decision",
            AdminKey = "admin"
        }), NullLogger<ApiKeyMiddleware>.Instance);
        DefaultHttpContext context = new();
        context.Request.Path = path;
        context.Request.Headers[ApiKeyMiddleware.HeaderName] = key;

        await sut.InvokeAsync(context);

        Assert.Equal(expectedStatus, context.Response.StatusCode);
    }
}
