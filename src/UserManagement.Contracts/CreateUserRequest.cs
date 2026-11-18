namespace UserManagement.Contracts;

public sealed record CreateUserRequest(string DisplayName, string? Email, IReadOnlyList<Guid>? RoleIds = null);
