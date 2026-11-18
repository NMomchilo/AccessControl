using Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UserManagement.Application;
using UserManagement.Domain;

namespace UserManagement.Infrastructure.Database;

public sealed partial class SqlUserStore(UserManagementDbContext db, ILogger<SqlUserStore> logger) : IUserStore
{
    public async Task<ManagedUser?> GetAsync(string tenantId, Guid unitOrganizationId, string objectId,
        CancellationToken cancellationToken)
    {
        UserEntity? user = await db.Users.AsNoTracking().AsSplitQuery()
            .Include(value => value.Organizations.Where(membership =>
                membership.UnitOrganizationId == unitOrganizationId))
            .Include(value => value.Roles.Where(role => role.UnitOrganizationId == unitOrganizationId))
            .ThenInclude(value => value.Role)
            .SingleOrDefaultAsync(value => value.TenantId == tenantId && value.ObjectId == objectId, cancellationToken);
        if (user is null || user.IsDeleted || !user.Organizations.Any(value => value.UnitOrganizationId == unitOrganizationId && !value.IsDeleted))
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
        IQueryable<UserEntity> query = db.Users.AsNoTracking().Where(user => user.TenantId == tenantId && !user.IsDeleted);
        if (email is not null)
            query = query.Where(user => user.NormalizedEmail == email);
        if (unitOrganizationId.HasValue)
        {
            Guid organizationId = unitOrganizationId.Value;
            query = query.Where(user => user.Organizations.Any(membership =>
                membership.UnitOrganizationId == organizationId && !membership.IsDeleted));
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
                !membership.UnitOrganization.IsDeleted && !membership.IsDeleted &&
                (!unitOrganizationId.HasValue || membership.UnitOrganizationId == unitOrganizationId.Value))
            .Select(membership => new { membership.UserId, membership.UnitOrganizationId,
                OrganizationName = membership.UnitOrganization.Name }).ToArrayAsync(cancellationToken);
        var assignments = await db.UserRoles.AsNoTracking()
            .Where(assignment => assignment.TenantId == tenantId && userIds.Contains(assignment.UserId) &&
                !assignment.Membership.IsDeleted && !assignment.Role.IsDeleted &&
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
                !role.UnitOrganization.IsDeleted && !role.IsDeleted)
            .Select(role => new ManagedRole(role.Id, role.TenantId, role.UnitOrganizationId,
                role.Code, role.DisplayName, role.IsActive))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> ReplaceRolesAsync(string tenantId, Guid unitOrganizationId, string objectId,
        IReadOnlyList<Guid> roleIds,
        CancellationToken cancellationToken)
    {
        UserEntity? user = await db.Users.AsNoTracking().SingleOrDefaultAsync(value =>
            value.TenantId == tenantId && value.ObjectId == objectId && !value.IsDeleted, cancellationToken);
        if (user is null || !await db.UnitOrganizations.AnyAsync(value => value.TenantId == tenantId &&
                value.Id == unitOrganizationId && !value.IsDeleted, cancellationToken) ||
            !await db.UnitOrganizationMemberships.AnyAsync(value =>
                value.TenantId == tenantId && value.UserId == user.Id &&
                value.UnitOrganizationId == unitOrganizationId && !value.IsDeleted, cancellationToken))
        {
            return false;
        }

        Guid[] distinct = await ValidateAssignableRolesAsync(tenantId, unitOrganizationId, roleIds,
            cancellationToken);

        // ExecuteDelete bypasses change tracking; detach any roles loaded by an earlier resolution.
        db.ChangeTracker.Clear();
        await using var transaction = await BeginTransactionIfNeededAsync(cancellationToken);
        await db.UserRoles.Where(value => value.UserId == user.Id &&
            value.UnitOrganizationId == unitOrganizationId).ExecuteDeleteAsync(cancellationToken);
        db.UserRoles.AddRange(distinct.Select(roleId => new UserRoleEntity
        {
            TenantId = tenantId, UserId = user.Id, UnitOrganizationId = unitOrganizationId, RoleId = roleId
        }));
        await db.SaveChangesAsync(cancellationToken);
        await IncrementRevisionAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();
        logger.LogInformation("Replaced roles for user {UserId} in organization {UnitOrganizationId}; count {RoleCount}",
            user.Id, unitOrganizationId, distinct.Length);
        return true;
    }

    private async Task<Guid[]> ValidateAssignableRolesAsync(string tenantId, Guid unitOrganizationId,
        IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken)
    {
        Guid[] distinct = roleIds.Distinct().ToArray();
        if (distinct.Any(id => id == Guid.Empty))
            throw new ArgumentException("Role IDs must be nonempty GUIDs.", nameof(roleIds));
        if (distinct.Length == 0) return distinct;
        int validCount = await db.Roles.CountAsync(value => value.TenantId == tenantId &&
            value.UnitOrganizationId == unitOrganizationId && value.IsActive && !value.IsDeleted &&
            distinct.Contains(value.Id), cancellationToken);
        if (validCount != distinct.Length)
            throw new ArgumentException("Every role ID must be active in the user's organization.", nameof(roleIds));
        return distinct;
    }

    public async Task<bool> SetActiveAsync(string tenantId, string objectId, bool isActive,
        CancellationToken cancellationToken)
    {
        UserEntity? user = await db.Users.SingleOrDefaultAsync(value =>
            value.TenantId == tenantId && value.ObjectId == objectId && !value.IsDeleted, cancellationToken);
        if (user is null)
        {
            return false;
        }

        await using var transaction = await BeginTransactionIfNeededAsync(cancellationToken);
        if (!isActive && user.IsActive && user.IsSuperAdmin)
            await EnsureAnotherSuperAdminAsync(tenantId, cancellationToken);
        user.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);
        if (isActive || !user.IsSuperAdmin) await IncrementRevisionAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
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
            !value.UnitOrganization.IsDeleted && !value.IsDeleted, cancellationToken);
        if (role is null) return false;
        if (role.IsActive == isActive) return true;
        await using var transaction = await BeginTransactionIfNeededAsync(cancellationToken);
        role.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);
        await IncrementRevisionAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Role {RoleId} active status changed to {IsActive} in tenant {TenantId}, organization {UnitOrganizationId}",
            roleId, isActive, tenantId, unitOrganizationId);
        return true;
    }

    public async Task<PageSlice<ManagedRole>> GetRolesPageAsync(string tenantId, Guid? unitOrganizationId,
        int offset, int pageSize, CancellationToken cancellationToken)
    {
        IQueryable<RoleEntity> query = db.Roles.AsNoTracking().Where(role => role.TenantId == tenantId &&
            !role.UnitOrganization.IsDeleted && !role.IsDeleted);
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
            await using var transaction = await BeginTransactionIfNeededAsync(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await IncrementRevisionAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
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
            await using var transaction = await BeginTransactionIfNeededAsync(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await IncrementRevisionAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
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
            await using var transaction = await BeginTransactionIfNeededAsync(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await IncrementRevisionAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
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
        db.Revisions.AsNoTracking().Where(value => value.Id == UserManagementRevisionEntity.SingletonId)
            .Select(value => value.Version).SingleAsync(cancellationToken);

    private async Task IncrementRevisionAsync(CancellationToken cancellationToken)
    {
        DateTime now = DateTime.UtcNow;
        string actor = db.RequiredActorId;
        int affected = await db.Revisions.Where(value => value.Id == UserManagementRevisionEntity.SingletonId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Version,
                value => value.Version + 1).SetProperty(value => value.LastUpdateUser, actor)
                .SetProperty(value => value.LastUpdateTime, now), cancellationToken);
        if (affected != 1)
        {
            throw new InvalidOperationException("User Management revision row is missing.");
        }
    }

    private static ManagedUser Map(UserEntity user, UnitOrganizationEntity organization) =>
        new(user.Id, user.TenantId, organization.Id, organization.Name, user.ObjectId, user.Email,
            user.DisplayName, user.IsActive && !user.IsDeleted && user.Organizations.Any(membership =>
                membership.UnitOrganizationId == organization.Id && !membership.IsDeleted), user.CreatedAt,
            user.Roles.Where(value => value.UnitOrganizationId == organization.Id && value.Role.IsActive &&
                !value.Role.IsDeleted && !user.IsDeleted && user.Organizations.Any(membership =>
                    membership.UnitOrganizationId == organization.Id && !membership.IsDeleted))
                .Select(value => Map(value.Role)).OrderBy(value => value.Code, StringComparer.Ordinal).ToArray());

    private static ManagedRole Map(RoleEntity role) =>
        new(role.Id, role.TenantId, role.UnitOrganizationId, role.Code, role.DisplayName, role.IsActive);

    private static ManagedUnitOrganization Map(UnitOrganizationEntity organization) =>
        new(organization.Id, organization.TenantId, organization.Name);
}
