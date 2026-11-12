namespace UserManagement.Infrastructure.Database;

public sealed class UnitOrganizationMembershipEntity
{
    public string TenantId { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public Guid UnitOrganizationId { get; set; }
    public UserEntity User { get; set; } = null!;
    public UnitOrganizationEntity UnitOrganization { get; set; } = null!;
    public List<UserRoleEntity> Roles { get; set; } = [];
}
