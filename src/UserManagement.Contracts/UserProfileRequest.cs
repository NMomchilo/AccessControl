namespace UserManagement.Contracts;

public sealed record UserProfileRequest(string DisplayName, string? Email);
