namespace AccessControl.Infrastructure.Database;

public sealed class AuthorizationRevisionEntity
{
    public int Id { get; set; }
    public long Version { get; set; }
}
