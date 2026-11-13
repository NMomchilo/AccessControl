using AccessControl.Application;
using AccessControl.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AccessControl.Infrastructure.Database;

public sealed class SqlPolicyCatalog(AccessControlDbContext db) : IPolicyCatalog
{
    public async Task<PolicyPageResponse> SearchAsync(string tenantId, Guid? unitOrganizationId,
        string? module, string? httpMethod, int page, int pageSize, CancellationToken cancellationToken)
    {
        IQueryable<EndpointPolicyEntity> query = db.EndpointPolicies.AsNoTracking()
            .Where(policy => policy.TenantId == tenantId);
        if (unitOrganizationId.HasValue)
            query = query.Where(policy => policy.UnitOrganizationId == unitOrganizationId.Value);
        if (module is not null)
            query = query.Where(policy => policy.Module == module);
        if (httpMethod is not null)
            query = query.Where(policy => policy.HttpMethod == httpMethod);
        int count = await query.CountAsync(cancellationToken);
        EndpointPolicyEntity[] entities = await query.OrderBy(policy => policy.UnitOrganizationId)
            .ThenBy(policy => policy.Module).ThenBy(policy => policy.HttpMethod)
            .ThenBy(policy => policy.RouteTemplate).ThenBy(policy => policy.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Include(policy => policy.RequiredEntitlements).AsSplitQuery()
            .ToArrayAsync(cancellationToken);
        PolicyRequest[] items = entities.Select(policy => new PolicyRequest(policy.TenantId,
            policy.Module, policy.HttpMethod, policy.RouteTemplate, policy.IsPublic,
            policy.RequiredEntitlements.Select(required => required.EntitlementId).Order().ToArray(),
            policy.UnitOrganizationId)).ToArray();
        return new PolicyPageResponse(items, page, pageSize, count);
    }
}
