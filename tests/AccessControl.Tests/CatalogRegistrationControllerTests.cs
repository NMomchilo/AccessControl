using AccessControl.Contracts;
using AccessControl.Infrastructure.Caching;
using AccessControl.Infrastructure.Database;
using AccessControl.Service.Controllers;
using AccessControl.Service.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AccessControl.Tests;

public sealed class CatalogRegistrationControllerTests
{
    [Fact]
    public async Task OptedInClientRegistersOrganizationProvidedByApiAsync()
    {
        Guid organization = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using AccessControlDbContext db = new(new DbContextOptionsBuilder<AccessControlDbContext>()
            .UseSqlite(connection).Options, TestActorProvider.Instance);
        await db.Database.EnsureCreatedAsync();
        using AuthorizationCache cache = new(Options.Create(new AccessControlCacheOptions()),
            NullLogger<AuthorizationCache>.Instance);
        CatalogRegistrationController controller = new(new CatalogRegistrationStore(db, cache,
            NullLogger<CatalogRegistrationStore>.Instance));
        DefaultHttpContext context = new();
        context.Items[nameof(DecisionClientCredential)] = new DecisionClientCredential
        {
            CanRegisterCatalog = true
        };
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        CatalogRegistrationRequest request = new("another-tenant", organization, "payments", "Payments",
            [new CatalogEndpoint("GET", "/customers/{id}")]);

        IActionResult anotherOrganization = await controller.RegisterAsync(request with
        { UnitOrganizationId = Guid.NewGuid(), TenantId = "another-tenant-2" }, default);
        IActionResult allowed = await controller.RegisterAsync(request, default);

        Assert.Equal(new CatalogRegistrationResult(1, 1),
            Assert.IsType<OkObjectResult>(anotherOrganization).Value);
        Assert.Equal(new CatalogRegistrationResult(1, 1), Assert.IsType<OkObjectResult>(allowed).Value);
        Assert.Equal(2, await db.EndpointPolicies.CountAsync());
        Assert.All(await db.Modules.ToListAsync(), module => Assert.Equal("payments", module.Code));
    }
}
