using System.Security.Claims;
using AdminManagement.Security;
using Microsoft.AspNetCore.Http;

namespace AccessControl.Tests;

public sealed class IdentityServiceTests
{
    [Fact]
    public async Task ReadsOnlyVerifiedClaimsAndChecksCurrentSuperAdminDutyAsync()
    {
        string oid = Guid.NewGuid().ToString("D");
        string appid = Guid.NewGuid().ToString("D");
        DefaultHttpContext context = new();
        HttpContextAccessor accessor = new() { HttpContext = context };
        RecordingDutyReader reader = new();
        IdentityService service = new(accessor, reader);

        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("oid", oid), new Claim("appid", appid),
                new Claim("preferred_username", "admin@example.test"), new Claim("idtyp", "app")]));
        Assert.Null(service.GetUserId());
        Assert.Null(service.GetUserEmail());
        Assert.Null(service.GetClientId());
        Assert.False(service.IsInternalServiceCall());
        Assert.False(await service.CheckIsSuperAdminAsync("default"));
        Assert.Equal(0, reader.Calls);

        context.User = new ClaimsPrincipal(new ClaimsIdentity(context.User.Claims, "verified"));
        Assert.Equal(Guid.Parse(oid), service.GetUserId());
        Assert.Equal(Guid.Parse(appid), service.GetClientId());
        Assert.Equal("admin@example.test", service.GetUserEmail());
        Assert.True(service.IsInternalServiceCall());
        Assert.True(await service.CheckIsSuperAdminAsync("default"));
        Assert.Equal(("default", oid), reader.LastRequest);
        Assert.False(await service.CheckIsSuperAdminAsync(" "));
        Assert.Equal(1, reader.Calls);
    }

    [Fact]
    public void ActorProviderUsesVerifiedOidOnlyAsync()
    {
        DefaultHttpContext context = new();
        HttpContextAccessor accessor = new() { HttpContext = context };
        HttpContextActorProvider provider = new(accessor);
        string oid = Guid.NewGuid().ToString("D");
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", oid)]));
        Assert.Null(provider.ActorId);
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", oid)], "verified"));
        Assert.Equal(oid, provider.ActorId);
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", "invalid")], "verified"));
        Assert.Null(provider.ActorId);
    }

    private sealed class RecordingDutyReader : IAdminDutyReader
    {
        public int Calls { get; private set; }
        public (string Tenant, string ObjectId) LastRequest { get; private set; }
        public Task<bool> IsSuperAdminAsync(string tenantId, string objectId, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = (tenantId, objectId);
            return Task.FromResult(true);
        }
    }
}
