using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UserManagement.Client;
using UserManagement.Contracts;

namespace AccessControl.Tests;

public sealed class HttpUserManagementClientTests
{
    private static readonly Guid OrganizationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    [Fact]
    public async Task ResolveAsync_SendsClientCredentialAndOidToResolutionEndpointAsync()
    {
        RecordingHandler handler = new();
        HttpUserManagementClient sut = new(new SingleClientFactory(handler),
            Options.Create(new UserManagementClientOptions
            {
                ServiceUrl = "https://user-management.example/",
                ApiKey = "test-client-key"
            }), NullLogger<HttpUserManagementClient>.Instance);
        UserIdentityRequest request = new("default", OrganizationId, "Alpha", "9155ff4d-2708-4770-b87e-e238645ec5df", "user@example.com", "User");

        UserIdentityResponse response = await sut.ResolveAsync(request, CancellationToken.None);

        Assert.Equal("reviewer", response.Roles.Single().Code);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/v1/users/resolve", handler.Path);
        Assert.Equal(request.ObjectId, handler.ObjectId);
        Assert.Equal("test-client-key", handler.Key);
    }

    [Fact]
    public async Task GetRevisionAsync_SendsClientCredentialAsync()
    {
        RecordingHandler handler = new();
        HttpUserManagementClient sut = new(new SingleClientFactory(handler),
            Options.Create(new UserManagementClientOptions
            {
                ServiceUrl = "https://user-management.example/",
                ApiKey = "test-client-key"
            }), NullLogger<HttpUserManagementClient>.Instance);

        long revision = await sut.GetRevisionAsync(CancellationToken.None);

        Assert.Equal(7, revision);
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("/v1/users/revision", handler.Path);
        Assert.Equal("test-client-key", handler.Key);
    }

    [Fact]
    public async Task ResolveAsync_OrganizationConflict_ThrowsAccessExceptionAsync()
    {
        RecordingHandler handler = new() { ForceConflict = true };
        HttpUserManagementClient sut = new(new SingleClientFactory(handler),
            Options.Create(new UserManagementClientOptions
            {
                ServiceUrl = "https://user-management.example/", ApiKey = "test-client-key"
            }), NullLogger<HttpUserManagementClient>.Instance);
        UserIdentityRequest request = new("default", OrganizationId, "Alpha",
            "9155ff4d-2708-4770-b87e-e238645ec5df", null, null);
        await Assert.ThrowsAsync<UnitOrganizationAccessException>(() =>
            sut.ResolveAsync(request, CancellationToken.None));
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }
        public string? Key { get; private set; }
        public string? ObjectId { get; private set; }
        public bool ForceConflict { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            Path = request.RequestUri?.AbsolutePath;
            Key = request.Headers.GetValues("X-User-Management-Key").Single();
            if (ForceConflict) return new HttpResponseMessage(HttpStatusCode.Conflict);
            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(7L) };
            }

            UserIdentityRequest? body = await request.Content!.ReadFromJsonAsync<UserIdentityRequest>(cancellationToken);
            ObjectId = body?.ObjectId;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new UserIdentityResponse(Guid.NewGuid(), body!.TenantId,
                    body.UnitOrganizationId, body.UnitOrganizationName, true,
                    [new UserRoleInfo(Guid.Parse("22222222-2222-2222-2222-222222222222"), "reviewer", "Reviewer")]))
            };
        }
    }
}
