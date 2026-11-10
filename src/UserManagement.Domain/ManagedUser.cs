namespace UserManagement.Domain;

public sealed record ManagedUser(Guid Id, string TenantId, string ObjectId, string? Email, string DisplayName,
    bool IsActive, DateTimeOffset CreatedAt, IReadOnlyList<ManagedRole> Roles);
