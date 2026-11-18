namespace AdminManagement.Security;

public sealed class EntraAdminOptions
{
    public const string SectionName = "EntraAdmin";
    public string Authority { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;

    public static bool IsValid(EntraAdminOptions value) =>
        Uri.TryCreate(value.Authority, UriKind.Absolute, out Uri? authority) &&
        authority.Scheme == Uri.UriSchemeHttps &&
        !string.IsNullOrWhiteSpace(value.Audience);
}
