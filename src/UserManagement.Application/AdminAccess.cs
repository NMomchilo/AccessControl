namespace UserManagement.Application;

public sealed record AdminAccess(bool IsActive, bool IsSuperAdmin, IReadOnlyList<Guid> ManagedUnitOrganizationIds);
