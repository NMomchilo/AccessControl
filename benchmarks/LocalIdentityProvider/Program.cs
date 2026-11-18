using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

// Local benchmark authority only. It binds to loopback and generates a fresh signing key on each start.
const string issuer = "https://localhost:7250";
const string audience = "api://local-flow-test";
using RSA rsa = RSA.Create(2048);
RsaSecurityKey key = new(rsa) { KeyId = Guid.NewGuid().ToString("N") };
RSAParameters publicKey = rsa.ExportParameters(false);

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(issuer);
WebApplication app = builder.Build();
if (!app.Environment.IsDevelopment())
    throw new InvalidOperationException("The local test identity provider runs only in Development.");
app.MapGet("/.well-known/openid-configuration", () => Results.Json(new
{
    issuer,
    jwks_uri = issuer + "/.well-known/jwks.json",
    authorization_endpoint = issuer + "/authorize",
    response_types_supported = new[] { "token" },
    subject_types_supported = new[] { "public" },
    id_token_signing_alg_values_supported = new[] { SecurityAlgorithms.RsaSha256 }
}));
app.MapGet("/.well-known/jwks.json", () => Results.Json(new
{
    keys = new[] { new
    {
        kty = "RSA", use = "sig", kid = key.KeyId, alg = "RS256",
        n = Base64UrlEncoder.Encode(publicKey.Modulus!),
        e = Base64UrlEncoder.Encode(publicKey.Exponent!)
    } }
}));
app.MapGet("/token", (Guid oid) =>
{
    if (oid == Guid.Empty) return Results.BadRequest();
    JwtSecurityToken token = new(issuer, audience,
        [new Claim("oid", oid.ToString("D")), new Claim("scp", "user_impersonation")],
        DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(30),
        new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
    return Results.Json(new { access_token = new JwtSecurityTokenHandler().WriteToken(token) });
});
await app.RunAsync();
