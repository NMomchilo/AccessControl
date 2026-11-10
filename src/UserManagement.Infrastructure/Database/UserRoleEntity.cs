namespace UserManagement.Infrastructure.Database;

public sealed class UserRoleEntity
{
    public Guid UserId { get; set; }
    public string Role { get; set; } = string.Empty;
    public UserEntity User { get; set; } = null!;
}
