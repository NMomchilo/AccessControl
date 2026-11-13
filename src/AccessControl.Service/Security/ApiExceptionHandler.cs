using AccessControl.Application;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace AccessControl.Service.Security;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        int status = exception switch
        {
            EntitlementConflictException => StatusCodes.Status409Conflict,
            ArgumentException => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };
        if (status is StatusCodes.Status400BadRequest or StatusCodes.Status409Conflict)
        {
            logger.LogWarning(exception, "Invalid Access Control API request; trace {TraceId}", httpContext.TraceIdentifier);
        }
        else
        {
            logger.LogError(exception, "Access Control API request failed; trace {TraceId}", httpContext.TraceIdentifier);
        }
        httpContext.Response.StatusCode = status;
        ProblemDetails problem = new()
        {
            Status = status,
            Title = status switch
            {
                StatusCodes.Status400BadRequest => "Invalid request",
                StatusCodes.Status409Conflict => "Entitlement conflict",
                _ => "Service error"
            }
        };
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
