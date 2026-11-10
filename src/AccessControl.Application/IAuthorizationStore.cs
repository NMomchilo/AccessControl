using AccessControl.Domain;

namespace AccessControl.Application;

public interface IAuthorizationStore
{
    Task<EndpointPolicy?> GetPolicyAsync(string tenantId, string module, string httpMethod, string routeTemplate, CancellationToken cancellationToken);
    Task<IReadOnlySet<string>> GetEntitlementsAsync(string tenantId, IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken);
    Task UpsertPolicyAsync(EndpointPolicy policy, CancellationToken cancellationToken);
    Task DeletePolicyAsync(string tenantId, string module, string httpMethod, string routeTemplate, CancellationToken cancellationToken);
    Task ReplaceRoleEntitlementsAsync(string tenantId, Guid roleId, IReadOnlyList<string> entitlements, CancellationToken cancellationToken);
}
