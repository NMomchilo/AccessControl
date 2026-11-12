using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AccessControl.Client;

public static class AccessControlExtensions
{
    public static IServiceCollection AddAccessControl(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AccessControlClientOptions>()
            .Bind(configuration.GetSection(AccessControlClientOptions.SectionName))
            .Validate(options => Uri.TryCreate(options.ServiceUrl, UriKind.Absolute, out Uri? uri) &&
                uri.Scheme == Uri.UriSchemeHttps &&
                !string.IsNullOrWhiteSpace(options.ApiKey) &&
                !string.IsNullOrWhiteSpace(options.Module) &&
                !string.IsNullOrWhiteSpace(options.TenantClaimType) &&
                !string.IsNullOrWhiteSpace(options.DefaultTenantId) &&
                !string.IsNullOrWhiteSpace(options.RoleIdClaimType) &&
                !string.IsNullOrWhiteSpace(options.RoleIdentityAuthenticationType) &&
                options.ExcludedPaths is not null &&
                options.TimeoutSeconds > 0,
                "AccessControl requires HTTPS ServiceUrl, ApiKey, Module, tenant settings and a positive timeout.")
            .ValidateOnStart();
        services.AddHttpClient(nameof(HttpAccessControlDecisionClient), (provider, client) =>
            client.Timeout = TimeSpan.FromSeconds(provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AccessControlClientOptions>>().Value.TimeoutSeconds));
        services.AddSingleton<IAccessControlDecisionClient, HttpAccessControlDecisionClient>();
        return services;
    }

    public static IApplicationBuilder UseAccessControl(this IApplicationBuilder app)
    {
        return app.UseMiddleware<AccessControlMiddleware>();
    }

    public static IEndpointRouteBuilder ExportAccessControlCatalog(this IEndpointRouteBuilder app)
    {
        EndpointCatalog.Export(app);
        return app;
    }
}
