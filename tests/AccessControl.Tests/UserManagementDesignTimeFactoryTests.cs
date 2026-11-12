using UserManagement.Infrastructure.Database;

namespace AccessControl.Tests;

[CollectionDefinition("User Management design-time environment", DisableParallelization = true)]
public sealed class UserManagementDesignTimeEnvironmentCollection;

[Collection("User Management design-time environment")]
public sealed class UserManagementDesignTimeFactoryTests
{
    [Fact]
    public void Factory_RequiresConnectionStringAndConfiguresSqlServerWithoutConnecting()
    {
        const string key = "ConnectionStrings__UserManagement";
        string? previous = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, null);
            DesignTimeDbContextFactory factory = new();
            Assert.Throws<InvalidOperationException>(() => factory.CreateDbContext([]));

            Environment.SetEnvironmentVariable(key,
                "Server=localhost;Database=DesignTimeOnly;User Id=sa;Password=unused;TrustServerCertificate=True");
            using UserManagementDbContext db = factory.CreateDbContext([]);
            Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", db.Database.ProviderName);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, previous);
        }
    }
}
