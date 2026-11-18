using Common.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AdminManagement.Security;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEntraAdminAuthentication(this IServiceCollection services,
        IConfiguration configuration)
    {
        EntraAdminOptions settings = configuration.GetSection(EntraAdminOptions.SectionName)
            .Get<EntraAdminOptions>() ?? new();
        if (!EntraAdminOptions.IsValid(settings))
            throw new InvalidOperationException("Configure EntraAdmin:Authority (HTTPS) and Audience for administrator access tokens.");
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = settings.Authority;
                options.Audience = settings.Audience;
                options.MapInboundClaims = false;
                options.RequireHttpsMetadata = true;
                options.TokenValidationParameters.ValidateIssuer = true;
                options.TokenValidationParameters.ValidateAudience = true;
                options.TokenValidationParameters.ValidateLifetime = true;
            });
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentActorProvider, HttpContextActorProvider>();
        return services;
    }
}
