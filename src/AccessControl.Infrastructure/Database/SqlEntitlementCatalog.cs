using AccessControl.Application;
using AccessControl.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AccessControl.Infrastructure.Database;

public sealed class SqlEntitlementCatalog(AccessControlDbContext db) : IEntitlementCatalog
{
    public async Task<EntitlementPageResponse> ListAsync(string tenantId, Guid? unitOrganizationId, int page,
        int pageSize, CancellationToken cancellationToken)
    {
        IQueryable<string> required = db.EndpointPolicyEntitlements.AsNoTracking()
            .Where(value => value.EndpointPolicy.TenantId == tenantId &&
                (!unitOrganizationId.HasValue || value.EndpointPolicy.UnitOrganizationId == unitOrganizationId.Value))
            .Select(value => value.Entitlement);
        IQueryable<string> granted = db.RoleEntitlements.AsNoTracking()
            .Where(value => value.TenantId == tenantId &&
                (!unitOrganizationId.HasValue || value.UnitOrganizationId == unitOrganizationId.Value))
            .Select(value => value.Entitlement);

        IQueryable<string> names = required.Union(granted);
        int totalCount = await names.CountAsync(cancellationToken);
        string[] items = await names.OrderBy(value => value).Skip((page - 1) * pageSize)
            .Take(pageSize).ToArrayAsync(cancellationToken);
        return new EntitlementPageResponse(items, page, pageSize, totalCount);
    }
}
