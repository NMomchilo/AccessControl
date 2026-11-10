using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UserManagement.Infrastructure.Database;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<UserManagementDbContext>
{
    public UserManagementDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__UserManagement")
            ?? throw new InvalidOperationException("Set ConnectionStrings__UserManagement before running EF migrations.");
        DbContextOptionsBuilder<UserManagementDbContext> options = new();
        options.UseSqlServer(connectionString);
        return new UserManagementDbContext(options.Options);
    }
}
