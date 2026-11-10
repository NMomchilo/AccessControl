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

    public async Task<EndpointPolicy?> GetPolicyAsync(string tenantId, string module, string httpMethod, string routeTemplate, CancellationToken cancellationToken)
    {
        EndpointPolicyEntity? entity = await dbContext.EndpointPolicies.AsNoTracking()
            .Include(x => x.RequiredEntitlements)
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Module == module &&
                x.HttpMethod == httpMethod && x.RouteTemplate == routeTemplate, cancellationToken);
        logger.LogDebug("Loaded endpoint policy for {TenantId} {Module} {HttpMethod} {RouteTemplate}: {Found}",
            tenantId, module, httpMethod, routeTemplate, entity is not null);
        return entity is null ? null : new EndpointPolicy(entity.TenantId, entity.Module, entity.HttpMethod,
            entity.RouteTemplate, entity.IsPublic, entity.RequiredEntitlements.Select(x => x.Entitlement).ToList());
    }

    public async Task<IReadOnlySet<string>> GetEntitlementsAsync(string tenantId, IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken)
    {
        if (roleIds.Count == 0)
        {
            logger.LogDebug("No roles supplied for tenant {TenantId}", tenantId);
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        List<string> values = await dbContext.RoleEntitlements.AsNoTracking()
            .Where(x => x.TenantId == tenantId && roleIds.Contains(x.RoleId))
            .Select(x => x.Entitlement)
            .ToListAsync(cancellationToken);
        logger.LogDebug("Loaded {EntitlementCount} entitlements for {RoleCount} roles in tenant {TenantId}",
            values.Count, roleIds.Count, tenantId);
        return values.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task UpsertPolicyAsync(EndpointPolicy policy, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        EndpointPolicyEntity? entity = await dbContext.EndpointPolicies.SingleOrDefaultAsync(x =>
            x.TenantId == policy.TenantId && x.Module == policy.Module &&
            x.HttpMethod == policy.HttpMethod && x.RouteTemplate == policy.RouteTemplate, cancellationToken);

        if (entity is null)
        {
            entity = new EndpointPolicyEntity
            {
                TenantId = policy.TenantId,
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

        foreach (string entitlement in policy.RequiredEntitlements.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            dbContext.EndpointPolicyEntitlements.Add(new EndpointPolicyEntitlementEntity
            {
                EndpointPolicyId = entity.Id,
                Entitlement = entitlement
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await IncrementRevisionAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Stored endpoint policy {TenantId} {Module} {HttpMethod} {RouteTemplate}; public {IsPublic}, entitlement count {Count}",
            policy.TenantId, policy.Module, policy.HttpMethod, policy.RouteTemplate, policy.IsPublic, policy.RequiredEntitlements.Count);
    }

    public async Task DeletePolicyAsync(string tenantId, string module, string httpMethod, string routeTemplate, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        int deleted = await dbContext.EndpointPolicies.Where(x => x.TenantId == tenantId && x.Module == module &&
            x.HttpMethod == httpMethod && x.RouteTemplate == routeTemplate).ExecuteDeleteAsync(cancellationToken);
        await IncrementRevisionAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Deleted {Count} endpoint policy rows for {TenantId} {Module} {HttpMethod} {RouteTemplate}",
            deleted, tenantId, module, httpMethod, routeTemplate);
    }

    public async Task ReplaceRoleEntitlementsAsync(string tenantId, Guid roleId, IReadOnlyList<string> entitlements, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        int deleted = await dbContext.RoleEntitlements.Where(x => x.TenantId == tenantId && x.RoleId == roleId)
            .ExecuteDeleteAsync(cancellationToken);

        foreach (string entitlement in entitlements.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            dbContext.RoleEntitlements.Add(new RoleEntitlementEntity
            {
                TenantId = tenantId,
                RoleId = roleId,
                Entitlement = entitlement
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await IncrementRevisionAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Replaced role entitlements for {TenantId} {RoleId}: removed {RemovedCount}, stored {StoredCount}",
            tenantId, roleId, deleted, entitlements.Count);
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
