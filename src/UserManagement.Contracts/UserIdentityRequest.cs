namespace UserManagement.Contracts;

public sealed record UserIdentityRequest(string TenantId, Guid UnitOrganizationId, string UnitOrganizationName,
    string ObjectId, string? Email, string? DisplayName);
