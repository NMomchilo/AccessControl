using Common.Persistence;

namespace UserManagement.Infrastructure.Database;

public sealed class UserManagementRevisionEntity : GeneralEntityBase
{
    public static readonly Guid SingletonId = Guid.Parse("a9acd89c-9a57-48c3-9dd1-222222222222");
    public long Version { get; set; }
}
