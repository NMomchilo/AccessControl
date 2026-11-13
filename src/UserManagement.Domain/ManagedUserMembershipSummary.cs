namespace UserManagement.Domain;

public sealed record ManagedUserMembershipSummary(Guid UnitOrganizationId, string UnitOrganizationName,
    IReadOnlyList<ManagedRole> Roles);
