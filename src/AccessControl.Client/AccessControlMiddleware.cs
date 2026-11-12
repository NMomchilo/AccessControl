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
            ? context.User.FindFirstValue("oid") ??
                context.User.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier")
            : null;
        if (authenticated && string.IsNullOrWhiteSpace(subjectId))
        {
            logger.LogWarning("Authenticated request has no oid claim for {Method} {Route}; trace {TraceId}", method, route, context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        string tenantId = (authenticated ? context.User.FindFirstValue(settings.TenantClaimType) : null)
            ?? settings.DefaultTenantId;
        Guid unitOrganizationId = Guid.Empty;
        ClaimsIdentity[] managedIdentities = context.User.Identities
            .Where(identity => identity.AuthenticationType == settings.RoleIdentityAuthenticationType).ToArray();
        if (authenticated)
        {
            string[] organizationClaims = managedIdentities.SelectMany(identity => identity.Claims)
                .Where(claim => claim.Type == "user_management_unit_organization_id")
                .Select(claim => claim.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (organizationClaims.Length != 1 ||
                !Guid.TryParse(organizationClaims[0], out unitOrganizationId) || unitOrganizationId == Guid.Empty ||
                managedIdentities.Any(identity => !string.Equals(identity.FindFirst("user_management_tenant_id")?.Value,
                    tenantId.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                logger.LogWarning("Missing or inconsistent unit organization/tenant from User Management; trace {TraceId}", context.TraceIdentifier);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }
        string[] roleClaims = authenticated
            ? managedIdentities
                .SelectMany(identity => identity.Claims)
                .Where(claim => claim.Type == settings.RoleIdClaimType)
                .Select(claim => claim.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : [];
        if (roleClaims.Length > 16 || roleClaims.Any(value => !Guid.TryParse(value, out Guid id) || id == Guid.Empty))
        {
            logger.LogWarning("Invalid application role IDs for {Method} {Route}; trace {TraceId}", method, route, context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        Guid[] roleIds = roleClaims.Select(Guid.Parse).Distinct().ToArray();
        DecisionRequest request = new(subjectId, roleIds, settings.Module, method, route, tenantId, unitOrganizationId);
        logger.LogDebug("Requesting decision for {TenantId} {Module} {Method} {Route}; authenticated {Authenticated}, roles {RoleCount}, trace {TraceId}",
            tenantId, settings.Module, method, route, authenticated, roleIds.Length, context.TraceIdentifier);

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
