using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        services.AddHttpContextAccessor();
        services.AddSingleton<IAccessControlDecisionClient, HttpAccessControlDecisionClient>();
        return services;
    }

    public static IApplicationBuilder UseAccessControl(this IApplicationBuilder app)
    {
        return app.UseMiddleware<AccessControlMiddleware>();
    }

    public static async Task RegisterAccessControlCatalogAsync(this IEndpointRouteBuilder app,
        AccessControlCatalogRegistrationOptions registration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);
        if (!IsValidRegistration(registration))
            throw new ArgumentException("Registration requires a tenant, unit organization ID/name, and module name.",
                nameof(registration));
        AccessControlClientOptions options = app.ServiceProvider.GetRequiredService<IOptions<AccessControlClientOptions>>().Value;
        IConfiguration configuration = app.ServiceProvider.GetRequiredService<IConfiguration>();
        string userManagementUrl = configuration["UserManagement:ServiceUrl"]
            ?? throw new InvalidOperationException("UserManagement:ServiceUrl is required for registration.");
        string userManagementKey = configuration["UserManagement:ApiKey"]
            ?? throw new InvalidOperationException("UserManagement:ApiKey is required for registration.");
        IHostEnvironment environment = app.ServiceProvider.GetRequiredService<IHostEnvironment>();
        if (userManagementKey.Length < 32 ||
            !Uri.TryCreate(userManagementUrl, UriKind.Absolute, out Uri? userManagementUri) ||
            !(userManagementUri.Scheme == Uri.UriSchemeHttps || environment.IsDevelopment() &&
              configuration.GetValue<bool>("UserManagement:AllowInsecureHttpForDevelopment") &&
              userManagementUri.Scheme == Uri.UriSchemeHttp && userManagementUri.IsLoopback))
            throw new InvalidOperationException("Registration requires a configured User Management service and client key.");

        IHttpClientFactory factory = app.ServiceProvider.GetRequiredService<IHttpClientFactory>();
        using HttpClient users = factory.CreateClient();
        using HttpClient access = factory.CreateClient();
        users.BaseAddress = userManagementUri;
        access.BaseAddress = new Uri(options.ServiceUrl);
        users.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        access.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        await EndpointCatalog.RegisterAsync(app.DataSources.SelectMany(source => source.Endpoints), options,
            registration, userManagementKey, users, access, cancellationToken);
        app.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AccessControl.EndpointCatalog")
            .LogInformation("Registered endpoint catalog for {TenantId} {UnitOrganizationId} {Module}",
                registration.TenantId, registration.UnitOrganizationId, options.Module);
    }

    private static bool IsValidRegistration(AccessControlCatalogRegistrationOptions registration) =>
        !string.IsNullOrWhiteSpace(registration.TenantId) && registration.TenantId.Length <= 128 &&
        registration.UnitOrganizationId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(registration.UnitOrganizationName) &&
        registration.UnitOrganizationName.Length <= 200 &&
        !string.IsNullOrWhiteSpace(registration.ModuleName) && registration.ModuleName.Length <= 200;
}
