namespace UserManagement.Contracts;

public sealed record RoleInfo(Guid RoleId, string TenantId, string Code, string DisplayName);
