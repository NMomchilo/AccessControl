namespace UserManagement.Infrastructure.Database;

public sealed class RoleEntity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<UserRoleEntity> Users { get; set; } = [];
}
