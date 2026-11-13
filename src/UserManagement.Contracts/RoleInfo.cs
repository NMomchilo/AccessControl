namespace UserManagement.Contracts;

public sealed record RoleInfo(Guid RoleId, string TenantId, Guid UnitOrganizationId, string Code, string DisplayName, bool IsActive = true);
