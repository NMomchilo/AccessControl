using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace AdminManagement.Security;

public sealed class IdentityService(IHttpContextAccessor accessor, IAdminDutyReader duties) : IIdentityService
{
    private ClaimsPrincipal? VerifiedUser => accessor.HttpContext?.User.Identity?.IsAuthenticated == true
        ? accessor.HttpContext.User : null;

    public string? GetUserEmail() => VerifiedUser?.FindFirstValue("preferred_username") ??
        VerifiedUser?.FindFirstValue(ClaimTypes.Email) ?? VerifiedUser?.FindFirstValue("upn");

    public Guid? GetUserId() => Parse(VerifiedUser?.FindFirstValue("oid") ??
        VerifiedUser?.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier"));

    public Guid? GetClientId() => Parse(VerifiedUser?.FindFirstValue("azp") ??
        VerifiedUser?.FindFirstValue("appid"));

    // Only an explicitly typed app-only token counts. A service principal may also have an oid.
    public bool IsInternalServiceCall() => VerifiedUser?.FindFirstValue("idtyp") == "app" &&
        GetClientId() is not null;

    public async Task<bool> CheckIsSuperAdminAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        Guid? oid = GetUserId();
        return oid.HasValue && !string.IsNullOrWhiteSpace(tenantId) &&
            await duties.IsSuperAdminAsync(tenantId, oid.Value.ToString("D"), cancellationToken);
    }

    private static Guid? Parse(string? value) => Guid.TryParse(value, out Guid result) && result != Guid.Empty
        ? result : null;
}
