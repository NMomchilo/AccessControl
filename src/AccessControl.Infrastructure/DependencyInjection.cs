using AccessControl.Application;
using AccessControl.Infrastructure.Caching;
using AccessControl.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AccessControl.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        string connectionString = configuration.GetConnectionString("AccessControl")
            ?? throw new InvalidOperationException("ConnectionStrings:AccessControl is required.");
        services.AddDbContext<AccessControlDbContext>(options => options.UseSqlServer(connectionString));
        services.AddOptions<AccessControlCacheOptions>()
            .Bind(configuration.GetSection(AccessControlCacheOptions.SectionName))
            .Validate(options => options.MaximumEntries > 0 &&
                options.EntryLifetime > TimeSpan.Zero && options.EntryLifetime <= TimeSpan.FromSeconds(5) &&
                options.RevisionPollInterval > TimeSpan.Zero && options.RevisionPollInterval <= options.EntryLifetime,
                "AccessControlCache requires a positive entry limit, an entry lifetime up to five seconds, and a poll interval no longer than that lifetime.")
            .ValidateOnStart();
        services.AddSingleton<AuthorizationCache>();
        services.AddScoped<SqlAuthorizationStore>();
        services.AddScoped<IEntitlementCatalog, SqlEntitlementCatalog>();
        services.AddScoped<IAuthorizationStore>(provider => new CachingAuthorizationStore(
            provider.GetRequiredService<SqlAuthorizationStore>(),
            provider.GetRequiredService<AuthorizationCache>(),
            provider.GetRequiredService<ILogger<CachingAuthorizationStore>>()));
        services.AddHostedService<AuthorizationCacheInvalidationService>();
        return services;
    }
}
