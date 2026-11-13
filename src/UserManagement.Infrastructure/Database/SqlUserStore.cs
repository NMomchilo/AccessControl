using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UserManagement.Application;
using UserManagement.Domain;

namespace UserManagement.Infrastructure.Database;

public sealed class SqlUserStore(UserManagementDbContext db, ILogger<SqlUserStore> logger) : IUserStore
{
    public async Task<ManagedUser> ResolveAsync(string tenantId, Guid unitOrganizationId, string unitOrganizationName,
        string objectId, string? email, string? displayName,
        CancellationToken cancellationToken)
    {
        UnitOrganizationEntity organization = await EnsureOrganizationAsync(tenantId, unitOrganizationId,
            unitOrganizationName, cancellationToken);
        UserEntity? user = await FindUserAsync(tenantId, unitOrganizationId, objectId, cancellationToken);
        if (user is null || !user.Organizations.Any(value => value.UnitOrganizationId == unitOrganizationId))
        {
            RoleEntity reviewer = await EnsureReviewerAsync(tenantId, unitOrganizationId, cancellationToken);
            // A concurrent Reviewer insert clears tracking. Reload an existing user before adding membership.
            if (user is not null && db.Entry(user).State == EntityState.Detached)
            {
                user = await FindUserAsync(tenantId, unitOrganizationId, objectId, cancellationToken);
                if (user?.Organizations.Any(value => value.UnitOrganizationId == unitOrganizationId) == true)
                    return Map(user, organization);
            }
            user ??= new UserEntity
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ObjectId = objectId, Email = email,
                DisplayName = displayName ?? email ?? "Unknown user", CreatedAt = DateTimeOffset.UtcNow
            };
            UnitOrganizationMembershipEntity membership = new()
            {
                TenantId = tenantId, UserId = user.Id, UnitOrganizationId = unitOrganizationId,
                Roles = reviewer.IsActive ? [new UserRoleEntity
                {
                    TenantId = tenantId, UserId = user.Id, UnitOrganizationId = unitOrganizationId,
                    RoleId = reviewer.Id, Role = reviewer
                }] : []
            };
            user.Organizations.Add(membership);
            if (db.Entry(user).State == EntityState.Detached) db.Users.Add(user);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Provisioned user {UserId} in tenant {TenantId}, organization {UnitOrganizationId}",
                    user.Id, tenantId, unitOrganizationId);
                db.ChangeTracker.Clear();
                user = await FindUserAsync(tenantId, unitOrganizationId, objectId, cancellationToken)
                    ?? throw new InvalidOperationException("Provisioned user could not be reloaded.");
                return Map(user, organization);
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                user = await FindUserAsync(tenantId, unitOrganizationId, objectId, cancellationToken);
                if (user is null || !user.Organizations.Any(value => value.UnitOrganizationId == unitOrganizationId))
                {
                    throw;
                }

                logger.LogInformation("Concurrent user provisioning resolved for {UserId}", user.Id);
                return Map(user, organization);
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

        return Map(user, organization);
    }

    public async Task<ManagedUser?> GetAsync(string tenantId, Guid unitOrganizationId, string objectId,
        CancellationToken cancellationToken)
    {
        UserEntity? user = await db.Users.AsNoTracking().AsSplitQuery()
            .Include(value => value.Organizations.Where(membership =>
                membership.UnitOrganizationId == unitOrganizationId))
            .Include(value => value.Roles.Where(role => role.UnitOrganizationId == unitOrganizationId))
            .ThenInclude(value => value.Role)
            .SingleOrDefaultAsync(value => value.TenantId == tenantId && value.ObjectId == objectId, cancellationToken);
        if (user is null || !user.Organizations.Any(value => value.UnitOrganizationId == unitOrganizationId))
            return null;
        UnitOrganizationEntity? organization = await db.UnitOrganizations.AsNoTracking()
            .SingleOrDefaultAsync(value => value.TenantId == tenantId && value.Id == unitOrganizationId &&
                !value.IsDeleted,
                cancellationToken);
        return organization is null ? null : Map(user, organization);
    }

    public async Task<PageSlice<ManagedUserSummary>> GetUsersAsync(string tenantId, Guid? unitOrganizationId,
        int offset, int pageSize, string? email, CancellationToken cancellationToken)
    {
        IQueryable<UserEntity> query = db.Users.AsNoTracking().Where(user => user.TenantId == tenantId);
        if (email is not null)
            query = query.Where(user => user.NormalizedEmail == email);
        if (unitOrganizationId.HasValue)
        {
            Guid organizationId = unitOrganizationId.Value;
            query = query.Where(user => user.Organizations.Any(membership =>
                membership.UnitOrganizationId == organizationId));
        }
        int totalCount = await query.CountAsync(cancellationToken);
        var users = await query.OrderBy(user => user.ObjectId).ThenBy(user => user.Id)
            .Skip(offset).Take(pageSize)
            .Select(user => new { user.Id, user.ObjectId, user.DisplayName, user.Email, user.IsActive })
            .ToArrayAsync(cancellationToken);
        if (users.Length == 0) return new PageSlice<ManagedUserSummary>([], totalCount);

        Guid[] userIds = users.Select(user => user.Id).ToArray();
        var memberships = await db.UnitOrganizationMemberships.AsNoTracking()
            .Where(membership => membership.TenantId == tenantId && userIds.Contains(membership.UserId) &&
                !membership.UnitOrganization.IsDeleted &&
                (!unitOrganizationId.HasValue || membership.UnitOrganizationId == unitOrganizationId.Value))
            .Select(membership => new { membership.UserId, membership.UnitOrganizationId,
                OrganizationName = membership.UnitOrganization.Name }).ToArrayAsync(cancellationToken);
        var assignments = await db.UserRoles.AsNoTracking()
            .Where(assignment => assignment.TenantId == tenantId && userIds.Contains(assignment.UserId) &&
                (!unitOrganizationId.HasValue || assignment.UnitOrganizationId == unitOrganizationId.Value))
            .Select(assignment => new { assignment.UserId, assignment.UnitOrganizationId,
                assignment.RoleId, assignment.Role.Code, assignment.Role.DisplayName, assignment.Role.IsActive })
            .ToArrayAsync(cancellationToken);
        var rolesByMembership = assignments.ToLookup(assignment => (assignment.UserId, assignment.UnitOrganizationId));
        var membershipsByUser = memberships.ToLookup(membership => membership.UserId);
        ManagedUserSummary[] items = users.Select(user => new ManagedUserSummary(user.Id, user.ObjectId,
            user.DisplayName, user.Email, user.IsActive, membershipsByUser[user.Id]
                .OrderBy(membership => membership.UnitOrganizationId)
                .Select(membership => new ManagedUserMembershipSummary(membership.UnitOrganizationId,
                    membership.OrganizationName, rolesByMembership[(user.Id, membership.UnitOrganizationId)]
                        .OrderBy(assignment => assignment.Code).ThenBy(assignment => assignment.RoleId)
                        .Select(assignment => new ManagedRole(assignment.RoleId, tenantId,
                            membership.UnitOrganizationId, assignment.Code, assignment.DisplayName,
                            assignment.IsActive)).ToArray())).ToArray())).ToArray();
        return new PageSlice<ManagedUserSummary>(items, totalCount);
    }

    public async Task<ManagedRole?> GetRoleAsync(string tenantId, Guid unitOrganizationId, Guid roleId,
        CancellationToken cancellationToken) =>
        await db.Roles.AsNoTracking().Where(role => role.TenantId == tenantId &&
                role.UnitOrganizationId == unitOrganizationId && role.Id == roleId &&
                !role.UnitOrganization.IsDeleted)
            .Select(role => new ManagedRole(role.Id, role.TenantId, role.UnitOrganizationId,
                role.Code, role.DisplayName, role.IsActive))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> ReplaceRolesAsync(string tenantId, Guid unitOrganizationId, string objectId,
        IReadOnlyList<Guid> roleIds,
        CancellationToken cancellationToken)
    {
        UserEntity? user = await db.Users.AsNoTracking().SingleOrDefaultAsync(value =>
            value.TenantId == tenantId && value.ObjectId == objectId, cancellationToken);
        if (user is null || !await db.UnitOrganizations.AnyAsync(value => value.TenantId == tenantId &&
                value.Id == unitOrganizationId && !value.IsDeleted, cancellationToken) ||
            !await db.UnitOrganizationMemberships.AnyAsync(value =>
                value.TenantId == tenantId && value.UserId == user.Id &&
                value.UnitOrganizationId == unitOrganizationId, cancellationToken))
        {
            return false;
        }

        Guid[] distinct = roleIds.Distinct().ToArray();
        int validCount = await db.Roles.CountAsync(value => value.TenantId == tenantId &&
            value.UnitOrganizationId == unitOrganizationId && value.IsActive && distinct.Contains(value.Id), cancellationToken);
        if (validCount != distinct.Length)
        {
            throw new ArgumentException("Every role ID must exist in the user's organization.", nameof(roleIds));
        }

        // ExecuteDelete bypasses change tracking; detach any roles loaded by an earlier resolution.
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.UserRoles.Where(value => value.UserId == user.Id &&
            value.UnitOrganizationId == unitOrganizationId).ExecuteDeleteAsync(cancellationToken);
        db.UserRoles.AddRange(distinct.Select(roleId => new UserRoleEntity
        {
            TenantId = tenantId, UserId = user.Id, UnitOrganizationId = unitOrganizationId, RoleId = roleId
        }));
        await db.SaveChangesAsync(cancellationToken);
        await IncrementRevisionAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();
        logger.LogInformation("Replaced roles for user {UserId} in organization {UnitOrganizationId}; count {RoleCount}",
            user.Id, unitOrganizationId, distinct.Length);
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

    public async Task<ManagedRole> CreateRoleAsync(string tenantId, Guid unitOrganizationId, string code, string displayName,
        CancellationToken cancellationToken)
    {
        if (!await db.UnitOrganizations.AnyAsync(value => value.TenantId == tenantId &&
                value.Id == unitOrganizationId && !value.IsDeleted, cancellationToken))
            throw new ArgumentException("Unit organization does not exist in this tenant.", nameof(unitOrganizationId));
        if (await db.Roles.AnyAsync(value => value.TenantId == tenantId &&
                value.UnitOrganizationId == unitOrganizationId && value.Code == code, cancellationToken))
        {
            throw new ArgumentException("Role code already exists in this organization.", nameof(code));
        }

        RoleEntity role = new()
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UnitOrganizationId = unitOrganizationId,
            Code = code, DisplayName = displayName
        };
        db.Roles.Add(role);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (await db.Roles.AnyAsync(value => value.TenantId == tenantId &&
                    value.UnitOrganizationId == unitOrganizationId && value.Code == code, cancellationToken))
            {
                throw new ArgumentException("Role code already exists in this organization.", nameof(code));
            }
            throw;
        }
        logger.LogInformation("Created role {RoleId} in organization {UnitOrganizationId}", role.Id, unitOrganizationId);
        return Map(role);
    }

    public async Task<bool> SetRoleActiveAsync(string tenantId, Guid unitOrganizationId, Guid roleId, bool isActive,
        CancellationToken cancellationToken)
    {
        RoleEntity? role = await db.Roles.SingleOrDefaultAsync(value => value.Id == roleId &&
            value.TenantId == tenantId && value.UnitOrganizationId == unitOrganizationId &&
            !value.UnitOrganization.IsDeleted, cancellationToken);
        if (role is null) return false;
        if (role.IsActive == isActive) return true;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        role.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);
        await IncrementRevisionAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Role {RoleId} active status changed to {IsActive} in tenant {TenantId}, organization {UnitOrganizationId}",
            roleId, isActive, tenantId, unitOrganizationId);
        return true;
    }

    public async Task<PageSlice<ManagedRole>> GetRolesPageAsync(string tenantId, Guid? unitOrganizationId,
        int offset, int pageSize, CancellationToken cancellationToken)
    {
        IQueryable<RoleEntity> query = db.Roles.AsNoTracking().Where(role => role.TenantId == tenantId &&
            !role.UnitOrganization.IsDeleted);
        if (unitOrganizationId.HasValue)
        {
            Guid organizationId = unitOrganizationId.Value;
            query = query.Where(role => role.UnitOrganizationId == organizationId);
        }
        int totalCount = await query.CountAsync(cancellationToken);
        ManagedRole[] items = await query.OrderBy(role => role.UnitOrganizationId)
            .ThenBy(role => role.Code).ThenBy(role => role.Id).Skip(offset).Take(pageSize)
            .Select(role => new ManagedRole(role.Id, role.TenantId, role.UnitOrganizationId,
                role.Code, role.DisplayName, role.IsActive)).ToArrayAsync(cancellationToken);
        return new PageSlice<ManagedRole>(items, totalCount);
    }

    public async Task<ManagedUnitOrganization> CreateUnitOrganizationAsync(string tenantId, Guid id, string name,
        CancellationToken cancellationToken)
    {
        if (await db.UnitOrganizations.AnyAsync(value => value.Id == id ||
                (value.TenantId == tenantId && !value.IsDeleted && value.Name == name), cancellationToken))
            throw new UnitOrganizationConflictException("Organization ID or name already exists.");
        UnitOrganizationEntity organization = new() { Id = id, TenantId = tenantId, Name = name };
        db.UnitOrganizations.Add(organization);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await IncrementRevisionAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            throw new UnitOrganizationConflictException("Organization ID or name already exists.");
        }
        logger.LogInformation("Created organization {UnitOrganizationId} in tenant {TenantId}", id, tenantId);
        return Map(organization);
    }

    public async Task<ManagedUnitOrganization?> GetUnitOrganizationAsync(string tenantId, Guid id,
        CancellationToken cancellationToken) =>
        await db.UnitOrganizations.AsNoTracking().Where(value => value.TenantId == tenantId &&
                value.Id == id && !value.IsDeleted)
            .Select(value => new ManagedUnitOrganization(value.Id, value.TenantId, value.Name))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<ManagedUnitOrganization>> GetUnitOrganizationsAsync(string tenantId,
        CancellationToken cancellationToken) =>
        await db.UnitOrganizations.AsNoTracking().Where(value => value.TenantId == tenantId && !value.IsDeleted)
            .OrderBy(value => value.Name).Select(value => new ManagedUnitOrganization(value.Id,
                value.TenantId, value.Name)).ToArrayAsync(cancellationToken);

    public async Task<ManagedUnitOrganization?> UpdateUnitOrganizationAsync(string tenantId, Guid id, string name,
        CancellationToken cancellationToken)
    {
        UnitOrganizationEntity? organization = await db.UnitOrganizations.SingleOrDefaultAsync(value =>
            value.TenantId == tenantId && value.Id == id && !value.IsDeleted, cancellationToken);
        if (organization is null) return null;
        if (organization.Name == name) return Map(organization);
        if (await db.UnitOrganizations.AnyAsync(value => value.TenantId == tenantId &&
                value.Id != id && !value.IsDeleted && value.Name == name, cancellationToken))
            throw new UnitOrganizationConflictException("Organization name already exists.");
        organization.Name = name;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await IncrementRevisionAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            throw new UnitOrganizationConflictException("Organization name already exists.");
        }
        logger.LogInformation("Renamed organization {UnitOrganizationId}", id);
        return Map(organization);
    }

    public async Task<bool> DeleteUnitOrganizationAsync(string tenantId, Guid id, CancellationToken cancellationToken)
    {
        UnitOrganizationEntity? organization = await db.UnitOrganizations.SingleOrDefaultAsync(value =>
            value.TenantId == tenantId && value.Id == id, cancellationToken);
        if (organization is null) return false;
        if (organization.IsDeleted) return true;
        organization.IsDeleted = true;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await IncrementRevisionAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            throw;
        }
        logger.LogInformation("Deleted organization {UnitOrganizationId}", id);
        return true;
    }

    public Task<long> GetRevisionAsync(CancellationToken cancellationToken) =>
        db.Revisions.AsNoTracking().Where(value => value.Id == 1)
            .Select(value => value.Version).SingleAsync(cancellationToken);

    private Task<UserEntity?> FindUserAsync(string tenantId, Guid unitOrganizationId, string objectId,
        CancellationToken cancellationToken) =>
        db.Users.AsSplitQuery()
            .Include(value => value.Organizations.Where(membership =>
                membership.UnitOrganizationId == unitOrganizationId))
            .Include(value => value.Roles.Where(role => role.UnitOrganizationId == unitOrganizationId))
            .ThenInclude(value => value.Role)
            .SingleOrDefaultAsync(value => value.TenantId == tenantId && value.ObjectId == objectId, cancellationToken);

    private async Task<UnitOrganizationEntity> EnsureOrganizationAsync(string tenantId, Guid id, string name,
        CancellationToken cancellationToken)
    {
        UnitOrganizationEntity? organization = await db.UnitOrganizations.SingleOrDefaultAsync(value =>
            value.Id == id, cancellationToken);
        if (organization is not null)
        {
            if (organization.TenantId != tenantId)
                throw new ArgumentException("Organization belongs to a different tenant.", nameof(id));
            if (organization.IsDeleted)
                throw new UnitOrganizationConflictException("Organization ID was deleted and cannot be reused.");
            return organization;
        }
        if (await db.UnitOrganizations.AnyAsync(value => value.TenantId == tenantId &&
                !value.IsDeleted && value.Name == name,
                cancellationToken))
            throw new UnitOrganizationConflictException("Organization name already belongs to a different ID.");
        organization = new UnitOrganizationEntity { Id = id, TenantId = tenantId, Name = name };
        db.UnitOrganizations.Add(organization);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Created gateway organization {UnitOrganizationId} in tenant {TenantId}", id,
                tenantId);
            return organization;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            organization = await db.UnitOrganizations.SingleOrDefaultAsync(value => value.Id == id,
                cancellationToken);
            if (organization is not null && organization.TenantId != tenantId)
                throw new ArgumentException("Organization belongs to a different tenant.", nameof(id));
            if (organization is not null && organization.IsDeleted)
                throw new UnitOrganizationConflictException("Organization ID was deleted and cannot be reused.");
            if (organization is null)
                throw new UnitOrganizationConflictException("Organization name already belongs to a different ID.");
            return organization;
        }
    }

    private async Task<RoleEntity> EnsureReviewerAsync(string tenantId, Guid organizationId,
        CancellationToken cancellationToken)
    {
        RoleEntity? reviewer = await db.Roles.SingleOrDefaultAsync(value =>
            value.TenantId == tenantId && value.UnitOrganizationId == organizationId &&
            value.Code == UserRoles.Reviewer, cancellationToken);
        if (reviewer is not null)
        {
            return reviewer;
        }

        reviewer = new RoleEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UnitOrganizationId = organizationId,
            Code = UserRoles.Reviewer, DisplayName = "Reviewer"
        };
        db.Roles.Add(reviewer);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Created Reviewer role {RoleId} in organization {UnitOrganizationId}", reviewer.Id,
                organizationId);
            return reviewer;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            reviewer = await db.Roles.SingleOrDefaultAsync(value =>
                value.TenantId == tenantId && value.UnitOrganizationId == organizationId &&
                value.Code == UserRoles.Reviewer, cancellationToken);
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

    private static ManagedUser Map(UserEntity user, UnitOrganizationEntity organization) =>
        new(user.Id, user.TenantId, organization.Id, organization.Name, user.ObjectId, user.Email,
            user.DisplayName, user.IsActive, user.CreatedAt,
            user.Roles.Where(value => value.UnitOrganizationId == organization.Id && value.Role.IsActive)
                .Select(value => Map(value.Role)).OrderBy(value => value.Code, StringComparer.Ordinal).ToArray());

    private static ManagedRole Map(RoleEntity role) =>
        new(role.Id, role.TenantId, role.UnitOrganizationId, role.Code, role.DisplayName, role.IsActive);

    private static ManagedUnitOrganization Map(UnitOrganizationEntity organization) =>
        new(organization.Id, organization.TenantId, organization.Name);
}
