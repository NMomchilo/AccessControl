using System.Text.Json;
using AdminManagement.Security;
using AccessControl.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AccessControl.Service.Security;

public sealed class AccessControlRequestMiddleware(RequestDelegate next, ILogger<AccessControlRequestMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsGet(context.Request.Method) &&
            (context.Request.Path == "/health" || context.Request.Path == "/ready"))
        {
            logger.LogDebug("Probe request accepted for {Path}; trace {TraceId}",
                context.Request.Path, context.TraceIdentifier);
            await next(context);
            return;
        }

        bool decision = HttpMethods.IsPost(context.Request.Method) &&
            context.Request.Path == "/v1/decisions";
        bool registration = HttpMethods.IsPost(context.Request.Method) &&
            context.Request.Path == "/v1/registrations/catalog";

        if (registration)
            context.User = RegistrationClientIdentity.Create("unattributed-registration");

        if (decision)
        {
            if (context.Request.ContentLength > 16 * 1024)
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                return;
            }
            DecisionRequest? request;
            try
            {
                context.Request.EnableBuffering(8192, 16 * 1024);
                request = await JsonSerializer.DeserializeAsync<DecisionRequest>(context.Request.Body,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web), context.RequestAborted);
                context.Request.Body.Position = 0;
            }
            catch (Exception exception) when (exception is JsonException or IOException)
            {
                logger.LogWarning("Malformed decision payload; trace {TraceId}", context.TraceIdentifier);
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            if (request is null)
            {
                logger.LogWarning("Empty decision payload; trace {TraceId}", context.TraceIdentifier);
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            if (!string.IsNullOrWhiteSpace(request.SubjectId))
            {
                AuthenticateResult authenticated = await context.AuthenticateAsync(
                    context.RequestServices.GetRequiredService<IOptions<DecisionEntraOptions>>().Value.AuthenticationScheme);
                string? oid = authenticated.Principal?.FindFirst("oid")?.Value ??
                    authenticated.Principal?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
                if (!authenticated.Succeeded || !Guid.TryParse(oid, out Guid verifiedOid) ||
                    !Guid.TryParse(request.SubjectId, out Guid requestedOid) || verifiedOid == Guid.Empty ||
                    requestedOid != verifiedOid)
                {
                    logger.LogWarning("Decision identity rejected; trace {TraceId}", context.TraceIdentifier);
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
                context.User = authenticated.Principal!;
            }
        }
        else if (!registration && (context.User.Identity?.IsAuthenticated != true ||
             !Guid.TryParse(context.User.FindFirst("oid")?.Value ??
                 context.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value,
                 out Guid objectId) || objectId == Guid.Empty))
        {
            logger.LogWarning("Access Control administrator request lacks a validated actor; trace {TraceId}",
                context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        logger.LogDebug("Access Control request accepted for {Method} {Path}; trace {TraceId}",
            context.Request.Method, context.Request.Path.Value, context.TraceIdentifier);
        await next(context);
    }
}
