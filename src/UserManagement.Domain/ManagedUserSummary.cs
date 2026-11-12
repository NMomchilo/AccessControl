namespace UserManagement.Domain;

public sealed record ManagedUserSummary(Guid Id, string ObjectId, string DisplayName, string? Email, bool IsActive);
