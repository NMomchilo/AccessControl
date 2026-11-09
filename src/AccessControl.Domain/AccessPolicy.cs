namespace AccessControl.Domain;

public sealed record AccessPolicy(string Module, string Resource, string Action, string Entitlement, string TenantId);
