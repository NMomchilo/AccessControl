namespace UserManagement.Contracts;

public sealed record CreateRoleRequest(string TenantId, string Code, string DisplayName);
