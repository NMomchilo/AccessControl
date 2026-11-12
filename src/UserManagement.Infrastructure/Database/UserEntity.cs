namespace UserManagement.Infrastructure.Database;

public sealed class UserEntity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string ObjectId { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public List<UserRoleEntity> Roles { get; set; } = [];
    public List<UnitOrganizationMembershipEntity> Organizations { get; set; } = [];
}
