namespace AccessControl.Domain;

public sealed record RoleEntitlement(string TenantId, Guid RoleId, string Entitlement, Guid UnitOrganizationId);
