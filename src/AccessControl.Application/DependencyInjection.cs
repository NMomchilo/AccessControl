using Microsoft.Extensions.DependencyInjection;

namespace AccessControl.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthorizationService>();
        services.AddScoped<EntitlementCatalogService>();
        services.AddScoped<PolicyCatalogService>();
        return services;
    }
}
