namespace UserManagement.Domain;

public sealed record ManagedRole(Guid Id, string TenantId, Guid UnitOrganizationId, string Code, string DisplayName);
