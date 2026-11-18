namespace AccessControl.Service.Security;

internal sealed record UserManagementAdminAccessResponse(bool IsActive, bool IsSuperAdmin,
    IReadOnlyList<Guid> ManagedUnitOrganizationIds, bool CanManage);
