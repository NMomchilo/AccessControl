using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AccessControl.Infrastructure.Database;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AccessControlDbContext>
{
    public AccessControlDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__AccessControl")
            ?? throw new InvalidOperationException("Set ConnectionStrings__AccessControl before running EF migrations.");
        DbContextOptionsBuilder<AccessControlDbContext> options = new();
        options.UseSqlServer(connectionString);
        return new AccessControlDbContext(options.Options);
    }
}
