using AccessControl.Contracts;
using Microsoft.Extensions.Logging;

namespace AccessControl.Application;

public sealed class PolicyCatalogService(IPolicyCatalog catalog, ILogger<PolicyCatalogService> logger)
{
    public async Task<PolicyPageResponse> SearchAsync(string tenantId, Guid? unitOrganizationId,
        string? module, string? httpMethod, int page, int pageSize, CancellationToken cancellationToken)
    {
        string normalizedTenant = Normalize(tenantId, 128, nameof(tenantId)).ToLowerInvariant();
        string? normalizedModule = module is null ? null :
            Normalize(module, 128, nameof(module)).ToLowerInvariant();
        string? normalizedMethod = httpMethod is null ? null :
            Normalize(httpMethod, 16, nameof(httpMethod)).ToUpperInvariant();
        if (page < 1 || pageSize is < 1 or > 100 || ((long)page - 1) * pageSize > int.MaxValue)
            throw new ArgumentException("Page and page size are invalid.");
        PolicyPageResponse result = await catalog.SearchAsync(normalizedTenant, unitOrganizationId,
            normalizedModule, normalizedMethod, page, pageSize, cancellationToken);
        logger.LogDebug("Listed {Count} policies for tenant {TenantId}, organization {UnitOrganizationId}, page {Page}",
            result.Items.Count, normalizedTenant, unitOrganizationId, page);
        return result;
    }

    private static string Normalize(string value, int maxLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maxLength)
            throw new ArgumentException($"{field} is required and must be at most {maxLength} characters.", field);
        return value.Trim();
    }
}
