using AccessControl.Contracts;

namespace AccessControl.Application;

public interface IEntitlementCatalog
{
    Task<EntitlementPageResponse> ListAsync(string tenantId, Guid? unitOrganizationId, int page, int pageSize,
        CancellationToken cancellationToken);
}
