using AccessControl.Application;
using AccessControl.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AccessControl.Infrastructure.Database;

public sealed class SqlAuthorizationStore(AccessControlDbContext dbContext, ILogger<SqlAuthorizationStore> logger) : IAuthorizationStore
{
    public Task<long> GetRevisionAsync(CancellationToken cancellationToken) =>
        dbContext.AuthorizationRevisions.AsNoTracking().Where(x => x.Id == 1)
            .Select(x => x.Version).SingleAsync(cancellationToken);

    public async Task<EndpointPolicy?> GetPolicyAsync(string tenantId, Guid unitOrganizationId, string module, string httpMethod, string routeTemplate, CancellationToken cancellationToken)
    {
        EndpointPolicyEntity? entity = await dbContext.EndpointPolicies.AsNoTracking()
            .Include(x => x.RequiredEntitlements)
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.UnitOrganizationId == unitOrganizationId && x.Module == module &&
                x.HttpMethod == httpMethod && x.RouteTemplate == routeTemplate, cancellationToken);
        logger.LogDebug("Loaded endpoint policy for {TenantId} {Module} {HttpMethod} {RouteTemplate}: {Found}",
            tenantId, module, httpMethod, routeTemplate, entity is not null);
        return entity is null ? null : new EndpointPolicy(entity.TenantId, entity.Module, entity.HttpMethod,
            entity.RouteTemplate, entity.IsPublic, entity.RequiredEntitlements.Select(x => x.EntitlementId).ToList(), entity.UnitOrganizationId);
    }

    public async Task<IReadOnlySet<Guid>> GetEntitlementsAsync(string tenantId, Guid unitOrganizationId, IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken)
    {
        if (roleIds.Count == 0)
        {
            logger.LogDebug("No roles supplied for tenant {TenantId}", tenantId);
            return new HashSet<Guid>();
        }

        List<Guid> values = await dbContext.RoleEntitlements.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.UnitOrganizationId == unitOrganizationId &&
                roleIds.Contains(x.RoleId) && x.Entitlement.IsActive)
            .Select(x => x.EntitlementId)
            .ToListAsync(cancellationToken);
        logger.LogDebug("Loaded {EntitlementCount} entitlements for {RoleCount} roles in tenant {TenantId}",
            values.Count, roleIds.Count, tenantId);
        return values.ToHashSet();
    }

    public async Task<IReadOnlyList<Guid>> GetAssignedEntitlementIdsAsync(string tenantId,
        Guid unitOrganizationId, Guid roleId, CancellationToken cancellationToken) =>
        await dbContext.RoleEntitlements.AsNoTracking().Where(value => value.TenantId == tenantId &&
            value.UnitOrganizationId == unitOrganizationId && value.RoleId == roleId)
            .OrderBy(value => value.EntitlementId).Select(value => value.EntitlementId)
            .ToArrayAsync(cancellationToken);

    public async Task UpsertPolicyAsync(EndpointPolicy policy, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await ValidateActiveEntitlementsAsync(policy.TenantId, policy.UnitOrganizationId,
            policy.RequiredEntitlementIds, cancellationToken);
        EndpointPolicyEntity? entity = await dbContext.EndpointPolicies.SingleOrDefaultAsync(x =>
            x.TenantId == policy.TenantId && x.UnitOrganizationId == policy.UnitOrganizationId && x.Module == policy.Module &&
            x.HttpMethod == policy.HttpMethod && x.RouteTemplate == policy.RouteTemplate, cancellationToken);

        if (entity is null)
        {
            entity = new EndpointPolicyEntity
            {
                TenantId = policy.TenantId,
                UnitOrganizationId = policy.UnitOrganizationId,
                Module = policy.Module,
                HttpMethod = policy.HttpMethod,
                RouteTemplate = policy.RouteTemplate,
                IsPublic = policy.IsPublic
            };
            dbContext.EndpointPolicies.Add(entity);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogDebug("Created endpoint policy row {PolicyId}", entity.Id);
        }
        else
        {
            entity.IsPublic = policy.IsPublic;
            await dbContext.EndpointPolicyEntitlements.Where(x => x.EndpointPolicyId == entity.Id)
                .ExecuteDeleteAsync(cancellationToken);
            logger.LogDebug("Replacing entitlements on endpoint policy row {PolicyId}", entity.Id);
        }

        foreach (Guid entitlementId in policy.RequiredEntitlementIds.Distinct())
        {
            dbContext.EndpointPolicyEntitlements.Add(new EndpointPolicyEntitlementEntity
            {
                EndpointPolicyId = entity.Id,
                TenantId = policy.TenantId,
                UnitOrganizationId = policy.UnitOrganizationId,
                EntitlementId = entitlementId
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await IncrementRevisionAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Stored endpoint policy {TenantId} {Module} {HttpMethod} {RouteTemplate}; public {IsPublic}, entitlement count {Count}",
            policy.TenantId, policy.Module, policy.HttpMethod, policy.RouteTemplate, policy.IsPublic, policy.RequiredEntitlementIds.Count);
    }

    public async Task DeletePolicyAsync(string tenantId, Guid unitOrganizationId, string module, string httpMethod, string routeTemplate, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        int deleted = await dbContext.EndpointPolicies.Where(x => x.TenantId == tenantId && x.UnitOrganizationId == unitOrganizationId && x.Module == module &&
            x.HttpMethod == httpMethod && x.RouteTemplate == routeTemplate).ExecuteDeleteAsync(cancellationToken);
        await IncrementRevisionAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Deleted {Count} endpoint policy rows for {TenantId} {Module} {HttpMethod} {RouteTemplate}",
            deleted, tenantId, module, httpMethod, routeTemplate);
    }

    public async Task ReplaceRoleEntitlementsAsync(string tenantId, Guid unitOrganizationId, Guid roleId, IReadOnlyList<Guid> entitlementIds, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await ValidateActiveEntitlementsAsync(tenantId, unitOrganizationId, entitlementIds, cancellationToken);
        int deleted = await dbContext.RoleEntitlements.Where(x => x.TenantId == tenantId && x.UnitOrganizationId == unitOrganizationId && x.RoleId == roleId)
            .ExecuteDeleteAsync(cancellationToken);

        foreach (Guid entitlementId in entitlementIds.Distinct())
        {
            dbContext.RoleEntitlements.Add(new RoleEntitlementEntity
            {
                TenantId = tenantId,
                UnitOrganizationId = unitOrganizationId,
                RoleId = roleId,
                EntitlementId = entitlementId
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await IncrementRevisionAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Replaced role entitlements for {TenantId} {RoleId}: removed {RemovedCount}, stored {StoredCount}",
            tenantId, roleId, deleted, entitlementIds.Count);
    }

    private async Task ValidateActiveEntitlementsAsync(string tenantId, Guid unitOrganizationId,
        IReadOnlyList<Guid> entitlementIds, CancellationToken cancellationToken)
    {
        Guid[] distinct = entitlementIds.Distinct().ToArray();
        if (distinct.Length == 0) return;
        int count = await dbContext.Entitlements.CountAsync(value => value.TenantId == tenantId &&
            value.UnitOrganizationId == unitOrganizationId && value.IsActive && distinct.Contains(value.Id), cancellationToken);
        if (count != distinct.Length)
            throw new ArgumentException("Every entitlement ID must be active in the selected tenant and unit organization.",
                nameof(entitlementIds));
    }

    private async Task IncrementRevisionAsync(CancellationToken cancellationToken)
    {
        int changed = await dbContext.AuthorizationRevisions.Where(x => x.Id == 1)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Version, x => x.Version + 1), cancellationToken);
        if (changed != 1)
        {
            throw new InvalidOperationException("Authorization revision row is missing. Apply the latest database migration.");
        }
        logger.LogDebug("Authorization revision incremented");
    }
}
