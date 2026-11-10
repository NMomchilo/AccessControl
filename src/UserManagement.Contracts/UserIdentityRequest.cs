namespace UserManagement.Contracts;

public sealed record UserIdentityRequest(string ObjectId, string? Email, string? DisplayName);
