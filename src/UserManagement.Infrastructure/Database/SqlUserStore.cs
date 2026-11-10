using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UserManagement.Application;
using UserManagement.Domain;

namespace UserManagement.Infrastructure.Database;

public sealed class SqlUserStore(UserManagementDbContext db, ILogger<SqlUserStore> logger) : IUserStore
{
    public async Task<ManagedUser> ResolveAsync(string tenantId, string objectId, string? email, string? displayName,
        CancellationToken cancellationToken)
    {
        UserEntity? user = await FindUserAsync(tenantId, objectId, cancellationToken);
        if (user is null)
        {
            RoleEntity reviewer = await EnsureReviewerAsync(tenantId, cancellationToken);
            user = new UserEntity
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ObjectId = objectId, Email = email,
                DisplayName = displayName ?? email ?? "Unknown user", CreatedAt = DateTimeOffset.UtcNow,
                Roles = [new UserRoleEntity { TenantId = tenantId, RoleId = reviewer.Id, Role = reviewer }]
            };
            db.Users.Add(user);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Provisioned user {UserId} in tenant {TenantId} with Reviewer role {RoleId}",
                    user.Id, tenantId, reviewer.Id);
                return Map(user);
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                user = await FindUserAsync(tenantId, objectId, cancellationToken);
                if (user is null)
                {
                    throw;
                }

                logger.LogInformation("Concurrent user provisioning resolved for {UserId}", user.Id);
                return Map(user);
            }
        }

        if (email is not null && user.Email != email)
        {
            user.Email = email;
        }

        if (displayName is not null && user.DisplayName != displayName)
        {
            user.DisplayName = displayName;
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogDebug("Updated profile for user {UserId}", user.Id);
        }

        return Map(user);
    }

    public async Task<ManagedUser?> GetAsync(string tenantId, string objectId, CancellationToken cancellationToken)
    {
        UserEntity? user = await db.Users.AsNoTracking().Include(value => value.Roles)
            .ThenInclude(value => value.Role)
            .SingleOrDefaultAsync(value => value.TenantId == tenantId && value.ObjectId == objectId, cancellationToken);
        return user is null ? null : Map(user);
    }

    public async Task<bool> ReplaceRolesAsync(string tenantId, string objectId, IReadOnlyList<Guid> roleIds,
        CancellationToken cancellationToken)
    {
        UserEntity? user = await db.Users.SingleOrDefaultAsync(value =>
            value.TenantId == tenantId && value.ObjectId == objectId, cancellationToken);
        if (user is null)
        {
            return false;
        }

        Guid[] distinct = roleIds.Distinct().ToArray();
        int validCount = await db.Roles.CountAsync(value => value.TenantId == tenantId && distinct.Contains(value.Id), cancellationToken);
        if (validCount != distinct.Length)
        {
            throw new ArgumentException("Every role ID must exist in the user's tenant.", nameof(roleIds));
        }

        // ExecuteDelete bypasses change tracking; detach any roles loaded by an earlier resolution.
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.UserRoles.Where(value => value.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
        db.UserRoles.AddRange(distinct.Select(roleId => new UserRoleEntity
        {
            TenantId = tenantId, UserId = user.Id, RoleId = roleId
        }));
        await db.SaveChangesAsync(cancellationToken);
        await IncrementRevisionAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();
        logger.LogInformation("Replaced roles for user {UserId} in tenant {TenantId}; count {RoleCount}",
            user.Id, tenantId, distinct.Length);
        return true;
    }

    public async Task<bool> SetActiveAsync(string tenantId, string objectId, bool isActive,
        CancellationToken cancellationToken)
    {
        UserEntity? user = await db.Users.SingleOrDefaultAsync(value =>
            value.TenantId == tenantId && value.ObjectId == objectId, cancellationToken);
        if (user is null)
        {
            return false;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        user.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);
        await IncrementRevisionAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Changed active status for user {UserId} to {IsActive}", user.Id, isActive);
        return true;
    }

    public async Task<ManagedRole> CreateRoleAsync(string tenantId, string code, string displayName,
        CancellationToken cancellationToken)
    {
        if (await db.Roles.AnyAsync(value => value.TenantId == tenantId && value.Code == code, cancellationToken))
        {
            throw new ArgumentException("Role code already exists in this tenant.", nameof(code));
        }

        RoleEntity role = new() { Id = Guid.NewGuid(), TenantId = tenantId, Code = code, DisplayName = displayName };
        db.Roles.Add(role);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (await db.Roles.AnyAsync(value => value.TenantId == tenantId && value.Code == code, cancellationToken))
            {
                throw new ArgumentException("Role code already exists in this tenant.", nameof(code));
            }
            throw;
        }
        logger.LogInformation("Created role {RoleId} in tenant {TenantId}", role.Id, tenantId);
        return Map(role);
    }

    public async Task<IReadOnlyList<ManagedRole>> GetRolesAsync(string tenantId, CancellationToken cancellationToken) =>
        await db.Roles.AsNoTracking().Where(value => value.TenantId == tenantId)
            .OrderBy(value => value.Code).Select(value => new ManagedRole(value.Id, value.TenantId,
                value.Code, value.DisplayName)).ToArrayAsync(cancellationToken);

    public Task<long> GetRevisionAsync(CancellationToken cancellationToken) =>
        db.Revisions.AsNoTracking().Where(value => value.Id == 1)
            .Select(value => value.Version).SingleAsync(cancellationToken);

    private Task<UserEntity?> FindUserAsync(string tenantId, string objectId, CancellationToken cancellationToken) =>
        db.Users.Include(value => value.Roles).ThenInclude(value => value.Role)
            .SingleOrDefaultAsync(value => value.TenantId == tenantId && value.ObjectId == objectId, cancellationToken);

    private async Task<RoleEntity> EnsureReviewerAsync(string tenantId, CancellationToken cancellationToken)
    {
        RoleEntity? reviewer = await db.Roles.SingleOrDefaultAsync(value =>
            value.TenantId == tenantId && value.Code == UserRoles.Reviewer, cancellationToken);
        if (reviewer is not null)
        {
            return reviewer;
        }

        reviewer = new RoleEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Code = UserRoles.Reviewer, DisplayName = "Reviewer"
        };
        db.Roles.Add(reviewer);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Created Reviewer role {RoleId} in tenant {TenantId}", reviewer.Id, tenantId);
            return reviewer;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            reviewer = await db.Roles.SingleOrDefaultAsync(value =>
                value.TenantId == tenantId && value.Code == UserRoles.Reviewer, cancellationToken);
            if (reviewer is null)
            {
                throw;
            }
            return reviewer;
        }
    }

    private async Task IncrementRevisionAsync(CancellationToken cancellationToken)
    {
        int affected = await db.Revisions.Where(value => value.Id == 1)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Version,
                value => value.Version + 1), cancellationToken);
        if (affected != 1)
        {
            throw new InvalidOperationException("User Management revision row is missing.");
        }
    }

    private static ManagedUser Map(UserEntity user) => new(user.Id, user.TenantId, user.ObjectId, user.Email,
        user.DisplayName, user.IsActive, user.CreatedAt,
        user.Roles.Select(value => Map(value.Role)).OrderBy(value => value.Code, StringComparer.Ordinal).ToArray());

    private static ManagedRole Map(RoleEntity role) => new(role.Id, role.TenantId, role.Code, role.DisplayName);
}
