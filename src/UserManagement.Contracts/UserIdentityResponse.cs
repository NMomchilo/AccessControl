namespace UserManagement.Contracts;

public sealed record UserIdentityResponse(Guid UserId, bool IsActive, IReadOnlyList<string> Roles);
