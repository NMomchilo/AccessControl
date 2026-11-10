namespace UserManagement.Contracts;

public sealed record UserIdentityRequest(string TenantId, string ObjectId, string? Email, string? DisplayName);
