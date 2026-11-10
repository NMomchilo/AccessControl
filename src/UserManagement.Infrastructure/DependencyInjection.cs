using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UserManagement.Application;
using UserManagement.Infrastructure.Database;

namespace UserManagement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddUserManagementInfrastructure(this IServiceCollection services,
        IConfiguration configuration)
    {
        string connectionString = configuration.GetConnectionString("UserManagement")
            ?? throw new InvalidOperationException("ConnectionStrings:UserManagement is required.");
        services.AddDbContext<UserManagementDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IUserStore, SqlUserStore>();
        return services;
    }
}
