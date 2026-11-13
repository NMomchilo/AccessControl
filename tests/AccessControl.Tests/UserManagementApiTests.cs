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
    private static readonly Guid OrganizationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

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
        string resolveJson = await resolvedResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"unitOrganizationId\"", resolveJson);
        Assert.DoesNotContain("\"userOrganizationId\"", resolveJson);
        Assert.Equal("reviewer", first?.Roles.Single().Code);
        using HttpRequestMessage createRole = new(HttpMethod.Post, "/v1/roles")
        {
            Content = JsonContent.Create(new CreateRoleRequest("default", OrganizationId, "admin", "Administrator"))
        };
        createRole.Headers.Add("X-User-Management-Key", AdminKey);
        using HttpResponseMessage createdRole = await client.SendAsync(createRole);
        RoleInfo? admin = await createdRole.Content.ReadFromJsonAsync<RoleInfo>();
        Assert.Equal(HttpStatusCode.Created, createdRole.StatusCode);
        using HttpResponseMessage listedRoles = await SendAdminAsync(client, HttpMethod.Get,
            $"/v1/roles?tenantId=default&unitOrganizationId={OrganizationId:D}");
        Assert.Equal(HttpStatusCode.OK, listedRoles.StatusCode);
        Assert.Contains((await listedRoles.Content.ReadFromJsonAsync<PagedResult<RoleInfo>>())!.Items, role =>
            role.RoleId == admin!.RoleId);
        using HttpResponseMessage singleRole = await SendAdminAsync(client, HttpMethod.Get,
            $"/v1/roles/{admin!.RoleId:D}?tenantId=default&unitOrganizationId={OrganizationId:D}");
        Assert.Equal(admin, await singleRole.Content.ReadFromJsonAsync<RoleInfo>());
        long revisionBefore = await ReadRevisionAsync(client);

        using HttpRequestMessage blockedWrite = new(HttpMethod.Put, $"/v1/users/{Oid}/roles?tenantId=default&unitOrganizationId={OrganizationId:D}")
        {
            Content = JsonContent.Create(new ReplaceRolesRequest([admin!.RoleId]))
        };
        blockedWrite.Headers.Add("X-User-Management-Key", ClientKey);
        using HttpResponseMessage blockedResponse = await client.SendAsync(blockedWrite);
        Assert.Equal(HttpStatusCode.Unauthorized, blockedResponse.StatusCode);

        using HttpRequestMessage adminWrite = new(HttpMethod.Put, $"/v1/users/{Oid}/roles?tenantId=default&unitOrganizationId={OrganizationId:D}")
        {
            Content = JsonContent.Create(new ReplaceRolesRequest([admin!.RoleId]))
        };
        adminWrite.Headers.Add("X-User-Management-Key", AdminKey);
        using HttpResponseMessage written = await client.SendAsync(adminWrite);
        Assert.Equal(HttpStatusCode.NoContent, written.StatusCode);
        Assert.Equal(revisionBefore + 1, await ReadRevisionAsync(client));

        using HttpRequestMessage second = ResolveRequest(ClientKey);
        using HttpResponseMessage secondResponse = await client.SendAsync(second);
        UserIdentityResponse? updated = await secondResponse.Content.ReadFromJsonAsync<UserIdentityResponse>();
        Assert.NotNull(updated);
        Assert.Equal([admin!.RoleId], updated.Roles.Select(role => role.RoleId).ToArray());
        using HttpResponseMessage users = await SendAdminAsync(client, HttpMethod.Get,
            "/v1/users?tenantId=default");
        Assert.Equal([admin.RoleId], (await users.Content.ReadFromJsonAsync<PagedResult<UserSummary>>())!.Items
            .Single().Memberships.Single().Roles.Select(role => role.RoleId));
        using HttpResponseMessage byEmail = await SendAdminAsync(client, HttpMethod.Get,
            "/v1/users/by-email?tenantId=default&email=USER%40EXAMPLE.COM");
        Assert.Equal(HttpStatusCode.OK, byEmail.StatusCode);
        Assert.Equal(Oid, (await byEmail.Content.ReadFromJsonAsync<PagedResult<UserSummary>>())!.Items.Single().ObjectId);
    }

    [Fact]
    public async Task OrganizationAdmin_CreateUpdateDeleteAndTombstoneAsync()
    {
        await using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await factory.InitializeAsync();
        Guid emptyOrganizationId = Guid.NewGuid();
        using HttpRequestMessage create = new(HttpMethod.Post, "/v1/unit-organizations")
        {
            Content = JsonContent.Create(new CreateUnitOrganizationRequest(emptyOrganizationId, "default", "Empty"))
        };
        create.Headers.Add("X-User-Management-Key", AdminKey);
        using HttpResponseMessage created = await client.SendAsync(create);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using HttpRequestMessage update = new(HttpMethod.Put,
            $"/v1/unit-organizations/{emptyOrganizationId:D}?tenantId=default")
        {
            Content = JsonContent.Create(new UpdateUnitOrganizationRequest("Renamed"))
        };
        update.Headers.Add("X-User-Management-Key", AdminKey);
        using HttpResponseMessage updated = await client.SendAsync(update);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("Renamed", (await updated.Content.ReadFromJsonAsync<UnitOrganizationInfo>())?.Name);

        long revisionBeforeDelete = await ReadRevisionAsync(client);

        using HttpRequestMessage delete = new(HttpMethod.Delete,
            $"/v1/unit-organizations/{emptyOrganizationId:D}?tenantId=default");
        delete.Headers.Add("X-User-Management-Key", AdminKey);
        using HttpResponseMessage deleted = await client.SendAsync(delete);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(revisionBeforeDelete + 1, await ReadRevisionAsync(client));

        using HttpRequestMessage repeatedDelete = new(HttpMethod.Delete,
            $"/v1/unit-organizations/{emptyOrganizationId:D}?tenantId=default");
        repeatedDelete.Headers.Add("X-User-Management-Key", AdminKey);
        using HttpResponseMessage repeatedDeleteResponse = await client.SendAsync(repeatedDelete);
        Assert.Equal(HttpStatusCode.NoContent, repeatedDeleteResponse.StatusCode);

        using HttpRequestMessage getDeleted = new(HttpMethod.Get,
            $"/v1/unit-organizations/{emptyOrganizationId:D}?tenantId=default");
        getDeleted.Headers.Add("X-User-Management-Key", AdminKey);
        using HttpResponseMessage getDeletedResponse = await client.SendAsync(getDeleted);
        Assert.Equal(HttpStatusCode.NotFound, getDeletedResponse.StatusCode);

        using HttpRequestMessage recreate = new(HttpMethod.Post, "/v1/unit-organizations")
        {
            Content = JsonContent.Create(new CreateUnitOrganizationRequest(emptyOrganizationId, "default", "Reused"))
        };
        recreate.Headers.Add("X-User-Management-Key", AdminKey);
        using HttpResponseMessage recreateResponse = await client.SendAsync(recreate);
        Assert.Equal(HttpStatusCode.Conflict, recreateResponse.StatusCode);

        using HttpRequestMessage deletedResolve = new(HttpMethod.Post, "/v1/users/resolve")
        {
            Content = JsonContent.Create(new UserIdentityRequest("default", emptyOrganizationId,
                "Reused", Oid, null, null))
        };
        deletedResolve.Headers.Add("X-User-Management-Key", ClientKey);
        using HttpResponseMessage deletedResolveResponse = await client.SendAsync(deletedResolve);
        Assert.Equal(HttpStatusCode.Conflict, deletedResolveResponse.StatusCode);

        using HttpRequestMessage resolve = ResolveRequest(ClientKey);
        using HttpResponseMessage resolved = await client.SendAsync(resolve);
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        using HttpRequestMessage referencedDelete = new(HttpMethod.Delete,
            $"/v1/unit-organizations/{OrganizationId:D}?tenantId=default");
        referencedDelete.Headers.Add("X-User-Management-Key", AdminKey);
        using HttpResponseMessage deletedReferenced = await client.SendAsync(referencedDelete);
        Assert.Equal(HttpStatusCode.NoContent, deletedReferenced.StatusCode);
        using HttpRequestMessage cannotResolve = ResolveRequest(ClientKey);
        using HttpResponseMessage cannotResolveResponse = await client.SendAsync(cannotResolve);
        Assert.Equal(HttpStatusCode.Conflict, cannotResolveResponse.StatusCode);
    }

    [Fact]
    public async Task OrganizationAdmin_ReadAndConflictResponsesPreserveTenantScopeAsync()
    {
        await using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await factory.InitializeAsync();
        Guid firstId = Guid.NewGuid();
        Guid secondId = Guid.NewGuid();

        using HttpResponseMessage first = await SendAdminAsync(client, HttpMethod.Post,
            "/v1/unit-organizations", new CreateUnitOrganizationRequest(firstId, "default", "Alpha"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using HttpResponseMessage second = await SendAdminAsync(client, HttpMethod.Post,
            "/v1/unit-organizations", new CreateUnitOrganizationRequest(secondId, "default", "Beta"));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        using HttpResponseMessage list = await SendAdminAsync(client, HttpMethod.Get,
            "/v1/unit-organizations?tenantId=default");
        Assert.Equal(2, (await list.Content.ReadFromJsonAsync<UnitOrganizationInfo[]>())?.Length);
        using HttpResponseMessage read = await SendAdminAsync(client, HttpMethod.Get,
            $"/v1/unit-organizations/{firstId:D}?tenantId=default");
        Assert.Equal(firstId, (await read.Content.ReadFromJsonAsync<UnitOrganizationInfo>())?.Id);
        using HttpResponseMessage wrongTenant = await SendAdminAsync(client, HttpMethod.Get,
            $"/v1/unit-organizations/{firstId:D}?tenantId=other");
        Assert.Equal(HttpStatusCode.NotFound, wrongTenant.StatusCode);

        using HttpResponseMessage duplicateName = await SendAdminAsync(client, HttpMethod.Put,
            $"/v1/unit-organizations/{secondId:D}?tenantId=default",
            new UpdateUnitOrganizationRequest("Alpha"));
        Assert.Equal(HttpStatusCode.Conflict, duplicateName.StatusCode);
        using HttpResponseMessage missingUpdate = await SendAdminAsync(client, HttpMethod.Put,
            $"/v1/unit-organizations/{Guid.NewGuid():D}?tenantId=default",
            new UpdateUnitOrganizationRequest("Missing"));
        Assert.Equal(HttpStatusCode.NotFound, missingUpdate.StatusCode);
        using HttpResponseMessage missingDelete = await SendAdminAsync(client, HttpMethod.Delete,
            $"/v1/unit-organizations/{Guid.NewGuid():D}?tenantId=default");
        Assert.Equal(HttpStatusCode.NotFound, missingDelete.StatusCode);
    }

    [Fact]
    public async Task UserAdministration_UnknownOrInvalidTargetsDoNotWriteAsync()
    {
        await using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await factory.InitializeAsync();
        using HttpResponseMessage missingUser = await SendAdminAsync(client, HttpMethod.Get,
            $"/v1/users/{Oid}?tenantId=default&unitOrganizationId={OrganizationId:D}");
        Assert.Equal(HttpStatusCode.NotFound, missingUser.StatusCode);
        using HttpResponseMessage missingActive = await SendAdminAsync(client, HttpMethod.Put,
            $"/v1/users/{Oid}/active?tenantId=default", new SetActiveRequest(false));
        Assert.Equal(HttpStatusCode.NotFound, missingActive.StatusCode);
        using HttpResponseMessage invalidRole = await SendAdminAsync(client, HttpMethod.Put,
            $"/v1/users/{Oid}/roles?tenantId=default&unitOrganizationId={OrganizationId:D}",
            new ReplaceRolesRequest([Guid.Empty]));
        Assert.Equal(HttpStatusCode.BadRequest, invalidRole.StatusCode);
        using HttpResponseMessage invalidResolve = await SendClientAsync(client, HttpMethod.Post,
            "/v1/users/resolve", new UserIdentityRequest("default", Guid.Empty, "Alpha", Oid, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, invalidResolve.StatusCode);
    }

    [Fact]
    public async Task HealthAndReadinessArePublicButOtherMethodsRequireCredentialsAsync()
    {
        await using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await factory.InitializeAsync();

        using HttpResponseMessage live = await client.GetAsync("/health");
        using HttpResponseMessage ready = await client.GetAsync("/ready");
        using HttpResponseMessage unexpectedMethod = await client.PostAsync("/health", null);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unexpectedMethod.StatusCode);
    }

    [Fact]
    public async Task PaginatedAdministratorLists_AreStableScopedAndExcludeDeletedOrganizationsAsync()
    {
        await using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await factory.InitializeAsync();
        Guid secondOrganizationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        Guid otherTenantOrganizationId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        string[] oids = Enumerable.Range(1, 3)
            .Select(value => new Guid(value, 0, 0, new byte[8]).ToString("D")).ToArray();
        foreach (string oid in oids)
        {
            using HttpResponseMessage resolved = await SendClientAsync(client, HttpMethod.Post, "/v1/users/resolve",
                new UserIdentityRequest("default", OrganizationId, "Alpha", oid, $"{oid}@example.com", oid));
            Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        }
        using (HttpResponseMessage secondMembership = await SendClientAsync(client, HttpMethod.Post,
            "/v1/users/resolve", new UserIdentityRequest("default", secondOrganizationId,
                "Beta", oids[1], null, null)))
            Assert.Equal(HttpStatusCode.OK, secondMembership.StatusCode);
        using (HttpResponseMessage otherTenantUser = await SendClientAsync(client, HttpMethod.Post,
            "/v1/users/resolve", new UserIdentityRequest("other", otherTenantOrganizationId,
                "Other", Guid.NewGuid().ToString("D"), null, null)))
            Assert.Equal(HttpStatusCode.OK, otherTenantUser.StatusCode);
        using (HttpResponseMessage disabled = await SendAdminAsync(client, HttpMethod.Put,
            $"/v1/users/{oids[2]}/active?tenantId=default", new SetActiveRequest(false)))
            Assert.Equal(HttpStatusCode.NoContent, disabled.StatusCode);
        using (HttpResponseMessage extraRole = await SendAdminAsync(client, HttpMethod.Post, "/v1/roles",
            new CreateRoleRequest("default", OrganizationId, "manager", "Manager")))
            Assert.Equal(HttpStatusCode.Created, extraRole.StatusCode);

        using HttpResponseMessage usersPage1 = await SendAdminAsync(client, HttpMethod.Get,
            "/v1/users?tenantId=default&page=1&pageSize=2");
        using HttpResponseMessage usersPage2 = await SendAdminAsync(client, HttpMethod.Get,
            "/v1/users?tenantId=default&page=2&pageSize=2");
        using HttpResponseMessage usersPage3 = await SendAdminAsync(client, HttpMethod.Get,
            "/v1/users?tenantId=default&page=3&pageSize=2");
        PagedResult<UserSummary> first = (await usersPage1.Content.ReadFromJsonAsync<PagedResult<UserSummary>>())!;
        PagedResult<UserSummary> second = (await usersPage2.Content.ReadFromJsonAsync<PagedResult<UserSummary>>())!;
        PagedResult<UserSummary> third = (await usersPage3.Content.ReadFromJsonAsync<PagedResult<UserSummary>>())!;
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(1, first.Page);
        Assert.Equal(2, first.PageSize);
        Assert.Equal(oids, first.Items.Concat(second.Items).Select(user => user.ObjectId).ToArray());
        Assert.Empty(third.Items);
        Assert.Contains(second.Items, user => !user.IsActive);

        using HttpResponseMessage organizationUsers = await SendAdminAsync(client, HttpMethod.Get,
            $"/v1/unit-organizations/{secondOrganizationId:D}/users?tenantId=default&page=1&pageSize=2");
        PagedResult<UserSummary> members =
            (await organizationUsers.Content.ReadFromJsonAsync<PagedResult<UserSummary>>())!;
        Assert.Equal(1, members.TotalCount);
        Assert.Equal(oids[1], members.Items.Single().ObjectId);

        using HttpResponseMessage rolesPage1 = await SendAdminAsync(client, HttpMethod.Get,
            "/v1/roles?tenantId=default&page=1&pageSize=2");
        using HttpResponseMessage rolesPage2 = await SendAdminAsync(client, HttpMethod.Get,
            "/v1/roles?tenantId=default&page=2&pageSize=2");
        PagedResult<RoleInfo> firstRoles = (await rolesPage1.Content.ReadFromJsonAsync<PagedResult<RoleInfo>>())!;
        PagedResult<RoleInfo> secondRoles = (await rolesPage2.Content.ReadFromJsonAsync<PagedResult<RoleInfo>>())!;
        Assert.Equal(3, firstRoles.TotalCount);
        Assert.Equal(3, firstRoles.Items.Count + secondRoles.Items.Count);
        using HttpResponseMessage filteredRoles = await SendAdminAsync(client, HttpMethod.Get,
            $"/v1/roles?tenantId=default&unitOrganizationId={OrganizationId:D}&page=1&pageSize=2");
        using HttpResponseMessage nestedRoles = await SendAdminAsync(client, HttpMethod.Get,
            $"/v1/unit-organizations/{OrganizationId:D}/roles?tenantId=default&page=1&pageSize=2");
        Assert.Equal(2, (await filteredRoles.Content.ReadFromJsonAsync<PagedResult<RoleInfo>>())?.TotalCount);
        Assert.Equal(2, (await nestedRoles.Content.ReadFromJsonAsync<PagedResult<RoleInfo>>())?.TotalCount);

        using (HttpResponseMessage delete = await SendAdminAsync(client, HttpMethod.Delete,
            $"/v1/unit-organizations/{secondOrganizationId:D}?tenantId=default"))
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        using HttpResponseMessage tombstonedUsers = await SendAdminAsync(client, HttpMethod.Get,
            $"/v1/unit-organizations/{secondOrganizationId:D}/users?tenantId=default");
        using HttpResponseMessage tombstonedRoles = await SendAdminAsync(client, HttpMethod.Get,
            $"/v1/roles?tenantId=default&unitOrganizationId={secondOrganizationId:D}");
        using HttpResponseMessage tombstonedNestedRoles = await SendAdminAsync(client, HttpMethod.Get,
            $"/v1/unit-organizations/{secondOrganizationId:D}/roles?tenantId=default");
        using HttpResponseMessage unknownOrganizationUsers = await SendAdminAsync(client, HttpMethod.Get,
            $"/v1/unit-organizations/{Guid.NewGuid():D}/users?tenantId=default");
        using HttpResponseMessage activeRoles = await SendAdminAsync(client, HttpMethod.Get,
            "/v1/roles?tenantId=default");
        using HttpResponseMessage allUsersAfterDelete = await SendAdminAsync(client, HttpMethod.Get,
            "/v1/users?tenantId=default");
        Assert.Equal(HttpStatusCode.NotFound, tombstonedUsers.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, tombstonedRoles.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, tombstonedNestedRoles.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownOrganizationUsers.StatusCode);
        Assert.Equal(2, (await activeRoles.Content.ReadFromJsonAsync<PagedResult<RoleInfo>>())?.TotalCount);
        Assert.Equal(3, (await allUsersAfterDelete.Content.ReadFromJsonAsync<PagedResult<UserSummary>>())?.TotalCount);
    }

    [Theory]
    [InlineData("page=0&pageSize=2")]
    [InlineData("page=1&pageSize=101")]
    [InlineData("page=2147483647&pageSize=100")]
    public async Task PaginatedAdministratorLists_RejectInvalidBoundsAsync(string query)
    {
        await using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await factory.InitializeAsync();
        using HttpResponseMessage users = await SendAdminAsync(client, HttpMethod.Get,
            $"/v1/users?tenantId=default&{query}");
        using HttpResponseMessage roles = await SendAdminAsync(client, HttpMethod.Get,
            $"/v1/roles?tenantId=default&{query}");
        Assert.Equal(HttpStatusCode.BadRequest, users.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, roles.StatusCode);
    }

    [Fact]
    public async Task PaginatedAdministratorLists_RejectClientCredentialsAsync()
    {
        await using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await factory.InitializeAsync();
        using HttpResponseMessage users = await SendClientAsync(client, HttpMethod.Get,
            "/v1/users?tenantId=default");
        using HttpResponseMessage roles = await SendClientAsync(client, HttpMethod.Get,
            "/v1/roles?tenantId=default");
        Assert.Equal(HttpStatusCode.Unauthorized, users.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, roles.StatusCode);
    }

    [Fact]
    public async Task RoleStatusEndpoint_RequiresAdministratorAndRemovesDisabledRoleFromResolutionAsync()
    {
        await using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await factory.InitializeAsync();
        using HttpRequestMessage firstRequest = ResolveRequest(ClientKey);
        using HttpResponseMessage firstResponse = await client.SendAsync(firstRequest);
        UserIdentityResponse first = (await firstResponse.Content.ReadFromJsonAsync<UserIdentityResponse>())!;
        Guid roleId = first.Roles.Single().RoleId;
        string path = $"/v1/roles/{roleId:D}/active?tenantId=default&unitOrganizationId={OrganizationId:D}";
        using HttpResponseMessage rejected = await SendClientAsync(client, HttpMethod.Put, path, new SetActiveRequest(false));
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        long revision = await ReadRevisionAsync(client);
        using HttpResponseMessage disabled = await SendAdminAsync(client, HttpMethod.Put, path, new SetActiveRequest(false));
        Assert.Equal(HttpStatusCode.NoContent, disabled.StatusCode);
        using HttpResponseMessage missingFlag = await SendAdminAsync(client, HttpMethod.Put, path, new { });
        Assert.Equal(HttpStatusCode.BadRequest, missingFlag.StatusCode);
        Assert.Equal(revision + 1, await ReadRevisionAsync(client));
        using HttpRequestMessage nextRequest = ResolveRequest(ClientKey);
        using HttpResponseMessage nextResponse = await client.SendAsync(nextRequest);
        Assert.Empty((await nextResponse.Content.ReadFromJsonAsync<UserIdentityResponse>())!.Roles);
        using HttpResponseMessage listed = await SendAdminAsync(client, HttpMethod.Get,
            $"/v1/roles?tenantId=default&unitOrganizationId={OrganizationId:D}");
        Assert.False((await listed.Content.ReadFromJsonAsync<PagedResult<RoleInfo>>())!.Items.Single().IsActive);
    }

    private static async Task<HttpResponseMessage> SendAdminAsync(HttpClient client, HttpMethod method,
        string path, object? body = null)
    {
        using HttpRequestMessage request = new(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Add("X-User-Management-Key", AdminKey);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendClientAsync(HttpClient client, HttpMethod method,
        string path, object? body = null)
    {
        using HttpRequestMessage request = new(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Add("X-User-Management-Key", ClientKey);
        return await client.SendAsync(request);
    }

    private static HttpRequestMessage ResolveRequest(string key)
    {
        HttpRequestMessage request = new(HttpMethod.Post, "/v1/users/resolve")
        {
            Content = JsonContent.Create(new UserIdentityRequest("default", OrganizationId, "Alpha", Oid,
                "user@example.com", "Example User"))
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
