using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AccessControl.Application;
using Microsoft.Extensions.Options;

namespace AccessControl.Service.Security;

public sealed class UserManagementAdminScopeAuthorizer(HttpClient client, IHttpContextAccessor contextAccessor,
    IOptions<UserManagementAdminLookupOptions> options, ILogger<UserManagementAdminScopeAuthorizer> logger)
    : IAdminScopeAuthorizer
{
    public async Task<bool> CanManageAsync(string actorObjectId, string tenantId, Guid? unitOrganizationId,
        bool requireSuperAdmin, CancellationToken cancellationToken)
    {
        HttpContext? context = contextAccessor.HttpContext;
        if (context?.User.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(actorObjectId, out Guid objectId) || objectId == Guid.Empty ||
            !string.Equals(context.User.FindFirst("oid")?.Value ??
                context.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value,
                objectId.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(tenantId))
            return false;

        string authorization = context.Request.Headers.Authorization.ToString();
        if (!AuthenticationHeaderValue.TryParse(authorization, out AuthenticationHeaderValue? bearer) ||
            !string.Equals(bearer.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(bearer.Parameter))
            return false;

        Guid? lookupOrganization = unitOrganizationId == Guid.Empty ? null : unitOrganizationId;
        string path = $"/v1/admin/access?tenantId={Uri.EscapeDataString(tenantId)}" +
            (lookupOrganization.HasValue ? $"&unitOrganizationId={lookupOrganization.Value:D}" : string.Empty);
        using HttpRequestMessage request = new(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer.Parameter);
        request.Headers.Add("X-User-Management-Key", options.Value.LookupKey);
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(exception, "User Management admin access lookup failed for tenant {TenantId}", tenantId);
            throw new AdminLookupUnavailableException("User Management administrator lookup is unavailable.");
        }
        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                return false;
            if (!response.IsSuccessStatusCode)
                throw new AdminLookupUnavailableException("User Management administrator lookup failed.");
            UserManagementAdminAccessResponse? access = await response.Content.ReadFromJsonAsync<UserManagementAdminAccessResponse>(cancellationToken);
            if (access is null || !access.IsActive) return false;
            bool permitted = requireSuperAdmin || !lookupOrganization.HasValue
                ? access.IsSuperAdmin
                : access.IsSuperAdmin || access.CanManage &&
                    access.ManagedUnitOrganizationIds.Contains(lookupOrganization.Value);
            logger.LogDebug("Admin scope lookup for actor {ObjectId} in tenant {TenantId}, organization {UnitOrganizationId}: {Permitted}",
                objectId, tenantId, unitOrganizationId, permitted);
            return permitted;
        }
    }
}
