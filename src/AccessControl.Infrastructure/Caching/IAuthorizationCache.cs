using AccessControl.Domain;

namespace AccessControl.Infrastructure.Caching;

public interface IAuthorizationCache
{
    Task<EndpointPolicy?> GetPolicyAsync(string tenantId, Guid unitOrganizationId, string httpMethod,
        string routeTemplate, Func<CancellationToken, Task<EndpointPolicy?>> load, CancellationToken cancellationToken);
    Task<IReadOnlySet<Guid>> GetGrantsAsync(string tenantId, Guid unitOrganizationId,
        IReadOnlyList<Guid> roleIds, Func<CancellationToken, Task<IReadOnlySet<Guid>>> load,
        CancellationToken cancellationToken);
    void InvalidateAll(string reason);
    void ObserveRevision(long revision);
}
