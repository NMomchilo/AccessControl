using Common.Persistence;

namespace UserManagement.Infrastructure.Database;

public sealed class UnitOrganizationEntity : GeneralEntityBase
{
    public string TenantId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public List<RoleEntity> Roles { get; set; } = [];
    public List<UnitOrganizationMembershipEntity> Members { get; set; } = [];
}
