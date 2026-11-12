using AccessControl.Application;
using AccessControl.Domain;
using Microsoft.Extensions.Logging;

namespace AccessControl.Infrastructure.Caching;

public sealed class CachingAuthorizationStore(IAuthorizationStore source, AuthorizationCache cache,
    ILogger<CachingAuthorizationStore> logger) : IAuthorizationStore
{
    public Task<EndpointPolicy?> GetPolicyAsync(string tenantId, Guid unitOrganizationId, string module, string httpMethod, string routeTemplate,
        CancellationToken cancellationToken) => cache.GetPolicyAsync(tenantId, unitOrganizationId, module, httpMethod, routeTemplate,
            token => source.GetPolicyAsync(tenantId, unitOrganizationId, module, httpMethod, routeTemplate, token), cancellationToken);

    public Task<IReadOnlySet<string>> GetEntitlementsAsync(string tenantId, Guid unitOrganizationId, IReadOnlyList<Guid> roleIds,
        CancellationToken cancellationToken)
    {
        Guid[] normalizedRoleIds = roleIds.Distinct().Order().ToArray();
        return normalizedRoleIds.Length == 0
            ? source.GetEntitlementsAsync(tenantId, unitOrganizationId, normalizedRoleIds, cancellationToken)
            : cache.GetGrantsAsync(tenantId, unitOrganizationId, normalizedRoleIds,
                token => source.GetEntitlementsAsync(tenantId, unitOrganizationId, normalizedRoleIds, token), cancellationToken);
    }

    public async Task UpsertPolicyAsync(EndpointPolicy policy, CancellationToken cancellationToken)
    {
        await source.UpsertPolicyAsync(policy, cancellationToken);
        cache.InvalidateAll("policy upsert");
        logger.LogDebug("Local cache cleared after policy upsert");
    }

    public async Task DeletePolicyAsync(string tenantId, Guid unitOrganizationId, string module, string httpMethod, string routeTemplate,
        CancellationToken cancellationToken)
    {
        await source.DeletePolicyAsync(tenantId, unitOrganizationId, module, httpMethod, routeTemplate, cancellationToken);
        cache.InvalidateAll("policy delete");
        logger.LogDebug("Local cache cleared after policy delete");
    }

    public async Task ReplaceRoleEntitlementsAsync(string tenantId, Guid unitOrganizationId, Guid roleId, IReadOnlyList<string> entitlements,
        CancellationToken cancellationToken)
    {
        await source.ReplaceRoleEntitlementsAsync(tenantId, unitOrganizationId, roleId, entitlements, cancellationToken);
        cache.InvalidateAll("role entitlements changed");
        logger.LogDebug("Local cache cleared after role entitlement change");
    }
}
