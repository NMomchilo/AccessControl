namespace UserManagement.Contracts;

public sealed record CreateUnitOrganizationRequest(Guid Id, string TenantId, string Name);
