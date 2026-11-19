using System.Security.Claims;

namespace AdminManagement.Security;

public static class RegistrationClientIdentity
{
    public const string AuthenticationType = "StartupRegistration";
    public const string ClientIdClaimType = "service_client_id";

    public static ClaimsPrincipal Create(string clientId) => new(new ClaimsIdentity(
        [new Claim(ClientIdClaimType, clientId)], AuthenticationType));
}
