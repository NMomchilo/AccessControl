namespace UserManagement.Domain;

public sealed record ManagedRole(Guid Id, string TenantId, string Code, string DisplayName);
