namespace UserManagement.Contracts;

public sealed record UserIdentityRequest(string TenantId, Guid UnitOrganizationId, string ObjectId);
