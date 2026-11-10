namespace UserManagement.Contracts;

public sealed record ReplaceRolesRequest(IReadOnlyList<Guid> RoleIds);
