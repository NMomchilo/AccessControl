namespace UserManagement.Contracts;

public sealed record UserSummary(Guid UserId, string ObjectId, string DisplayName, string? Email, bool IsActive,
    IReadOnlyList<UserMembershipSummary> Memberships);
