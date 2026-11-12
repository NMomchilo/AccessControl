namespace UserManagement.Contracts;

public sealed record UserIdentityResponse(Guid UserId, string TenantId, Guid UnitOrganizationId,
    string UnitOrganizationName, bool IsActive, IReadOnlyList<UserRoleInfo> Roles);
