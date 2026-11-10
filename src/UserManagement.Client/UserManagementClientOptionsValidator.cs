using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace UserManagement.Client;

public sealed class UserManagementClientOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<UserManagementClientOptions>
{
    public ValidateOptionsResult Validate(string? name, UserManagementClientOptions options)
    {
        if (!Uri.TryCreate(options.ServiceUrl, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttps &&
                !(environment.IsDevelopment() && options.AllowInsecureHttpForDevelopment &&
                    uri.Scheme == Uri.UriSchemeHttp)))
        {
            return ValidateOptionsResult.Fail("User Management ServiceUrl must use HTTPS, except explicit Development HTTP.");
        }

        return ValidateOptionsResult.Success;
    }
}
