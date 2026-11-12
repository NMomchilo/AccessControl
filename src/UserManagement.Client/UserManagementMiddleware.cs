using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UserManagement.Contracts;

namespace UserManagement.Client;

public sealed class UserManagementMiddleware(RequestDelegate next, IUserManagementClient client,
    IOptions<UserManagementClientOptions> options,
    ILogger<UserManagementMiddleware> logger)
{
    private const string MappedOid = "http://schemas.microsoft.com/identity/claims/objectidentifier";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        string? objectId = context.User.FindFirstValue("oid") ?? context.User.FindFirstValue(MappedOid);
        if (!Guid.TryParse(objectId, out Guid id) || id == Guid.Empty)
        {
            logger.LogWarning("Authenticated request has no valid oid; trace {TraceId}", context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        string? delegatedScope = context.User.FindFirstValue("scp") ??
            context.User.FindFirstValue("http://schemas.microsoft.com/identity/claims/scope");
        if (string.IsNullOrWhiteSpace(delegatedScope) ||
            !delegatedScope.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains(options.Value.RequiredDelegatedScope, StringComparer.Ordinal))
        {
            logger.LogWarning("Authenticated principal has no delegated user scope; trace {TraceId}",
                context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        string tenantId = context.User.FindFirstValue(options.Value.TenantClaimType) ?? options.Value.DefaultTenantId;
        if (string.IsNullOrWhiteSpace(tenantId) || tenantId.Length > 128)
        {
            logger.LogWarning("Authenticated request has invalid tenant; trace {TraceId}", context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        ClaimsIdentity[] gatewayIdentities = context.User.Identities.Where(identity =>
            identity.IsAuthenticated && identity.AuthenticationType == options.Value.GatewayIdentityAuthenticationType)
            .ToArray();
        Claim[] idClaims = gatewayIdentities.Length == 1
            ? gatewayIdentities[0].FindAll(options.Value.UnitOrganizationIdClaimType).ToArray() : [];
        Claim[] nameClaims = gatewayIdentities.Length == 1
            ? gatewayIdentities[0].FindAll(options.Value.UnitOrganizationNameClaimType).ToArray() : [];
        string? organizationName = nameClaims.Length == 1 ? nameClaims[0].Value : null;
        if (idClaims.Length != 1 || !Guid.TryParse(idClaims[0].Value, out Guid organizationId) ||
            organizationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(organizationName) || organizationName.Trim().Length > 200)
        {
            logger.LogWarning("Authenticated request lacks valid gateway organization context; trace {TraceId}",
                context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        UserIdentityRequest request = new(tenantId, organizationId, organizationName.Trim(), id.ToString("D"),
            context.User.FindFirstValue("preferred_username") ?? context.User.FindFirstValue(ClaimTypes.Email) ??
                context.User.FindFirstValue(ClaimTypes.Upn) ?? context.User.FindFirstValue("upn") ??
                context.User.FindFirstValue("unique_name"),
            context.User.FindFirstValue("name") ?? context.User.FindFirstValue(ClaimTypes.Name));
        UserIdentityResponse resolved;
        try
        {
            resolved = await client.ResolveAsync(request, context.RequestAborted);
        }
        catch (UnitOrganizationAccessException)
        {
            logger.LogWarning("Gateway organization was rejected; trace {TraceId}", context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or
            InvalidOperationException or System.Text.Json.JsonException)
        {
            logger.LogError(exception, "User Management unavailable; trace {TraceId}", context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        if (!resolved.IsActive)
        {
            logger.LogInformation("Inactive user denied; trace {TraceId}", context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (!string.Equals(resolved.TenantId, tenantId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError("User Management returned a different tenant; trace {TraceId}", context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }
        if (resolved.UnitOrganizationId != organizationId || string.IsNullOrWhiteSpace(resolved.UnitOrganizationName))
        {
            logger.LogError("User Management returned a different organization; trace {TraceId}",
                context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        ClaimsIdentity identity = new([], UserManagementClaims.IdentityAuthenticationType, ClaimTypes.Name,
            UserManagementClaims.Role);
        identity.AddClaim(new Claim(UserManagementClaims.UserId, resolved.UserId.ToString("D")));
        identity.AddClaim(new Claim(UserManagementClaims.TenantId, resolved.TenantId));
        identity.AddClaim(new Claim(UserManagementClaims.UnitOrganizationId, resolved.UnitOrganizationId.ToString("D")));
        identity.AddClaim(new Claim(UserManagementClaims.UnitOrganizationName, resolved.UnitOrganizationName));
        foreach (UserRoleInfo role in resolved.Roles)
        {
            identity.AddClaim(new Claim(UserManagementClaims.RoleId, role.RoleId.ToString("D")));
            identity.AddClaim(new Claim(UserManagementClaims.Role, role.Code));
        }

        context.User.AddIdentity(identity);
        logger.LogDebug("Added {RoleCount} application roles; trace {TraceId}", resolved.Roles.Count, context.TraceIdentifier);
        await next(context);
    }
}
