namespace AccessControl.Domain;

public sealed record RoleEntitlement(string TenantId, string Role, string Entitlement);
