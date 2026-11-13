using AccessControl.Domain;

namespace AccessControl.Application;

public interface IAuthorizationStore
{
    Task<EndpointPolicy?> GetPolicyAsync(string tenantId, Guid unitOrganizationId, string module, string httpMethod, string routeTemplate, CancellationToken cancellationToken);
    Task<IReadOnlySet<Guid>> GetEntitlementsAsync(string tenantId, Guid unitOrganizationId, IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> GetAssignedEntitlementIdsAsync(string tenantId, Guid unitOrganizationId, Guid roleId, CancellationToken cancellationToken);
    Task UpsertPolicyAsync(EndpointPolicy policy, CancellationToken cancellationToken);
    Task DeletePolicyAsync(string tenantId, Guid unitOrganizationId, string module, string httpMethod, string routeTemplate, CancellationToken cancellationToken);
    Task ReplaceRoleEntitlementsAsync(string tenantId, Guid unitOrganizationId, Guid roleId, IReadOnlyList<Guid> entitlementIds, CancellationToken cancellationToken);
}
