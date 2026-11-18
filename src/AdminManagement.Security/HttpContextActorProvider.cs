using Common.Persistence;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace AdminManagement.Security;

public sealed class HttpContextActorProvider(IHttpContextAccessor accessor) : ICurrentActorProvider
{
    public string? ActorId
    {
        get
        {
            ClaimsPrincipal? user = accessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true) return null;
            string? oid = user.FindFirstValue("oid") ??
                user.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier");
            return Guid.TryParse(oid, out Guid parsed) && parsed != Guid.Empty
                ? parsed.ToString("D") : null;
        }
    }
}
