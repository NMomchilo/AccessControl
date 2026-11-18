using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AccessControl.Tests;

public sealed class TestEntraAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.Authorization.ToString() != "Bearer test-admin-token")
            return Task.FromResult(AuthenticateResult.NoResult());
        ClaimsIdentity identity = new([new Claim("oid", "aaaaaaaa-1111-1111-1111-111111111111")], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
