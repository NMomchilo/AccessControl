using AccessControl.Contracts;
using AccessControl.Application;
using AccessControl.Infrastructure.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AccessControl.Infrastructure.Database;

public sealed class CatalogRegistrationStore(AccessControlDbContext db, AuthorizationCache cache,
    ILogger<CatalogRegistrationStore> logger) : ICatalogRegistrationService
{
    public async Task<CatalogRegistrationResult> RegisterAsync(CatalogRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                ModuleEntity? module = await db.Modules.SingleOrDefaultAsync(value =>
                    value.TenantId == request.TenantId && value.UnitOrganizationId == request.UnitOrganizationId &&
                    value.Code == request.Module, cancellationToken);
                if (module is not null && (!module.IsActive || module.Name != request.ModuleName))
                    throw new CatalogRegistrationConflictException();
                int modulesCreated = 0;
                if (module is null)
                {
                    db.Modules.Add(new ModuleEntity
                    {
                        TenantId = request.TenantId, UnitOrganizationId = request.UnitOrganizationId,
                        Code = request.Module, Name = request.ModuleName
                    });
                    modulesCreated = 1;
                }

                var existing = await db.EndpointPolicies.AsNoTracking().Where(value =>
                        value.TenantId == request.TenantId &&
                        value.UnitOrganizationId == request.UnitOrganizationId && value.Module == request.Module)
                    .Select(value => new { value.HttpMethod, value.RouteTemplate })
                    .ToListAsync(cancellationToken);
                var known = existing.Select(value => (value.HttpMethod, value.RouteTemplate)).ToHashSet();
                int policiesCreated = 0;
                foreach (CatalogEndpoint endpoint in request.Endpoints)
                {
                    var key = (endpoint.HttpMethod, endpoint.RouteTemplate);
                    if (known.Contains(key)) continue;
                    db.EndpointPolicies.Add(new EndpointPolicyEntity
                    {
                        TenantId = request.TenantId, UnitOrganizationId = request.UnitOrganizationId,
                        Module = request.Module, HttpMethod = key.HttpMethod, RouteTemplate = key.RouteTemplate,
                        IsPublic = false, IsActive = true
                    });
                    known.Add(key);
                    policiesCreated++;
                }

                if (modulesCreated + policiesCreated > 0)
                {
                    await db.SaveChangesAsync(cancellationToken);
                    if (policiesCreated > 0)
                    {
                        string actor = db.RequiredActorId;
                        int changed = await db.AuthorizationRevisions.Where(value =>
                                value.Id == AuthorizationRevisionEntity.SingletonId)
                            .ExecuteUpdateAsync(setters => setters
                                .SetProperty(value => value.Version, value => value.Version + 1)
                                .SetProperty(value => value.LastUpdateUser, actor)
                                .SetProperty(value => value.LastUpdateTime, DateTime.UtcNow), cancellationToken);
                        if (changed != 1) throw new InvalidOperationException("Authorization revision row is missing.");
                    }
                }
                await transaction.CommitAsync(cancellationToken);
                if (policiesCreated > 0) cache.InvalidateAll("catalog registration");
                logger.LogInformation("Catalog registered for {TenantId} {UnitOrganizationId} {Module}; modules {ModulesCreated}, policies {PoliciesCreated}",
                    request.TenantId, request.UnitOrganizationId, request.Module, modulesCreated, policiesCreated);
                return new CatalogRegistrationResult(modulesCreated, policiesCreated);
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                db.ChangeTracker.Clear();
                logger.LogInformation("Concurrent catalog registration detected; retrying once");
            }
        }
        throw new CatalogRegistrationConflictException();
    }
}
