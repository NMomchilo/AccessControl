using Common.Persistence;

namespace AccessControl.Infrastructure.Database;

public sealed class AuthorizationRevisionEntity : GeneralEntityBase
{
    public static readonly Guid SingletonId = Guid.Parse("a9acd89c-9a57-48c3-9dd1-222222222221");
    public long Version { get; set; }
}
