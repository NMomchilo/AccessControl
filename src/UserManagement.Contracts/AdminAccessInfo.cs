namespace UserManagement.Contracts;

public sealed record AdminAccessInfo(bool IsActive, bool IsSuperAdmin,
    IReadOnlyList<Guid> ManagedUnitOrganizationIds, bool CanManage);
