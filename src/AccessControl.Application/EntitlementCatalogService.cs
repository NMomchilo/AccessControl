using AccessControl.Contracts;
using Microsoft.Extensions.Logging;

namespace AccessControl.Application;

public sealed class EntitlementCatalogService(IEntitlementCatalog catalog, ILogger<EntitlementCatalogService> logger)
{
    public async Task<EntitlementInfo> CreateAsync(CreateEntitlementRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string tenantId = NormalizeTenant(request.TenantId);
        if (request.UnitOrganizationId == Guid.Empty)
            throw new ArgumentException("Unit organization ID is required.", nameof(request));
        string code = NormalizeText(request.Code, 128, nameof(request.Code)).ToLowerInvariant();
        string displayName = NormalizeText(request.DisplayName, 200, nameof(request.DisplayName));
        EntitlementInfo created = await catalog.CreateAsync(tenantId, request.UnitOrganizationId, code,
            displayName, cancellationToken);
        logger.LogInformation("Created entitlement {EntitlementId} in tenant {TenantId}, organization {UnitOrganizationId}",
            created.Id, tenantId, request.UnitOrganizationId);
        return created;
    }

    public async Task<bool> SetActiveAsync(string tenantId, Guid unitOrganizationId, Guid entitlementId,
        bool isActive, CancellationToken cancellationToken)
    {
        string normalizedTenant = NormalizeTenant(tenantId);
        if (unitOrganizationId == Guid.Empty || entitlementId == Guid.Empty)
            throw new ArgumentException("Unit organization and entitlement IDs are required.");
        bool found = await catalog.SetActiveAsync(normalizedTenant, unitOrganizationId, entitlementId,
            isActive, cancellationToken);
        logger.LogInformation("Entitlement {EntitlementId} active status set to {IsActive}; found {Found}",
            entitlementId, isActive, found);
        return found;
    }

    public async Task<EntitlementInfo?> UpdateDisplayNameAsync(string tenantId, Guid unitOrganizationId,
        Guid entitlementId, string displayName, CancellationToken cancellationToken)
    {
        string normalizedTenant = NormalizeTenant(tenantId);
        if (unitOrganizationId == Guid.Empty || entitlementId == Guid.Empty)
            throw new ArgumentException("Unit organization and entitlement IDs are required.");
        string normalizedName = NormalizeText(displayName, 200, nameof(displayName));
        EntitlementInfo? updated = await catalog.UpdateDisplayNameAsync(normalizedTenant, unitOrganizationId,
            entitlementId, normalizedName, cancellationToken);
        logger.LogInformation("Entitlement {EntitlementId} display name updated; found {Found}", entitlementId,
            updated is not null);
        return updated;
    }

    public async Task<EntitlementPageResponse> ListAsync(string tenantId, Guid? unitOrganizationId, int page,
        int pageSize, CancellationToken cancellationToken)
    {
        string normalizedTenant = NormalizeTenant(tenantId);
        if (unitOrganizationId == Guid.Empty)
            throw new ArgumentException("Unit organization ID must be nonempty.", nameof(unitOrganizationId));
        if (page < 1 || pageSize is < 1 or > 100 || ((long)page - 1) * pageSize > int.MaxValue)
            throw new ArgumentException("Page must be positive, page size must be 1 through 100, and the offset must fit an integer.");

        EntitlementPageResponse result = await catalog.ListAsync(normalizedTenant, unitOrganizationId, page,
            pageSize, cancellationToken);
        logger.LogInformation("Listed {Count} of {TotalCount} entitlements for tenant {TenantId}, organization {UnitOrganizationId}, page {Page}",
            result.Items.Count, result.TotalCount, normalizedTenant, unitOrganizationId, page);
        return result;
    }

    public async Task<EntitlementInfo?> GetAsync(string tenantId, Guid unitOrganizationId,
        Guid entitlementId, CancellationToken cancellationToken)
    {
        string normalizedTenant = NormalizeTenant(tenantId);
        if (unitOrganizationId == Guid.Empty || entitlementId == Guid.Empty)
            throw new ArgumentException("Unit organization and entitlement IDs are required.");
        EntitlementInfo? entitlement = await catalog.GetAsync(normalizedTenant, unitOrganizationId,
            entitlementId, cancellationToken);
        logger.LogDebug("Looked up entitlement {EntitlementId}; found {Found}", entitlementId,
            entitlement is not null);
        return entitlement;
    }

    private static string NormalizeTenant(string value) => NormalizeText(value, 128, nameof(value)).ToLowerInvariant();

    private static string NormalizeText(string value, int maximumLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximumLength)
            throw new ArgumentException($"{field} is required and must be at most {maximumLength} characters.", field);
        return value.Trim();
    }
}
