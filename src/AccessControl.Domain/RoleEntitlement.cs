namespace AccessControl.Domain;

public sealed record RoleEntitlement(string TenantId, Guid RoleId, Guid EntitlementId, Guid UnitOrganizationId);
