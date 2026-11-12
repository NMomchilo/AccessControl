using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using UserManagement.Application;

namespace UserManagement.Service.Security;

public sealed class UserManagementExceptionHandler(ILogger<UserManagementExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception,
        CancellationToken cancellationToken)
    {
        int status = exception switch
        {
            ArgumentException => StatusCodes.Status400BadRequest,
            UnitOrganizationConflictException => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };
        if (status == StatusCodes.Status400BadRequest || status == StatusCodes.Status409Conflict)
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
            Title = status switch
            {
                StatusCodes.Status400BadRequest => "Invalid request",
                StatusCodes.Status409Conflict => "Organization conflict",
                _ => "Service error"
            }
        }, cancellationToken);
        return true;
    }
}
