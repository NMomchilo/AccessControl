namespace UserManagement.Contracts;

public sealed record UserIdentityResponse(Guid UserId, string TenantId, bool IsActive, IReadOnlyList<UserRoleInfo> Roles);
