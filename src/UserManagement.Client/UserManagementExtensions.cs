using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace UserManagement.Client;

public static class UserManagementExtensions
{
    public static IServiceCollection AddUserManagement(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<UserManagementClientOptions>, UserManagementClientOptionsValidator>();
        services.AddOptions<UserManagementClientOptions>()
            .Bind(configuration.GetSection(UserManagementClientOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey) &&
                !string.IsNullOrWhiteSpace(options.TenantClaimType) &&
                !string.IsNullOrWhiteSpace(options.DefaultTenantId) &&
                options.DefaultTenantId.Length <= 128 &&
                !string.IsNullOrWhiteSpace(options.GatewayIdentityAuthenticationType) &&
                !string.IsNullOrWhiteSpace(options.UnitOrganizationIdClaimType) &&
                !string.IsNullOrWhiteSpace(options.UnitOrganizationNameClaimType) &&
                !string.IsNullOrWhiteSpace(options.RequiredDelegatedScope) &&
                !options.RequiredDelegatedScope.Contains(' ') && options.TimeoutSeconds > 0 &&
                options.MaximumCachedUsers > 0 && options.CacheLifetime > TimeSpan.Zero &&
                options.CacheLifetime <= TimeSpan.FromSeconds(5) &&
                options.RevisionPollInterval > TimeSpan.Zero &&
                options.RevisionPollInterval <= options.CacheLifetime,
                "UserManagement requires a service URL, API key, positive cache size, and cache lifetime up to five seconds.")
            .ValidateOnStart();
        services.AddHttpClient(nameof(HttpUserManagementClient), (provider, client) =>
            client.Timeout = TimeSpan.FromSeconds(provider.GetRequiredService<IOptions<UserManagementClientOptions>>()
                .Value.TimeoutSeconds));
        services.AddSingleton<HttpUserManagementClient>();
        services.AddSingleton<UserResolutionCache>(provider => new UserResolutionCache(
            provider.GetRequiredService<HttpUserManagementClient>(),
            provider.GetRequiredService<IOptions<UserManagementClientOptions>>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<UserResolutionCache>>()));
        services.AddSingleton<IUserManagementClient>(provider => provider.GetRequiredService<UserResolutionCache>());
        services.AddHostedService<UserManagementRevisionPoller>();
        return services;
    }

    public static IApplicationBuilder UseUserManagement(this IApplicationBuilder app) =>
        app.UseMiddleware<UserManagementMiddleware>();
}
