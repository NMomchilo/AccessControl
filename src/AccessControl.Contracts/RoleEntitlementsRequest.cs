namespace AccessControl.Contracts;

public sealed record RoleEntitlementsRequest(string TenantId, string Role, IReadOnlyList<string> Entitlements);
