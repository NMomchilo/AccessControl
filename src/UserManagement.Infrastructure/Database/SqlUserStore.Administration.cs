using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using UserManagement.Application;
using UserManagement.Domain;

namespace UserManagement.Infrastructure.Database;

public sealed partial class SqlUserStore
{
    public async Task<AdminAccess> GetAdminAccessAsync(string tenantId, string objectId, CancellationToken cancellationToken)
    {
        UserEntity? user = await db.Users.AsNoTracking().SingleOrDefaultAsync(value =>
            value.TenantId == tenantId && value.ObjectId == objectId, cancellationToken);
        if (user is null || !user.IsActive || user.IsDeleted)
            return new AdminAccess(false, false, []);
        Guid[] units = await db.UnitOrganizationMemberships.AsNoTracking()
            .Where(value => value.TenantId == tenantId && value.UserId == user.Id &&
                value.IsAdmin && !value.IsDeleted && !value.UnitOrganization.IsDeleted)
            .OrderBy(value => value.UnitOrganizationId)
            .Select(value => value.UnitOrganizationId).ToArrayAsync(cancellationToken);
        return new AdminAccess(true, user.IsSuperAdmin, units);
    }

    public async Task<bool> CreateUserAsync(string tenantId, string objectId, string displayName, string? email,
        CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(value => value.TenantId == tenantId && value.ObjectId == objectId,
                cancellationToken)) return false;
        db.Users.Add(new UserEntity { Id = Guid.NewGuid(), TenantId = tenantId, ObjectId = objectId,
            DisplayName = displayName, Email = email, CreatedAt = DateTimeOffset.UtcNow });
        await SaveRevisionAsync(cancellationToken);
        return true;
    }

    public async Task<bool> CreateUserInOrganizationAsync(string tenantId, Guid unitOrganizationId,
        string objectId, string displayName, string? email, CancellationToken cancellationToken,
        IReadOnlyList<Guid>? roleIds = null)
    {
        if (!await db.UnitOrganizations.AnyAsync(value => value.TenantId == tenantId &&
                value.Id == unitOrganizationId && !value.IsDeleted, cancellationToken) ||
            await db.Users.AnyAsync(value => value.TenantId == tenantId && value.ObjectId == objectId,
                cancellationToken)) return false;
        Guid[] assignedRoles = await ValidateAssignableRolesAsync(tenantId, unitOrganizationId, roleIds ?? [],
            cancellationToken);
        await using var transaction = await BeginTransactionIfNeededAsync(cancellationToken);
        UserEntity user = new() { Id = Guid.NewGuid(), TenantId = tenantId, ObjectId = objectId,
            DisplayName = displayName, Email = email, CreatedAt = DateTimeOffset.UtcNow };
        user.Organizations.Add(new UnitOrganizationMembershipEntity
        {
            TenantId = tenantId, UserId = user.Id, UnitOrganizationId = unitOrganizationId,
            Roles = assignedRoles.Select(roleId => new UserRoleEntity
            { TenantId = tenantId, UserId = user.Id, UnitOrganizationId = unitOrganizationId, RoleId = roleId }).ToList()
        });
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        await IncrementRevisionAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UpdateUserAsync(string tenantId, string objectId, string displayName, string? email,
        CancellationToken cancellationToken)
    {
        UserEntity? user = await db.Users.SingleOrDefaultAsync(value => value.TenantId == tenantId &&
            value.ObjectId == objectId && !value.IsDeleted, cancellationToken);
        if (user is null) return false;
        user.DisplayName = displayName;
        user.Email = email;
        await SaveRevisionAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteUserAsync(string tenantId, string objectId, CancellationToken cancellationToken)
    {
        UserEntity? user = await db.Users.SingleOrDefaultAsync(value => value.TenantId == tenantId &&
            value.ObjectId == objectId && !value.IsDeleted, cancellationToken);
        if (user is null) return false;
        if (user.IsSuperAdmin && user.IsActive)
        {
            await using var transaction = await BeginTransactionIfNeededAsync(cancellationToken);
            await EnsureAnotherSuperAdminAsync(tenantId, cancellationToken);
            user.IsDeleted = true;
            user.IsActive = false;
            user.IsSuperAdmin = false;
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return true;
        }
        user.IsDeleted = true;
        user.IsActive = false;
        user.IsSuperAdmin = false;
        await SaveRevisionAsync(cancellationToken);
        return true;
    }

    public async Task<bool> AssignMembershipAsync(string tenantId, Guid unitOrganizationId, string objectId,
        CancellationToken cancellationToken)
    {
        UserEntity? user = await db.Users.SingleOrDefaultAsync(value => value.TenantId == tenantId &&
            value.ObjectId == objectId && !value.IsDeleted, cancellationToken);
        if (user is null || !await db.UnitOrganizations.AnyAsync(value => value.TenantId == tenantId &&
                value.Id == unitOrganizationId && !value.IsDeleted, cancellationToken)) return false;
        UnitOrganizationMembershipEntity? membership = await db.UnitOrganizationMemberships.SingleOrDefaultAsync(value =>
            value.TenantId == tenantId && value.UserId == user.Id && value.UnitOrganizationId == unitOrganizationId,
            cancellationToken);
        if (membership is not null)
        {
            if (membership.IsDeleted)
            {
                await using var transaction = await BeginTransactionIfNeededAsync(cancellationToken);
                UserRoleEntity[] oldRoles = await db.UserRoles.Where(value => value.TenantId == tenantId &&
                    value.UserId == user.Id && value.UnitOrganizationId == unitOrganizationId)
                    .ToArrayAsync(cancellationToken);
                db.UserRoles.RemoveRange(oldRoles);
                membership.IsDeleted = false;
                membership.IsAdmin = false;
                await db.SaveChangesAsync(cancellationToken);
                await IncrementRevisionAsync(cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            }
            return true;
        }
        db.UnitOrganizationMemberships.Add(new UnitOrganizationMembershipEntity
        {
            TenantId = tenantId, UserId = user.Id, UnitOrganizationId = unitOrganizationId
        });
        await SaveRevisionAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RevokeMembershipAsync(string tenantId, Guid unitOrganizationId, string objectId,
        CancellationToken cancellationToken)
    {
        UnitOrganizationMembershipEntity? membership = await db.UnitOrganizationMemberships
            .SingleOrDefaultAsync(value => value.TenantId == tenantId && value.UnitOrganizationId == unitOrganizationId &&
                value.User.ObjectId == objectId && !value.IsDeleted, cancellationToken);
        if (membership is null) return false;
        membership.IsDeleted = true;
        membership.IsAdmin = false;
        await SaveRevisionAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SetSuperAdminAsync(string tenantId, string objectId, bool granted,
        CancellationToken cancellationToken)
    {
        UserEntity? user = await db.Users.SingleOrDefaultAsync(value => value.TenantId == tenantId &&
            value.ObjectId == objectId && !value.IsDeleted, cancellationToken);
        if (user is null) return false;
        if (!granted && user.IsSuperAdmin)
        {
            await using var transaction = await BeginTransactionIfNeededAsync(cancellationToken);
            if (user.IsActive) await EnsureAnotherSuperAdminAsync(tenantId, cancellationToken);
            else await IncrementRevisionAsync(cancellationToken);
            user.IsSuperAdmin = false;
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return true;
        }
        user.IsSuperAdmin = granted;
        await SaveRevisionAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SetUnitAdminAsync(string tenantId, Guid unitOrganizationId, string objectId,
        bool granted, CancellationToken cancellationToken)
    {
        UnitOrganizationMembershipEntity? membership = await db.UnitOrganizationMemberships.SingleOrDefaultAsync(value =>
            value.TenantId == tenantId && value.UnitOrganizationId == unitOrganizationId &&
            value.User.ObjectId == objectId && !value.User.IsDeleted && !value.IsDeleted &&
            !value.UnitOrganization.IsDeleted, cancellationToken);
        if (membership is null) return false;
        membership.IsAdmin = granted;
        await SaveRevisionAsync(cancellationToken);
        return true;
    }

    public async Task<ManagedRole?> UpdateRoleAsync(string tenantId, Guid unitOrganizationId, Guid roleId,
        string displayName, CancellationToken cancellationToken)
    {
        RoleEntity? role = await db.Roles.SingleOrDefaultAsync(value => value.TenantId == tenantId &&
            value.UnitOrganizationId == unitOrganizationId && value.Id == roleId && !value.IsDeleted &&
            !value.UnitOrganization.IsDeleted, cancellationToken);
        if (role is null) return null;
        role.DisplayName = displayName;
        await SaveRevisionAsync(cancellationToken);
        return Map(role);
    }

    public async Task<bool> DeleteRoleAsync(string tenantId, Guid unitOrganizationId, Guid roleId,
        CancellationToken cancellationToken)
    {
        RoleEntity? role = await db.Roles.SingleOrDefaultAsync(value => value.TenantId == tenantId &&
            value.UnitOrganizationId == unitOrganizationId && value.Id == roleId && !value.IsDeleted,
            cancellationToken);
        if (role is null) return false;
        role.IsDeleted = true;
        role.IsActive = false;
        await SaveRevisionAsync(cancellationToken);
        return true;
    }

    private async Task SaveRevisionAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionIfNeededAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await IncrementRevisionAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    private async Task EnsureAnotherSuperAdminAsync(string tenantId, CancellationToken cancellationToken)
    {
        await IncrementRevisionAsync(cancellationToken);
        int activeAdmins = await db.Users.CountAsync(value => value.TenantId == tenantId &&
            value.IsSuperAdmin && value.IsActive && !value.IsDeleted, cancellationToken);
        if (activeAdmins <= 1)
            throw new AdminDutyConflictException("The last active SuperAdmin cannot be removed.");
    }

    private async Task<IDbContextTransaction?> BeginTransactionIfNeededAsync(CancellationToken cancellationToken) =>
        db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
}

