using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using UserManagement.Service.Security;

namespace AccessControl.Tests;

public sealed class UserManagementExceptionHandlerTests
{
    [Fact]
    public async Task UnexpectedStoreFailure_Returns500WithoutExceptionDetailsAsync()
    {
        DefaultHttpContext context = new();
        await using MemoryStream body = new();
        context.Response.Body = body;
        UserManagementExceptionHandler sut = new(NullLogger<UserManagementExceptionHandler>.Instance);

        bool handled = await sut.TryHandleAsync(context,
            new InvalidOperationException("sensitive database details"), CancellationToken.None);

        body.Position = 0;
        string response = await new StreamReader(body).ReadToEndAsync();
        Assert.True(handled);
        Assert.Equal(500, context.Response.StatusCode);
        Assert.Contains("Service error", response);
        Assert.DoesNotContain("sensitive database details", response);
    }
}
