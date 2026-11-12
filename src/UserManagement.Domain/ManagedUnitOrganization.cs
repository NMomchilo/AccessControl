namespace UserManagement.Domain;

public sealed record ManagedUnitOrganization(Guid Id, string TenantId, string Name);
