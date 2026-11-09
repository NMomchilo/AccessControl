using System.Security.Claims;
using AccessControl.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AccessControl.Client;

public sealed class AccessControlMiddleware(
    RequestDelegate next,
    IAccessControlDecisionClient decisionClient,
    IOptions<AccessControlClientOptions> options,
    ILogger<AccessControlMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        RouteEndpoint? endpoint = context.GetEndpoint() as RouteEndpoint;
        if (context.GetEndpoint() is null)
        {
            logger.LogDebug("No matched endpoint; passing to downstream 404 handler; trace {TraceId}", context.TraceIdentifier);
            await next(context);
            return;
        }

        string? rawRoute = endpoint?.RoutePattern.RawText;
        if (string.IsNullOrWhiteSpace(rawRoute))
        {
            logger.LogWarning("Matched endpoint has no route template; trace {TraceId}", context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        string route = EndpointKey.NormalizeRoute(rawRoute);
        string method = EndpointKey.NormalizeMethod(context.Request.Method);
        AccessControlClientOptions settings = options.Value;
        if (settings.ExcludedPaths.Any(path => string.Equals(EndpointKey.NormalizeRoute(path), route, StringComparison.Ordinal)))
        {
            logger.LogDebug("Locally excluded endpoint {Method} {Route}; trace {TraceId}", method, route, context.TraceIdentifier);
            await next(context);
            return;
        }

        bool authenticated = context.User.Identity?.IsAuthenticated == true;
        string? subjectId = authenticated
            ? context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub")
            : null;
        if (authenticated && string.IsNullOrWhiteSpace(subjectId))
        {
            logger.LogWarning("Authenticated request has no subject claim for {Method} {Route}; trace {TraceId}", method, route, context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        string tenantId = (authenticated ? context.User.FindFirstValue(settings.TenantClaimType) : null)
            ?? settings.DefaultTenantId;
        string[] roles = authenticated
            ? context.User.Claims.Where(claim => claim.Type == ClaimTypes.Role || claim.Type == "role")
                .Select(claim => claim.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : [];
        DecisionRequest request = new(subjectId, roles, settings.Module, method, route, tenantId);
        logger.LogDebug("Requesting decision for {TenantId} {Module} {Method} {Route}; authenticated {Authenticated}, roles {RoleCount}, trace {TraceId}",
            tenantId, settings.Module, method, route, authenticated, roles.Length, context.TraceIdentifier);

        DecisionResponse decision;
        try
        {
            decision = await decisionClient.DecideAsync(request, context.RequestAborted);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException or System.Text.Json.JsonException)
        {
            logger.LogError(exception, "Decision service unavailable for {Module} {Method} {Route}; trace {TraceId}",
                settings.Module, method, route, context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        if (decision.Permit)
        {
            logger.LogInformation("Access permitted for {Module} {Method} {Route}; trace {TraceId}", settings.Module, method, route, context.TraceIdentifier);
            await next(context);
            return;
        }

        context.Response.StatusCode = decision.AuthenticationRequired
            ? StatusCodes.Status401Unauthorized : StatusCodes.Status403Forbidden;
        logger.LogInformation("Access denied with status {StatusCode} for {Module} {Method} {Route}; trace {TraceId}",
            context.Response.StatusCode, settings.Module, method, route, context.TraceIdentifier);
    }
}
