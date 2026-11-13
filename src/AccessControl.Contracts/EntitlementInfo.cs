namespace AccessControl.Contracts;

public sealed record EntitlementInfo(Guid Id, string TenantId, Guid UnitOrganizationId,
    string Code, string DisplayName, bool IsActive);
