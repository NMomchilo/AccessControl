namespace AccessControl.Service.Security;

public sealed class UserManagementAdminLookupOptions
{
    public const string SectionName = "UserManagementAdminLookup";
    public string ServiceUrl { get; set; } = string.Empty;
    public string LookupKey { get; set; } = string.Empty;

    public static bool IsValid(UserManagementAdminLookupOptions options) =>
        Uri.TryCreate(options.ServiceUrl, UriKind.Absolute, out Uri? uri) &&
        uri.Scheme is "http" or "https" && options.LookupKey.Length >= 32;
}
