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
            ClaimsIdentity[] serviceIdentities = user.Identities.Where(identity =>
                identity.IsAuthenticated && identity.AuthenticationType == RegistrationClientIdentity.AuthenticationType)
                .ToArray();
            if (serviceIdentities.Length > 0)
            {
                if (serviceIdentities.Length != 1) return null;
                Claim[] ids = serviceIdentities[0].FindAll(RegistrationClientIdentity.ClientIdClaimType).ToArray();
                return ids.Length == 1 && !string.IsNullOrWhiteSpace(ids[0].Value) && ids[0].Value.Length <= 200
                    ? ids[0].Value : null;
            }
            string? oid = user.FindFirstValue("oid") ??
                user.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier");
            return Guid.TryParse(oid, out Guid parsed) && parsed != Guid.Empty
                ? parsed.ToString("D") : null;
        }
    }
}
