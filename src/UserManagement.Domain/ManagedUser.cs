namespace UserManagement.Domain;

public sealed record ManagedUser(Guid Id, string TenantId, Guid UnitOrganizationId, string UnitOrganizationName,
    string ObjectId, string? Email, string DisplayName,
    bool IsActive, DateTimeOffset CreatedAt, IReadOnlyList<ManagedRole> Roles);
