using AccessControl.Contracts;
using AccessControl.Application;
using AccessControl.Infrastructure.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AccessControl.Infrastructure.Database;

public sealed class CatalogRegistrationStore(AccessControlDbContext db, AuthorizationCache cache,
    ILogger<CatalogRegistrationStore> logger) : ICatalogRegistrationService
{
    public async Task<CatalogRegistrationResult> RegisterAsync(CatalogRegistrationRequest request, string clientId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientId) || clientId.Length > 200)
            throw new ArgumentException("A validated client ID is required.", nameof(clientId));
        for (int attempt = 0; attempt < 2; attempt++)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var existing = await db.EndpointPolicies.Where(value =>
                        value.TenantId == request.TenantId &&
                        value.UnitOrganizationId == request.UnitOrganizationId)
                    .ToListAsync(cancellationToken);
                var known = existing.ToDictionary(value => (value.HttpMethod, value.RouteTemplate));
                int policiesCreated = 0;
                foreach (CatalogEndpoint endpoint in request.Endpoints)
                {
                    var key = (endpoint.HttpMethod, endpoint.RouteTemplate);
                    if (known.ContainsKey(key)) continue;
                    EndpointPolicyEntity policy = new()
                    {
                        TenantId = request.TenantId, UnitOrganizationId = request.UnitOrganizationId,
                        HttpMethod = key.HttpMethod, RouteTemplate = key.RouteTemplate,
                        IsPublic = false, IsActive = true
                    };
                    db.EndpointPolicies.Add(policy);
                    known.Add(key, policy);
                    policiesCreated++;
                }

                if (db.ChangeTracker.HasChanges())
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
                logger.LogInformation("Catalog registered for {TenantId} {UnitOrganizationId} by {ClientId}; policies {PoliciesCreated}",
                    request.TenantId, request.UnitOrganizationId, clientId, policiesCreated);
                return new CatalogRegistrationResult(policiesCreated);
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
