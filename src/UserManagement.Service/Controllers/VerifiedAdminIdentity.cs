using System.Security.Claims;

namespace UserManagement.Service.Controllers;

internal static class VerifiedAdminIdentity
{
    public static string Oid(ClaimsPrincipal user, string tenantId)
    {
        if (user.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(tenantId) ||
            !Guid.TryParse(user.FindFirstValue("oid") ??
                user.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier"),
                out Guid oid) || oid == Guid.Empty)
            throw new UnauthorizedAccessException("A validated Entra administrator identity is required.");
        return oid.ToString("D");
    }
}
