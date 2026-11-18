using Microsoft.Extensions.DependencyInjection;

namespace UserManagement.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddUserManagementApplication(this IServiceCollection services)
    {
        services.AddScoped<UserManagementService>();
        services.AddScoped<IUserManagementAdminAuthorizer, UserManagementAdminAuthorizer>();
        return services;
    }
}
