using AccessControl.Contracts;

namespace AccessControl.Application;

public interface IEntitlementCatalog
{
    Task<EntitlementInfo> CreateAsync(string tenantId, Guid unitOrganizationId, string code,
        string displayName, CancellationToken cancellationToken);
    Task<bool> SetActiveAsync(string tenantId, Guid unitOrganizationId, Guid entitlementId,
        bool isActive, CancellationToken cancellationToken);
    Task<EntitlementInfo?> UpdateDisplayNameAsync(string tenantId, Guid unitOrganizationId, Guid entitlementId,
        string displayName, CancellationToken cancellationToken);
    Task<EntitlementPageResponse> ListAsync(string tenantId, Guid? unitOrganizationId, int page, int pageSize,
        CancellationToken cancellationToken);
    Task<EntitlementInfo?> GetAsync(string tenantId, Guid unitOrganizationId, Guid entitlementId,
        CancellationToken cancellationToken);
}
