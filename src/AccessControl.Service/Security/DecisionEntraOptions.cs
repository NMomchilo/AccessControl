namespace AccessControl.Service.Security;

public sealed class DecisionEntraOptions
{
    public const string SectionName = "DecisionEntra";
    public string Authority { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string AuthenticationScheme { get; set; } = "DecisionEntra";

    public static bool IsValid(DecisionEntraOptions options) =>
        Uri.TryCreate(options.Authority, UriKind.Absolute, out Uri? uri) &&
        uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrWhiteSpace(options.Audience) &&
        !string.IsNullOrWhiteSpace(options.AuthenticationScheme);
}
