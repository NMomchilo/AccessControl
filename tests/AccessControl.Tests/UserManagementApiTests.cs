using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UserManagement.Contracts;
using UserManagement.Infrastructure.Database;

namespace AccessControl.Tests;

public sealed class UserManagementApiTests
{
    private const string Oid = "9155ff4d-2708-4770-b87e-e238645ec5df";
    private const string ClientKey = "local-client-key-0123456789abcdef";
    private const string AdminKey = "local-admin-key-0123456789abcdefg";

    [Fact]
    public async Task ResolveAndAdministration_EnforceCredentialScopesAndUpdateRolesAsync()
    {
        await using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await factory.InitializeAsync();

        using HttpRequestMessage denied = ResolveRequest(AdminKey);
        using HttpResponseMessage deniedResponse = await client.SendAsync(denied);
        Assert.Equal(HttpStatusCode.Unauthorized, deniedResponse.StatusCode);

        using HttpRequestMessage resolve = ResolveRequest(ClientKey);
        using HttpResponseMessage resolvedResponse = await client.SendAsync(resolve);
        UserIdentityResponse? first = await resolvedResponse.Content.ReadFromJsonAsync<UserIdentityResponse>();
        Assert.Equal(HttpStatusCode.OK, resolvedResponse.StatusCode);
        Assert.Equal(["reviewer"], first?.Roles);
        long revisionBefore = await ReadRevisionAsync(client);

        using HttpRequestMessage blockedWrite = new(HttpMethod.Put, $"/v1/users/{Oid}/roles")
        {
            Content = JsonContent.Create(new ReplaceRolesRequest(["admin"]))
        };
        blockedWrite.Headers.Add("X-User-Management-Key", ClientKey);
        using HttpResponseMessage blockedResponse = await client.SendAsync(blockedWrite);
        Assert.Equal(HttpStatusCode.Unauthorized, blockedResponse.StatusCode);

        using HttpRequestMessage adminWrite = new(HttpMethod.Put, $"/v1/users/{Oid}/roles")
        {
            Content = JsonContent.Create(new ReplaceRolesRequest(["admin"]))
        };
        adminWrite.Headers.Add("X-User-Management-Key", AdminKey);
        using HttpResponseMessage written = await client.SendAsync(adminWrite);
        Assert.Equal(HttpStatusCode.NoContent, written.StatusCode);
        Assert.Equal(revisionBefore + 1, await ReadRevisionAsync(client));

        using HttpRequestMessage second = ResolveRequest(ClientKey);
        using HttpResponseMessage secondResponse = await client.SendAsync(second);
        UserIdentityResponse? updated = await secondResponse.Content.ReadFromJsonAsync<UserIdentityResponse>();
        Assert.Equal(["admin"], updated?.Roles);
    }

    private static HttpRequestMessage ResolveRequest(string key)
    {
        HttpRequestMessage request = new(HttpMethod.Post, "/v1/users/resolve")
        {
            Content = JsonContent.Create(new UserIdentityRequest(Oid, "user@example.com", "Example User"))
        };
        request.Headers.Add("X-User-Management-Key", key);
        return request;
    }

    private static async Task<long> ReadRevisionAsync(HttpClient client)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "/v1/users/revision");
        request.Headers.Add("X-User-Management-Key", ClientKey);
        using HttpResponseMessage response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<long>();
    }

    private sealed class ApiFactory : WebApplicationFactory<UserManagement.Service.UserManagementServiceMarker>
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");

        public async Task InitializeAsync()
        {
            await _connection.OpenAsync();
            using IServiceScope scope = Services.CreateScope();
            UserManagementDbContext db = scope.ServiceProvider.GetRequiredService<UserManagementDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("UserManagementDatabase:ApplyMigrationsOnStartup", "false");
            builder.UseSetting("ConnectionStrings:UserManagement", "Server=localhost;Database=unused;Trusted_Connection=True");
            builder.UseSetting("UserManagementKeys:Clients:0:ClientId", "sample-api");
            builder.UseSetting("UserManagementKeys:Clients:0:Key", ClientKey);
            builder.UseSetting("UserManagementKeys:Administrators:0:ActorId", "operator");
            builder.UseSetting("UserManagementKeys:Administrators:0:Key", AdminKey);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<UserManagementDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<UserManagementDbContext>>();
                services.RemoveAll<UserManagementDbContext>();
                services.AddDbContext<UserManagementDbContext>(options => options.UseSqlite(_connection));
            });
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
