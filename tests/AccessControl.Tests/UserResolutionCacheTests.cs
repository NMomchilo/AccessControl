using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UserManagement.Client;
using UserManagement.Contracts;

namespace AccessControl.Tests;

public sealed class UserResolutionCacheTests
{
    private static readonly Guid OrganizationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly UserIdentityRequest Request = new("default", OrganizationId, "Alpha",
        "9155ff4d-2708-4770-b87e-e238645ec5df", null, null);
    private static readonly Guid Reviewer = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Manager = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task ResolveAsync_RepeatedCall_UsesOneRemoteLookupAsync()
    {
        CountingClient source = new();
        using UserResolutionCache cache = CreateCache(source);

        await cache.ResolveAsync(Request, CancellationToken.None);
        await cache.ResolveAsync(Request, CancellationToken.None);

        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task ResolveAsync_SameOidInDifferentTenants_UsesSeparateEntriesAsync()
    {
        CountingClient source = new();
        using UserResolutionCache cache = CreateCache(source);

        await cache.ResolveAsync(Request, CancellationToken.None);
        await cache.ResolveAsync(Request with { TenantId = "tenant-b" }, CancellationToken.None);
        await cache.ResolveAsync(Request, CancellationToken.None);

        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task ResolveAsync_SameOidInDifferentOrganizations_UsesSeparateEntriesAsync()
    {
        CountingClient source = new();
        using UserResolutionCache cache = CreateCache(source);
        await cache.ResolveAsync(Request, CancellationToken.None);
        await cache.ResolveAsync(Request with { UnitOrganizationId = Guid.NewGuid(), UnitOrganizationName = "Beta" },
            CancellationToken.None);
        await cache.ResolveAsync(Request, CancellationToken.None);
        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task ResolveAsync_ConcurrentMisses_UseOneRemoteLookupAsync()
    {
        CountingClient source = new(TimeSpan.FromMilliseconds(30));
        using UserResolutionCache cache = CreateCache(source);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => cache.ResolveAsync(Request, CancellationToken.None)));

        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task ResolveAsync_DistinctUsers_DoNotBlockEachOtherAsync()
    {
        ConcurrentStartClient source = new(65);
        using UserResolutionCache cache = CreateCache(source);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));
        UserIdentityRequest[] requests = Enumerable.Range(1, 65)
            .Select(index => Request with { ObjectId = new Guid(index, 0, 0, new byte[8]).ToString("D") })
            .ToArray();

        await Task.WhenAll(requests.Select(request => cache.ResolveAsync(request, timeout.Token)));

        Assert.Equal(65, source.Started);
    }

    [Fact]
    public async Task ResolveAsync_ExpiredEntry_RefreshesRoleAsync()
    {
        CountingClient source = new();
        using UserResolutionCache cache = CreateCache(source, TimeSpan.FromMilliseconds(20));
        await cache.ResolveAsync(Request, CancellationToken.None);

        await Task.Delay(40);
        await cache.ResolveAsync(Request, CancellationToken.None);

        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task ResolveAsync_RoleChange_BecomesVisibleAfterExpiryAsync()
    {
        MutableClient source = new();
        using UserResolutionCache cache = new(source, Options.Create(new UserManagementClientOptions
        {
            MaximumCachedUsers = 10,
            CacheLifetime = TimeSpan.FromMilliseconds(20)
        }), NullLogger<UserResolutionCache>.Instance);
        UserIdentityResponse before = await cache.ResolveAsync(Request, CancellationToken.None);
        source.Roles = ["manager"];

        UserIdentityResponse stale = await cache.ResolveAsync(Request, CancellationToken.None);
        await Task.Delay(40);
        UserIdentityResponse current = await cache.ResolveAsync(Request, CancellationToken.None);

        Assert.Equal([Reviewer], before.Roles.Select(role => role.RoleId).ToArray());
        Assert.Equal([Reviewer], stale.Roles.Select(role => role.RoleId).ToArray());
        Assert.Equal([Manager], current.Roles.Select(role => role.RoleId).ToArray());
    }

    [Fact]
    public async Task ResolveAsync_Failure_IsNotCachedAsync()
    {
        FlakyClient source = new();
        using UserResolutionCache cache = new(source, Options.Create(new UserManagementClientOptions
        {
            MaximumCachedUsers = 10,
            CacheLifetime = TimeSpan.FromSeconds(5)
        }), NullLogger<UserResolutionCache>.Instance);

        await Assert.ThrowsAsync<HttpRequestException>(() => cache.ResolveAsync(Request, CancellationToken.None));
        UserIdentityResponse recovered = await cache.ResolveAsync(Request, CancellationToken.None);

        Assert.Equal([Reviewer], recovered.Roles.Select(role => role.RoleId).ToArray());
        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task ResolveAsync_SizeLimit_DoesNotCacheMoreThanConfiguredAsync()
    {
        CountingClient source = new();
        using UserResolutionCache cache = new(source, Options.Create(new UserManagementClientOptions
        {
            MaximumCachedUsers = 1,
            CacheLifetime = TimeSpan.FromSeconds(5)
        }), NullLogger<UserResolutionCache>.Instance);
        UserIdentityRequest other = Request with { ObjectId = "11111111-1111-1111-1111-111111111111" };

        await cache.ResolveAsync(Request, CancellationToken.None);
        await cache.ResolveAsync(other, CancellationToken.None);
        await cache.ResolveAsync(other, CancellationToken.None);

        Assert.Equal(3, source.Calls);
    }

    [Fact]
    public async Task ObserveRevision_AdminChange_InvalidatesOtherClientCacheAsync()
    {
        MutableClient source = new();
        using UserResolutionCache first = new(source, Options.Create(new UserManagementClientOptions
        {
            MaximumCachedUsers = 10,
            CacheLifetime = TimeSpan.FromSeconds(5)
        }), NullLogger<UserResolutionCache>.Instance);
        using UserResolutionCache second = new(source, Options.Create(new UserManagementClientOptions
        {
            MaximumCachedUsers = 10,
            CacheLifetime = TimeSpan.FromSeconds(5)
        }), NullLogger<UserResolutionCache>.Instance);
        first.ObserveRevision(0);
        second.ObserveRevision(0);
        await second.ResolveAsync(Request, CancellationToken.None);
        source.Roles = ["manager"];

        Assert.Equal([Reviewer], (await second.ResolveAsync(Request, CancellationToken.None)).Roles.Select(role => role.RoleId).ToArray());
        first.ObserveRevision(1);
        second.ObserveRevision(1);

        Assert.Equal([Manager], (await second.ResolveAsync(Request, CancellationToken.None)).Roles.Select(role => role.RoleId).ToArray());
    }

    [Fact]
    public async Task ObserveRevision_InFlightLoad_DoesNotRestoreOldEntryAsync()
    {
        PendingClient source = new();
        using UserResolutionCache cache = new(source, Options.Create(new UserManagementClientOptions
        {
            MaximumCachedUsers = 10,
            CacheLifetime = TimeSpan.FromSeconds(5)
        }), NullLogger<UserResolutionCache>.Instance);
        cache.ObserveRevision(0);
        Task<UserIdentityResponse> first = cache.ResolveAsync(Request, CancellationToken.None);
        cache.ObserveRevision(1);
        source.Complete(Response("reviewer"));
        await first;

        await cache.ResolveAsync(Request, CancellationToken.None);

        Assert.Equal(2, source.Calls);
    }

    private static UserResolutionCache CreateCache(IUserManagementClient source, TimeSpan? lifetime = null) =>
        new(source, Options.Create(new UserManagementClientOptions
        {
            MaximumCachedUsers = 100,
            CacheLifetime = lifetime ?? TimeSpan.FromSeconds(5)
        }), NullLogger<UserResolutionCache>.Instance);

    private sealed class CountingClient(TimeSpan? delay = null) : IUserManagementClient
    {
        public int Calls;

        public async Task<UserIdentityResponse> ResolveAsync(UserIdentityRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            if (delay is not null)
            {
                await Task.Delay(delay.Value, cancellationToken);
            }

            return Response("reviewer", request.TenantId);
        }
    }

    private sealed class MutableClient : IUserManagementClient
    {
        public IReadOnlyList<string> Roles { get; set; } = ["reviewer"];

        public Task<UserIdentityResponse> ResolveAsync(UserIdentityRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Response(Roles.Single(), request.TenantId));
    }

    private sealed class FlakyClient : IUserManagementClient
    {
        public int Calls;

        public Task<UserIdentityResponse> ResolveAsync(UserIdentityRequest request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref Calls) == 1)
            {
                throw new HttpRequestException("temporary outage");
            }

            return Task.FromResult(Response("reviewer", request.TenantId));
        }
    }

    private sealed class PendingClient : IUserManagementClient
    {
        private readonly TaskCompletionSource<UserIdentityResponse> _first =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;

        public Task<UserIdentityResponse> ResolveAsync(UserIdentityRequest request, CancellationToken cancellationToken) =>
            Interlocked.Increment(ref Calls) == 1
                ? _first.Task
                : Task.FromResult(Response("manager", request.TenantId));

        public void Complete(UserIdentityResponse response) => _first.SetResult(response);
    }

    private sealed class ConcurrentStartClient(int expected) : IUserManagementClient
    {
        private readonly TaskCompletionSource _allStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Started;

        public async Task<UserIdentityResponse> ResolveAsync(UserIdentityRequest request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref Started) == expected)
            {
                _allStarted.TrySetResult();
            }

            await _allStarted.Task.WaitAsync(cancellationToken);
            return Response("reviewer", request.TenantId);
        }
    }

    private static UserIdentityResponse Response(string code, string tenantId = "default") =>
        new(Guid.NewGuid(), tenantId, OrganizationId, "Alpha", true,
            [new UserRoleInfo(code == "reviewer" ? Reviewer : Manager, code, code)]);
}
