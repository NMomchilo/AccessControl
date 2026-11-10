namespace UserManagement.Contracts;

public sealed record ReplaceRolesRequest(IReadOnlyList<string> Roles);
