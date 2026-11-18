using UserManagement.Contracts;

namespace UserManagement.Application;

public sealed partial class UserManagementService
{
    public async Task<AdminAccessInfo> GetAdminAccessAsync(string tenantId, string actorOid,
        Guid? unitOrganizationId, CancellationToken cancellationToken)
    {
        AdminAccess access = await store.GetAdminAccessAsync(NormalizeTenant(tenantId), NormalizeObjectId(actorOid),
            cancellationToken);
        return new AdminAccessInfo(access.IsActive, access.IsSuperAdmin, access.ManagedUnitOrganizationIds,
            access.IsActive && (access.IsSuperAdmin || unitOrganizationId.HasValue &&
                access.ManagedUnitOrganizationIds.Contains(NormalizeOrganizationId(unitOrganizationId.Value))));
    }

    public async Task RequireAdminAsync(string tenantId, string actorOid, Guid unitOrganizationId,
        CancellationToken cancellationToken)
    {
        if (!(await GetAdminAccessAsync(tenantId, actorOid, unitOrganizationId, cancellationToken)).CanManage)
            throw new UnauthorizedAccessException("Actor cannot manage this organization.");
    }

    public async Task RequireSuperAdminAsync(string tenantId, string actorOid, CancellationToken cancellationToken)
    {
        AdminAccessInfo access = await GetAdminAccessAsync(tenantId, actorOid, null, cancellationToken);
        if (!access.IsActive || !access.IsSuperAdmin)
            throw new UnauthorizedAccessException("Actor is not a SuperAdmin in this tenant.");
    }

    public Task<bool> CreateUserAsync(string tenantId, string objectId, string displayName, string? email,
        CancellationToken cancellationToken) => store.CreateUserAsync(NormalizeTenant(tenantId),
        NormalizeObjectId(objectId), NormalizeOrganizationName(displayName),
        NormalizeOptional(email, options.Value.MaximumEmailLength, nameof(email)), cancellationToken);

    public Task<bool> CreateUserInOrganizationAsync(string tenantId, Guid unitOrganizationId, string objectId,
        string displayName, string? email, CancellationToken cancellationToken, IReadOnlyList<Guid>? roleIds = null) =>
        store.CreateUserInOrganizationAsync(NormalizeTenant(tenantId), NormalizeOrganizationId(unitOrganizationId),
            NormalizeObjectId(objectId), NormalizeOrganizationName(displayName),
            NormalizeOptional(email, options.Value.MaximumEmailLength, nameof(email)), cancellationToken,
            ValidateNewUserRoleIds(roleIds));

    private IReadOnlyList<Guid> ValidateNewUserRoleIds(IReadOnlyList<Guid>? roleIds)
    {
        if (roleIds is null) return [];
        if (roleIds.Count > options.Value.MaximumRolesPerUser || roleIds.Any(id => id == Guid.Empty))
            throw new ArgumentException("Role IDs must be nonempty GUIDs within the configured limit.", nameof(roleIds));
        return roleIds.Distinct().ToArray();
    }

    public Task<bool> UpdateUserAsync(string tenantId, string objectId, string displayName, string? email,
        CancellationToken cancellationToken) => store.UpdateUserAsync(NormalizeTenant(tenantId),
        NormalizeObjectId(objectId), NormalizeOrganizationName(displayName),
        NormalizeOptional(email, options.Value.MaximumEmailLength, nameof(email)), cancellationToken);

    public Task<bool> DeleteUserAsync(string tenantId, string objectId, CancellationToken cancellationToken) =>
        store.DeleteUserAsync(NormalizeTenant(tenantId), NormalizeObjectId(objectId), cancellationToken);

    public Task<bool> AssignMembershipAsync(string tenantId, Guid unitOrganizationId, string objectId,
        CancellationToken cancellationToken) => store.AssignMembershipAsync(NormalizeTenant(tenantId),
        NormalizeOrganizationId(unitOrganizationId), NormalizeObjectId(objectId), cancellationToken);

    public Task<bool> RevokeMembershipAsync(string tenantId, Guid unitOrganizationId, string objectId,
        CancellationToken cancellationToken) => store.RevokeMembershipAsync(NormalizeTenant(tenantId),
        NormalizeOrganizationId(unitOrganizationId), NormalizeObjectId(objectId), cancellationToken);

    public Task<bool> SetSuperAdminAsync(string tenantId, string objectId, bool granted,
        CancellationToken cancellationToken) => store.SetSuperAdminAsync(NormalizeTenant(tenantId),
        NormalizeObjectId(objectId), granted, cancellationToken);

    public Task<bool> SetUnitAdminAsync(string tenantId, Guid unitOrganizationId, string objectId,
        bool granted, CancellationToken cancellationToken) => store.SetUnitAdminAsync(NormalizeTenant(tenantId),
        NormalizeOrganizationId(unitOrganizationId), NormalizeObjectId(objectId), granted, cancellationToken);

    public async Task<RoleInfo?> UpdateRoleAsync(string tenantId, Guid unitOrganizationId, Guid roleId,
        string displayName, CancellationToken cancellationToken)
    {
        if (roleId == Guid.Empty) throw new ArgumentException("Role ID is required.", nameof(roleId));
        var role = await store.UpdateRoleAsync(NormalizeTenant(tenantId), NormalizeOrganizationId(unitOrganizationId),
            roleId, NormalizeOrganizationName(displayName), cancellationToken);
        return role is null ? null : new RoleInfo(role.Id, role.TenantId, role.UnitOrganizationId,
            role.Code, role.DisplayName, role.IsActive);
    }

    public Task<bool> DeleteRoleAsync(string tenantId, Guid unitOrganizationId, Guid roleId,
        CancellationToken cancellationToken) => store.DeleteRoleAsync(NormalizeTenant(tenantId),
        NormalizeOrganizationId(unitOrganizationId), roleId == Guid.Empty
            ? throw new ArgumentException("Role ID is required.", nameof(roleId)) : roleId, cancellationToken);
}
