using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using UserManagement.Contracts;

namespace UserManagement.Client;

public sealed class UserManagementMiddleware(RequestDelegate next, IUserManagementClient client,
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
        if (string.IsNullOrWhiteSpace(delegatedScope))
        {
            logger.LogWarning("Authenticated principal has no delegated user scope; trace {TraceId}",
                context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        UserIdentityRequest request = new(id.ToString("D"),
            context.User.FindFirstValue("preferred_username") ?? context.User.FindFirstValue(ClaimTypes.Email) ??
                context.User.FindFirstValue(ClaimTypes.Upn) ?? context.User.FindFirstValue("upn") ??
                context.User.FindFirstValue("unique_name"),
            context.User.FindFirstValue("name") ?? context.User.FindFirstValue(ClaimTypes.Name));
        UserIdentityResponse resolved;
        try
        {
            resolved = await client.ResolveAsync(request, context.RequestAborted);
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

        ClaimsIdentity identity = new(UserManagementClaims.IdentityAuthenticationType);
        identity.AddClaim(new Claim(UserManagementClaims.UserId, resolved.UserId.ToString("D")));
        foreach (string role in resolved.Roles)
        {
            identity.AddClaim(new Claim(UserManagementClaims.Role, role));
        }

        context.User.AddIdentity(identity);
        logger.LogDebug("Added {RoleCount} application roles; trace {TraceId}", resolved.Roles.Count, context.TraceIdentifier);
        await next(context);
    }
}
