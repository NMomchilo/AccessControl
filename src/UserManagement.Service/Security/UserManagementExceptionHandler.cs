using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace UserManagement.Service.Security;

public sealed class UserManagementExceptionHandler(ILogger<UserManagementExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception,
        CancellationToken cancellationToken)
    {
        int status = exception is ArgumentException
            ? StatusCodes.Status400BadRequest : StatusCodes.Status500InternalServerError;
        if (status == StatusCodes.Status400BadRequest)
        {
            logger.LogWarning("Invalid User Management request; trace {TraceId}", context.TraceIdentifier);
        }
        else
        {
            logger.LogError(exception, "User Management request failed; trace {TraceId}", context.TraceIdentifier);
        }

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = status == StatusCodes.Status400BadRequest ? "Invalid request" : "Service error"
        }, cancellationToken);
        return true;
    }
}
