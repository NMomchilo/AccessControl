namespace UserManagement.Service.Security;

public sealed class UserManagementKeyOptions
{
    public const string SectionName = "UserManagementKeys";
    public List<ServiceCredential> Clients { get; set; } = [];
    public List<AdministratorCredential> Administrators { get; set; } = [];

    public static bool IsValid(UserManagementKeyOptions options)
    {
        if (options.Clients is null || options.Administrators is null ||
            options.Clients.Count == 0 || options.Administrators.Count == 0)
        {
            return false;
        }

        string[] keys = options.Clients.Select(value => value.Key)
            .Concat(options.Administrators.Select(value => value.Key)).ToArray();
        return options.Clients.All(value => !string.IsNullOrWhiteSpace(value.ClientId)) &&
            options.Administrators.All(value => !string.IsNullOrWhiteSpace(value.ActorId)) &&
            keys.All(value => !string.IsNullOrWhiteSpace(value) && value.Length >= 32) &&
            keys.Distinct(StringComparer.Ordinal).Count() == keys.Length &&
            options.Clients.Select(value => value.ClientId).Distinct(StringComparer.Ordinal).Count() == options.Clients.Count &&
            options.Administrators.Select(value => value.ActorId).Distinct(StringComparer.Ordinal).Count() == options.Administrators.Count;
    }
}
