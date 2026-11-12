using AccessControl.Contracts;
using Microsoft.Extensions.Logging;

namespace AccessControl.Application;

public sealed class EntitlementCatalogService(IEntitlementCatalog catalog, ILogger<EntitlementCatalogService> logger)
{
    public async Task<EntitlementPageResponse> ListAsync(string tenantId, Guid? unitOrganizationId, int page,
        int pageSize, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || tenantId.Trim().Length > 128)
            throw new ArgumentException("Tenant ID is required and must be at most 128 characters.", nameof(tenantId));
        if (unitOrganizationId == Guid.Empty)
            throw new ArgumentException("Unit organization ID must be nonempty.", nameof(unitOrganizationId));
        if (page < 1 || pageSize is < 1 or > 100 || ((long)page - 1) * pageSize > int.MaxValue)
            throw new ArgumentException("Page must be positive, page size must be 1 through 100, and the offset must fit an integer.");

        string normalizedTenant = tenantId.Trim().ToLowerInvariant();
        EntitlementPageResponse result = await catalog.ListAsync(normalizedTenant, unitOrganizationId, page,
            pageSize, cancellationToken);
        logger.LogInformation("Listed {Count} of {TotalCount} referenced entitlements for tenant {TenantId}, organization {UnitOrganizationId}, page {Page}",
            result.Items.Count, result.TotalCount, normalizedTenant, unitOrganizationId, page);
        return result;
    }
}
