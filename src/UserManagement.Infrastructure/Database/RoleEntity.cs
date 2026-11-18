using Common.Persistence;

namespace UserManagement.Infrastructure.Database;

public sealed class RoleEntity : GeneralEntityBase
{
    public string TenantId { get; set; } = string.Empty;
    public Guid UnitOrganizationId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public List<UserRoleEntity> Users { get; set; } = [];
    public UnitOrganizationEntity UnitOrganization { get; set; } = null!;
}
