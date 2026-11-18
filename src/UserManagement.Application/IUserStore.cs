using UserManagement.Domain;

namespace UserManagement.Application;

public interface IUserStore
{
    Task<AdminAccess> GetAdminAccessAsync(string tenantId, string objectId, CancellationToken cancellationToken);
    Task<bool> CreateUserAsync(string tenantId, string objectId, string displayName, string? email, CancellationToken cancellationToken);
    Task<bool> CreateUserInOrganizationAsync(string tenantId, Guid unitOrganizationId, string objectId,
        string displayName, string? email, CancellationToken cancellationToken, IReadOnlyList<Guid>? roleIds = null);
    Task<bool> UpdateUserAsync(string tenantId, string objectId, string displayName, string? email, CancellationToken cancellationToken);
    Task<bool> DeleteUserAsync(string tenantId, string objectId, CancellationToken cancellationToken);
    Task<bool> AssignMembershipAsync(string tenantId, Guid unitOrganizationId, string objectId, CancellationToken cancellationToken);
    Task<bool> RevokeMembershipAsync(string tenantId, Guid unitOrganizationId, string objectId, CancellationToken cancellationToken);
    Task<bool> SetSuperAdminAsync(string tenantId, string objectId, bool granted, CancellationToken cancellationToken);
    Task<bool> SetUnitAdminAsync(string tenantId, Guid unitOrganizationId, string objectId, bool granted, CancellationToken cancellationToken);
    Task<ManagedRole?> UpdateRoleAsync(string tenantId, Guid unitOrganizationId, Guid roleId, string displayName, CancellationToken cancellationToken);
    Task<bool> DeleteRoleAsync(string tenantId, Guid unitOrganizationId, Guid roleId, CancellationToken cancellationToken);
    Task<ManagedUser?> GetAsync(string tenantId, Guid unitOrganizationId, string objectId, CancellationToken cancellationToken);
    Task<PageSlice<ManagedUserSummary>> GetUsersAsync(string tenantId, Guid? unitOrganizationId,
        int offset, int pageSize, string? email, CancellationToken cancellationToken);
    Task<ManagedRole?> GetRoleAsync(string tenantId, Guid unitOrganizationId, Guid roleId,
        CancellationToken cancellationToken);
    Task<bool> ReplaceRolesAsync(string tenantId, Guid unitOrganizationId, string objectId,
        IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken);
    Task<bool> SetActiveAsync(string tenantId, string objectId, bool isActive, CancellationToken cancellationToken);
    Task<ManagedRole> CreateRoleAsync(string tenantId, Guid unitOrganizationId, string code, string displayName, CancellationToken cancellationToken);
    Task<bool> SetRoleActiveAsync(string tenantId, Guid unitOrganizationId, Guid roleId, bool isActive, CancellationToken cancellationToken);
    Task<PageSlice<ManagedRole>> GetRolesPageAsync(string tenantId, Guid? unitOrganizationId,
        int offset, int pageSize, CancellationToken cancellationToken);
    Task<ManagedUnitOrganization> CreateUnitOrganizationAsync(string tenantId, Guid id, string name, CancellationToken cancellationToken);
    Task<ManagedUnitOrganization?> GetUnitOrganizationAsync(string tenantId, Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ManagedUnitOrganization>> GetUnitOrganizationsAsync(string tenantId, CancellationToken cancellationToken);
    Task<ManagedUnitOrganization?> UpdateUnitOrganizationAsync(string tenantId, Guid id, string name, CancellationToken cancellationToken);
    Task<bool> DeleteUnitOrganizationAsync(string tenantId, Guid id, CancellationToken cancellationToken);
    Task<long> GetRevisionAsync(CancellationToken cancellationToken);
}
