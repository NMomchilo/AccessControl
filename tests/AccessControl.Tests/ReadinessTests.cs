using System.Net;
using AccessControl.Infrastructure.Caching;
using AccessControl.Infrastructure.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace AccessControl.Tests;

public sealed class ReadinessTests
{
    [Theory]
    [InlineData(true, HttpStatusCode.OK)]
    [InlineData(false, HttpStatusCode.ServiceUnavailable)]
    public async Task ReadyReflectsDatabaseConnectivityAsync(bool databaseAvailable, HttpStatusCode expectedStatus)
    {
        await using ReadinessFactory factory = new(databaseAvailable);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/ready");

        Assert.Equal(expectedStatus, response.StatusCode);
    }

    private sealed class ReadinessFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection;

        public ReadinessFactory(bool databaseAvailable)
        {
            _connection = databaseAvailable
                ? new SqliteConnection("Data Source=:memory:")
                : new SqliteConnection($"Data Source={Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db")};Mode=ReadOnly");
            if (databaseAvailable) _connection.Open();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("AccessControlDatabase:ApplyMigrationsOnStartup", "false");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<AccessControlDbContext>();
                services.AddScoped(_ => new AccessControlDbContext(
                    new DbContextOptionsBuilder<AccessControlDbContext>().UseSqlite(_connection).Options));
                foreach (ServiceDescriptor descriptor in services.Where(descriptor =>
                    descriptor.ServiceType == typeof(IHostedService) &&
                    descriptor.ImplementationType == typeof(AuthorizationCacheInvalidationService)).ToArray())
                    services.Remove(descriptor);
            });
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
