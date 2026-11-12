using AccessControl.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace AccessControl.Tests;

[CollectionDefinition("AccessControlEnvironment", DisableParallelization = true)]
public sealed class AccessControlEnvironmentCollection;

[Collection("AccessControlEnvironment")]
public sealed class AccessControlDesignTimeFactoryTests
{
    [Fact]
    public void FactoryRequiresConnectionStringAndConfiguresSqlServerWithoutConnecting()
    {
        const string variable = "ConnectionStrings__AccessControl";
        string? previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, null);
            DesignTimeDbContextFactory factory = new();
            Assert.Throws<InvalidOperationException>(() => factory.CreateDbContext([]));

            Environment.SetEnvironmentVariable(variable,
                "Server=localhost;Database=unused;User Id=sa;Password=unused-password;TrustServerCertificate=True");
            using AccessControlDbContext db = factory.CreateDbContext([]);
            Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", db.Database.ProviderName);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }
}
