using AccessControl.Application;
using AccessControl.Contracts;
using AccessControl.Infrastructure.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessControl.Infrastructure.Database;

public sealed class SqlEntitlementCatalog(AccessControlDbContext db, AuthorizationCache? cache = null,
    ILogger<SqlEntitlementCatalog>? logger = null) : IEntitlementCatalog
{
    private readonly ILogger<SqlEntitlementCatalog> _logger = logger ?? NullLogger<SqlEntitlementCatalog>.Instance;

    public async Task<EntitlementInfo> CreateAsync(string tenantId, Guid unitOrganizationId, string code,
        string displayName, CancellationToken cancellationToken)
    {
        if (await db.Entitlements.AnyAsync(value => value.TenantId == tenantId &&
            value.UnitOrganizationId == unitOrganizationId && value.Code == code, cancellationToken))
            throw new EntitlementConflictException("Entitlement code already exists in this unit organization.");
        EntitlementEntity entity = new()
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UnitOrganizationId = unitOrganizationId,
            Code = code, DisplayName = displayName, IsActive = true
        };
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Entitlements.Add(entity);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            db.ChangeTracker.Clear();
            if (await db.Entitlements.AsNoTracking().AnyAsync(value => value.TenantId == tenantId &&
                value.UnitOrganizationId == unitOrganizationId && value.Code == code, cancellationToken))
            {
                _logger.LogWarning(exception, "Entitlement create conflicted in tenant {TenantId}, organization {UnitOrganizationId}",
                    tenantId, unitOrganizationId);
                throw new EntitlementConflictException("Entitlement code already exists in this unit organization.");
            }
            throw;
        }
        await IncrementRevisionAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        cache?.InvalidateAll("entitlement created");
        _logger.LogInformation("Created entitlement {EntitlementId} in tenant {TenantId}, organization {UnitOrganizationId}",
            entity.Id, tenantId, unitOrganizationId);
        return Map(entity);
    }

    public async Task<bool> SetActiveAsync(string tenantId, Guid unitOrganizationId, Guid entitlementId,
        bool isActive, CancellationToken cancellationToken)
    {
        EntitlementEntity? entity = await db.Entitlements.SingleOrDefaultAsync(value => value.Id == entitlementId &&
            value.TenantId == tenantId && value.UnitOrganizationId == unitOrganizationId, cancellationToken);
        if (entity is null) return false;
        if (entity.IsActive == isActive) return true;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        entity.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);
        await IncrementRevisionAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        cache?.InvalidateAll("entitlement active status changed");
        _logger.LogInformation("Entitlement {EntitlementId} active status set to {IsActive}", entitlementId, isActive);
        return true;
    }

    public async Task<EntitlementInfo?> UpdateDisplayNameAsync(string tenantId, Guid unitOrganizationId,
        Guid entitlementId, string displayName, CancellationToken cancellationToken)
    {
        int changed = await db.Entitlements.Where(value => value.Id == entitlementId &&
                value.TenantId == tenantId && value.UnitOrganizationId == unitOrganizationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.DisplayName, displayName),
                cancellationToken);
        if (changed == 0) return null;
        EntitlementEntity updated = await db.Entitlements.AsNoTracking().SingleAsync(value =>
            value.Id == entitlementId && value.TenantId == tenantId &&
            value.UnitOrganizationId == unitOrganizationId, cancellationToken);
        _logger.LogInformation("Updated display name of entitlement {EntitlementId} in tenant {TenantId}, organization {UnitOrganizationId}",
            entitlementId, tenantId, unitOrganizationId);
        return Map(updated);
    }

    public async Task<EntitlementPageResponse> ListAsync(string tenantId, Guid? unitOrganizationId, int page,
        int pageSize, CancellationToken cancellationToken)
    {
        IQueryable<EntitlementEntity> query = db.Entitlements.AsNoTracking().Where(value =>
            value.TenantId == tenantId && (!unitOrganizationId.HasValue ||
                value.UnitOrganizationId == unitOrganizationId.Value));
        int totalCount = await query.CountAsync(cancellationToken);
        EntitlementInfo[] items = await query.OrderBy(value => value.UnitOrganizationId).ThenBy(value => value.Code)
            .ThenBy(value => value.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(value => new EntitlementInfo(value.Id, value.TenantId, value.UnitOrganizationId,
                value.Code, value.DisplayName, value.IsActive)).ToArrayAsync(cancellationToken);
        return new EntitlementPageResponse(items, page, pageSize, totalCount);
    }

    public Task<EntitlementInfo?> GetAsync(string tenantId, Guid unitOrganizationId, Guid entitlementId,
        CancellationToken cancellationToken) =>
        db.Entitlements.AsNoTracking().Where(value => value.TenantId == tenantId &&
                value.UnitOrganizationId == unitOrganizationId && value.Id == entitlementId)
            .Select(value => new EntitlementInfo(value.Id, value.TenantId, value.UnitOrganizationId,
                value.Code, value.DisplayName, value.IsActive))
            .SingleOrDefaultAsync(cancellationToken);

    private async Task IncrementRevisionAsync(CancellationToken cancellationToken)
    {
        int changed = await db.AuthorizationRevisions.Where(value => value.Id == 1)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Version,
                value => value.Version + 1), cancellationToken);
        if (changed != 1)
            throw new InvalidOperationException("Authorization revision row is missing. Apply the latest database migration.");
    }

    private static EntitlementInfo Map(EntitlementEntity value) =>
        new(value.Id, value.TenantId, value.UnitOrganizationId, value.Code, value.DisplayName, value.IsActive);
}
