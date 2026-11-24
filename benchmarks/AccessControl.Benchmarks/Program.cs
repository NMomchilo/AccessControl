using Common.Persistence;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using AccessControl.Application;
using AccessControl.Contracts;
using AccessControl.Domain;
using AccessControl.Infrastructure.Caching;
using AccessControl.Infrastructure.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Shared.Infrastructure;
using System.Collections.Concurrent;
using StackExchange.Redis;

namespace AccessControl.Benchmarks;

internal static class Program
{
    private const string DecisionKey = "local-benchmark-decision-key-0123456789";
    private const string BenchmarkBearer = "benchmark-token";
    private const string BenchmarkOid = "9155ff4d-2708-4770-b87e-e238645ec5df";
    private static readonly Guid Reader = Guid.Parse("10954aa4-7532-80de-a80e-a7be3ad4c991");
    private static readonly Guid Organization = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid BenchmarkRead = Guid.Parse("99999999-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DecisionRequest Request = new(BenchmarkOid,
        "GET", "/benchmark/{id}", "benchmark-tenant", Organization, [Reader]);

    public static async Task<int> Main(string[] args)
    {
        try
        {
            Settings settings = Settings.Parse(args);
            BenchmarkCase[] cases = settings.WorkloadFile is null
                ? [new BenchmarkCase("default", Request, true, 1)]
                : LoadWorkload(settings.WorkloadFile);
            List<BenchmarkResult> results = [];
            if (settings.RemoteUrl is not null)
            {
                string key = Environment.GetEnvironmentVariable("AccessControlBenchmark__DecisionKey")
                    ?? throw new ArgumentException("Set AccessControlBenchmark__DecisionKey for remote mode.");
                using HttpClient client = new() { BaseAddress = new Uri(settings.RemoteUrl), Timeout = TimeSpan.FromSeconds(10) };
                string? bearer = Environment.GetEnvironmentVariable("AccessControlBenchmark__BearerToken");
                if (cases.Any(item => item.Request.SubjectId is not null) && string.IsNullOrWhiteSpace(bearer))
                    throw new ArgumentException("Set AccessControlBenchmark__BearerToken for protected remote decisions.");
                await MeasureAllAsync("remote SQL Server service", client, key, bearer, settings, cases, results);
            }
            else
            {
                await using (BenchmarkFactory fake = new(BenchmarkStore.Fake))
                {
                    using HttpClient client = fake.CreateClient();
                    await MeasureAllAsync("in-memory store", client, DecisionKey, BenchmarkBearer, settings, cases, results);
                }

                string dataSource = $"access-control-benchmark-{Guid.NewGuid():N}";
                string connectionString = $"Data Source={dataSource};Mode=Memory;Cache=Shared";
                await using SqliteConnection keeper = new(connectionString);
                await keeper.OpenAsync();
                await using (BenchmarkFactory sqlite = new(BenchmarkStore.Sqlite, connectionString))
                {
                    using HttpClient client = sqlite.CreateClient();
                    await SeedAsync(sqlite.Services);
                    await MeasureAllAsync(Environment.GetEnvironmentVariable("AccessControlBenchmark__RedisConnectionString") is null
                        ? "SQLite store with test cache" : "SQLite store with Redis", client, DecisionKey, BenchmarkBearer, settings, cases, results);
                }
            }

            BenchmarkReport report = new(DateTimeOffset.UtcNow, RuntimeInformation.FrameworkDescription,
                RuntimeInformation.OSDescription, Environment.ProcessorCount, settings.RequestCount, cases, results);
            string outputPath = Path.GetFullPath(settings.OutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(report,
                new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Benchmark results saved to {outputPath}");
            Console.WriteLine("Scenario | Concurrency | Requests/s | p50 ms | p95 ms | p99 ms | Max ms");
            foreach (BenchmarkResult result in results)
            {
                Console.WriteLine($"{result.Scenario} | {result.Concurrency} | {result.RequestsPerSecond:F1} | " +
                    $"{result.P50Ms:F2} | {result.P95Ms:F2} | {result.P99Ms:F2} | {result.MaxMs:F2}");
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Benchmark failed: {exception}");
            return 1;
        }
    }

    private static BenchmarkCase[] LoadWorkload(string path)
    {
        BenchmarkCase[] cases = JsonSerializer.Deserialize<BenchmarkCase[]>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        if (cases.Length == 0 || cases.Any(item => string.IsNullOrWhiteSpace(item.Name) || item.Weight < 1 ||
            item.Request is null ||
            string.IsNullOrWhiteSpace(item.Request.HttpMethod) ||
            string.IsNullOrWhiteSpace(item.Request.RouteTemplate) ||
            string.IsNullOrWhiteSpace(item.Request.TenantId) ||
            (item.Request.SubjectId is not null && item.Request.UnitOrganizationId == Guid.Empty) ||
            item.Request.RoleIds is { Count: > 16 } ||
            item.Request.RoleIds?.Any(id => id == Guid.Empty) == true) ||
            cases.Sum(item => item.Weight) > 10000)
        {
            throw new ArgumentException("Workload must contain valid named cases with positive weights.");
        }

        return cases;
    }

    private static async Task SeedAsync(IServiceProvider services)
    {
        using IServiceScope scope = services.CreateScope();
        AccessControlDbContext db = scope.ServiceProvider.GetRequiredService<AccessControlDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Entitlements.Add(new EntitlementEntity
        {
            Id = BenchmarkRead, TenantId = "benchmark-tenant", UnitOrganizationId = Organization,
            DisplayName = "Read benchmark", IsActive = true
        });
        await db.SaveChangesAsync();
        IAuthorizationStore store = scope.ServiceProvider.GetRequiredService<IAuthorizationStore>();
        await store.UpsertPolicyAsync(new EndpointPolicy("benchmark-tenant", "GET",
            "/benchmark/{id}", false, [BenchmarkRead], Organization), CancellationToken.None);
        await store.ReplaceRoleEntitlementsAsync("benchmark-tenant", Organization, Reader, [BenchmarkRead], CancellationToken.None);
    }

    private static async Task MeasureAllAsync(string scenario, HttpClient client, string key, string? bearer,
        Settings settings, IReadOnlyList<BenchmarkCase> cases, List<BenchmarkResult> results)
    {
        foreach (int concurrency in settings.Concurrency)
        {
            await SendBatchAsync(client, key, bearer, Math.Min(200, settings.RequestCount), concurrency, cases, null);
            long[] ticks = new long[settings.RequestCount];
            Stopwatch wall = Stopwatch.StartNew();
            await SendBatchAsync(client, key, bearer, settings.RequestCount, concurrency, cases, ticks);
            wall.Stop();
            Array.Sort(ticks);
            double ToMs(long value) => value * 1000.0 / Stopwatch.Frequency;
            results.Add(new BenchmarkResult(scenario, concurrency, settings.RequestCount, wall.Elapsed.TotalMilliseconds,
                settings.RequestCount / wall.Elapsed.TotalSeconds, ToMs(Percentile(ticks, 0.50)),
                ToMs(Percentile(ticks, 0.95)), ToMs(Percentile(ticks, 0.99)), ToMs(ticks[^1])));
        }
    }

    private static async Task SendBatchAsync(HttpClient client, string key, string? bearer, int count, int concurrency,
        IReadOnlyList<BenchmarkCase> cases, long[]? ticks)
    {
        int next = -1;
        int totalWeight = cases.Sum(item => item.Weight);
        Task[] workers = Enumerable.Range(0, concurrency).Select(async _ =>
        {
            while (true)
            {
                int index = Interlocked.Increment(ref next);
                if (index >= count)
                {
                    return;
                }

                int selection = index % totalWeight;
                BenchmarkCase selected = cases[0];
                foreach (BenchmarkCase item in cases)
                {
                    selection -= item.Weight;
                    if (selection < 0)
                    {
                        selected = item;
                        break;
                    }
                }

                using HttpRequestMessage message = new(HttpMethod.Post, "v1/decisions")
                {
                    Content = JsonContent.Create(selected.Request)
                };
                message.Headers.Add("X-Access-Control-Key", key);
                if (selected.Request.SubjectId is not null && bearer is not null)
                    message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
                long started = Stopwatch.GetTimestamp();
                using HttpResponseMessage response = await client.SendAsync(message);
                DecisionResponse? decision = await response.Content.ReadFromJsonAsync<DecisionResponse>();
                long elapsed = Stopwatch.GetTimestamp() - started;
                if (!response.IsSuccessStatusCode || decision?.Permit != selected.ExpectedPermit)
                {
                    throw new InvalidOperationException($"Decision failed for {selected.Name} at request {index}: " +
                        $"HTTP {(int)response.StatusCode}, " +
                        $"reason {decision?.Reason ?? "missing response"}");
                }

                if (ticks is not null)
                {
                    ticks[index] = elapsed;
                }
            }
        }).ToArray();
        await Task.WhenAll(workers);
    }

    private static long Percentile(long[] sorted, double percentile) =>
        sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * percentile) - 1, 0, sorted.Length - 1)];

    private sealed record Settings(int RequestCount, int[] Concurrency, string? RemoteUrl,
        string? WorkloadFile, string OutputPath)
    {
        public static Settings Parse(string[] args)
        {
            int requests = 2000;
            int[] concurrency = [1, 16];
            string? remoteUrl = null;
            string? workloadFile = null;
            string output = "artifacts/benchmarks/latest.json";
            for (int i = 0; i < args.Length; i++)
            {
                if (++i >= args.Length)
                {
                    throw new ArgumentException("Every option requires a value.");
                }

                string value = args[i];
                switch (args[i - 1])
                {
                    case "--requests": requests = int.Parse(value); break;
                    case "--concurrency": concurrency = value.Split(',').Select(int.Parse).ToArray(); break;
                    case "--remote-url": remoteUrl = value; break;
                    case "--workload-file": workloadFile = value; break;
                    case "--output": output = value; break;
                    default: throw new ArgumentException($"Unknown option: {args[i - 1]}");
                }
            }

            if (requests < 1 || concurrency.Length == 0 || concurrency.Any(value => value < 1))
            {
                throw new ArgumentException("Requests and concurrency must be positive.");
            }
            if (remoteUrl is not null && (!Uri.TryCreate(remoteUrl, UriKind.Absolute, out Uri? uri) ||
                uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException("Remote URL must be an absolute HTTPS URL.");
            }
            return new Settings(requests, concurrency, remoteUrl, workloadFile, output);
        }
    }

    private sealed class BenchmarkFactory(BenchmarkStore store, string? connectionString = null)
        : WebApplicationFactory<global::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("AccessControlDatabase:ApplyMigrationsOnStartup", "false");
            builder.UseContentRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "../../../../../src/AccessControl.Service")));
            builder.UseSetting("ConnectionStrings:AccessControl", "Server=localhost;Database=unused;Trusted_Connection=True");
            builder.UseSetting("ApiKeys:DecisionClients:0:ClientId", "benchmark-local");
            builder.UseSetting("ApiKeys:DecisionClients:0:Key", DecisionKey);
            builder.UseSetting("ApiKeys:DecisionClients:0:Tenants:0", "benchmark-tenant");
            builder.UseSetting("ApiKeys:DecisionClients:0:AllowAnyUnitOrganization", "true");
            builder.UseSetting("ApiKeys:AdminKey", "local-benchmark-admin-key-0123456789");
            builder.UseSetting("EntraAdmin:Authority", "https://login.microsoftonline.com/test/v2.0");
            builder.UseSetting("EntraAdmin:Audience", "api://benchmark-admin");
            builder.UseSetting("DecisionEntra:Authority", "https://login.microsoftonline.com/test/v2.0");
            builder.UseSetting("DecisionEntra:Audience", "api://benchmark-decision");
            builder.UseSetting("DecisionEntra:AuthenticationScheme", "BenchmarkEntra");
            builder.UseSetting("UserManagementAdminLookup:ServiceUrl", "http://localhost:5220");
            builder.UseSetting("UserManagementAdminLookup:LookupKey", new string('a', 40));
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRedisCacheService>();
                string? redisConnection = Environment.GetEnvironmentVariable("AccessControlBenchmark__RedisConnectionString");
                if (string.IsNullOrWhiteSpace(redisConnection))
                    services.AddSingleton<IRedisCacheService, BenchmarkRedisCache>();
                else
                {
                    services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnection));
                    services.AddSingleton<IRedisCacheService>(provider =>
                        new RedisCacheService(provider.GetRequiredService<IConnectionMultiplexer>(), 0, "benchmark-ac"));
                }
                services.AddSingleton<ICurrentActorProvider>(new BenchmarkActorProvider());
                services.AddAuthentication(options => options.DefaultAuthenticateScheme = "BenchmarkEntra")
                    .AddScheme<AuthenticationSchemeOptions, BenchmarkAuthenticationHandler>("BenchmarkEntra", _ => { });
                if (store == BenchmarkStore.Fake)
                {
                    services.RemoveAll<IAuthorizationStore>();
                    services.AddScoped<IAuthorizationStore, FakeAuthorizationStore>();
                    foreach (ServiceDescriptor descriptor in services.Where(descriptor =>
                        descriptor.ServiceType == typeof(IHostedService) &&
                        descriptor.ImplementationType == typeof(AuthorizationCacheInvalidationService)).ToArray())
                    {
                        services.Remove(descriptor);
                    }
                }
                else
                {
                    services.RemoveAll<DbContextOptions<AccessControlDbContext>>();
                    services.RemoveAll<IDbContextOptionsConfiguration<AccessControlDbContext>>();
                    services.RemoveAll<AccessControlDbContext>();
                    services.AddDbContext<AccessControlDbContext>(options => options.UseSqlite(connectionString));
                }
            });
        }
    }

    private sealed class BenchmarkRedisCache : IRedisCacheService
    {
        private readonly ConcurrentDictionary<string, (string Value, DateTimeOffset Expires)> _entries = new();
        public Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_entries.TryGetValue(key, out var entry) && entry.Expires > DateTimeOffset.UtcNow
                ? entry.Value : null);
        }
        public Task SetValueAsync(string key, string value, TimeSpan lifetime, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _entries[key] = (value, DateTimeOffset.UtcNow.Add(lifetime));
            return Task.CompletedTask;
        }
        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _entries.TryRemove(key, out _);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuthorizationStore : IAuthorizationStore
    {
        private static readonly EndpointPolicy Policy = new("benchmark-tenant", "GET",
            "/benchmark/{id}", false, [BenchmarkRead], Organization);
        private static readonly IReadOnlySet<Guid> Grants = new HashSet<Guid> { BenchmarkRead };

        public Task<EndpointPolicy?> GetPolicyAsync(string tenantId, Guid unitOrganizationId,  string httpMethod,
            string routeTemplate, CancellationToken cancellationToken) => Task.FromResult<EndpointPolicy?>(
                tenantId == Policy.TenantId && unitOrganizationId == Policy.UnitOrganizationId && httpMethod == Policy.HttpMethod &&
                routeTemplate == Policy.RouteTemplate ? Policy : null);

        public Task<IReadOnlySet<Guid>> GetEntitlementsAsync(string tenantId, Guid unitOrganizationId, IReadOnlyList<Guid> roleIds,
            CancellationToken cancellationToken) => Task.FromResult(tenantId == Policy.TenantId &&
                unitOrganizationId == Organization && roleIds.Contains(Reader) ? Grants :
                (IReadOnlySet<Guid>)new HashSet<Guid>());

        public Task<IReadOnlyList<Guid>> GetAssignedEntitlementIdsAsync(string tenantId, Guid unitOrganizationId,
            Guid roleId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>([BenchmarkRead]);

        public Task UpsertPolicyAsync(EndpointPolicy policy, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeletePolicyAsync(string tenantId, Guid unitOrganizationId,  string httpMethod, string routeTemplate,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReplaceRoleEntitlementsAsync(string tenantId, Guid unitOrganizationId, Guid roleId, IReadOnlyList<Guid> entitlementIds,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class BenchmarkAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers.Authorization.ToString() != "Bearer " + BenchmarkBearer)
                return Task.FromResult(AuthenticateResult.NoResult());
            ClaimsIdentity identity = new([new Claim("oid", BenchmarkOid)], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private enum BenchmarkStore { Fake, Sqlite }
    private sealed record BenchmarkCase(string Name, DecisionRequest Request, bool ExpectedPermit, int Weight);
    private sealed record BenchmarkResult(string Scenario, int Concurrency, int Requests, double WallTimeMs,
        double RequestsPerSecond, double P50Ms, double P95Ms, double P99Ms, double MaxMs);
    private sealed record BenchmarkReport(DateTimeOffset TimestampUtc, string Runtime, string Os, int ProcessorCount,
        int RequestsPerRun, IReadOnlyList<BenchmarkCase> Workload, IReadOnlyList<BenchmarkResult> Results);
}

file sealed class BenchmarkActorProvider : ICurrentActorProvider
{
    public string ActorId => "benchmark-setup";
}
