namespace UserManagement.Infrastructure.Database;

public sealed class UserRoleEntity
{
    public Guid UserId { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public Guid RoleId { get; set; }
    public UserEntity User { get; set; } = null!;
    public RoleEntity Role { get; set; } = null!;
}
