using System.Net;
using AccessControl.Service.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessControl.Tests;

public sealed class AccessControlApiExceptionHandlerTests
{
    [Theory]
    [InlineData(true, HttpStatusCode.BadRequest, "Invalid request")]
    [InlineData(false, HttpStatusCode.InternalServerError, "Service error")]
    public async Task TryHandleAsync_UsesSafeProblemResponseAsync(bool invalidRequest, HttpStatusCode expectedStatus,
        string expectedTitle)
    {
        DefaultHttpContext context = new();
        context.Response.Body = new MemoryStream();
        ApiExceptionHandler handler = new(NullLogger<ApiExceptionHandler>.Instance);
        Exception failure = invalidRequest ? new ArgumentException("sensitive argument") :
            new InvalidOperationException("sensitive database detail");

        bool handled = await handler.TryHandleAsync(context, failure, CancellationToken.None);
        context.Response.Body.Position = 0;
        ProblemDetails? result = await System.Text.Json.JsonSerializer.DeserializeAsync<ProblemDetails>(
            context.Response.Body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.True(handled);
        Assert.Equal((int)expectedStatus, context.Response.StatusCode);
        Assert.Equal(expectedTitle, result?.Title);
        context.Response.Body.Position = 0;
        Assert.DoesNotContain("sensitive", await new StreamReader(context.Response.Body).ReadToEndAsync());
    }
}
