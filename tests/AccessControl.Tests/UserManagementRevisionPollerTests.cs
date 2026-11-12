using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UserManagement.Client;
using UserManagement.Contracts;

namespace AccessControl.Tests;

public sealed class UserManagementRevisionPollerTests
{
    private static readonly Guid OrganizationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly UserIdentityRequest Request = new("default", OrganizationId, "Alpha",
        "9155ff4d-2708-4770-b87e-e238645ec5df", null, null);

    [Fact]
    public async Task Poller_RetriesFailedReadAndInvalidatesCachedRolesWhenRevisionChangesAsync()
    {
        MutableResolutionClient source = new();
        UserManagementClientOptions options = new()
        {
            ServiceUrl = "https://user-management.example/",
            ApiKey = "test-client-key",
            RevisionPollInterval = TimeSpan.FromMilliseconds(10),
            CacheLifetime = TimeSpan.FromSeconds(5)
        };
        using UserResolutionCache cache = new(source, Options.Create(options),
            NullLogger<UserResolutionCache>.Instance);
        UserIdentityResponse initial = await cache.ResolveAsync(Request, CancellationToken.None);
        source.CurrentRoleId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        RevisionHandler handler = new();
        HttpUserManagementClient client = new(new SingleClientFactory(handler), Options.Create(options),
            NullLogger<HttpUserManagementClient>.Instance);
        using UserManagementRevisionPoller poller = new(client, cache, Options.Create(options),
            NullLogger<UserManagementRevisionPoller>.Instance);

        await poller.StartAsync(CancellationToken.None);
        try
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));
            while (!timeout.IsCancellationRequested)
            {
                UserIdentityResponse current = await cache.ResolveAsync(Request, timeout.Token);
                if (current.Roles.Single().RoleId == source.CurrentRoleId) break;
                await Task.Delay(10, timeout.Token);
            }

            Assert.NotEqual(initial.Roles.Single().RoleId,
                (await cache.ResolveAsync(Request, CancellationToken.None)).Roles.Single().RoleId);
            Assert.True(handler.Calls >= 3); // failure, baseline, changed revision
            Assert.Equal(2, source.Calls);
        }
        finally
        {
            await poller.StopAsync(CancellationToken.None);
        }
    }

    private sealed class MutableResolutionClient : IUserManagementClient
    {
        public int Calls;
        public Guid CurrentRoleId { get; set; } = Guid.Parse("22222222-2222-2222-2222-222222222222");

        public Task<UserIdentityResponse> ResolveAsync(UserIdentityRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new UserIdentityResponse(Guid.NewGuid(), request.TenantId,
                request.UnitOrganizationId, request.UnitOrganizationName, true,
                [new UserRoleInfo(CurrentRoleId, "reviewer", "Reviewer")]));
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RevisionHandler : HttpMessageHandler
    {
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            int call = Interlocked.Increment(ref Calls);
            if (call == 1) throw new HttpRequestException("temporary revision endpoint outage");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(call == 2 ? 0L : 1L)
            });
        }
    }
}
