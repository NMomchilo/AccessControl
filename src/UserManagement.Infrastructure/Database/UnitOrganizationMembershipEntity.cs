using Common.Persistence;

namespace UserManagement.Infrastructure.Database;

public sealed class UnitOrganizationMembershipEntity : GeneralEntityBase
{
    public string TenantId { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public Guid UnitOrganizationId { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsAdmin { get; set; }
    public UserEntity User { get; set; } = null!;
    public UnitOrganizationEntity UnitOrganization { get; set; } = null!;
    public List<UserRoleEntity> Roles { get; set; } = [];
}
