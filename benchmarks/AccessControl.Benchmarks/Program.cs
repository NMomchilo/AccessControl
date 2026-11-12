using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
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

namespace AccessControl.Benchmarks;

internal static class Program
{
    private const string DecisionKey = "local-benchmark-decision-key";
    private static readonly Guid Reader = Guid.Parse("10954aa4-7532-80de-a80e-a7be3ad4c991");
    private static readonly Guid Organization = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DecisionRequest Request = new("benchmark-user", [Reader], "benchmark",
        "GET", "/benchmark/{id}", "benchmark-tenant", Organization);

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
                await MeasureAllAsync("remote SQL Server service", client, key, settings, cases, results);
            }
            else
            {
                await using (BenchmarkFactory fake = new(BenchmarkStore.Fake))
                {
                    using HttpClient client = fake.CreateClient();
                    await MeasureAllAsync("in-memory store", client, DecisionKey, settings, cases, results);
                }

                string dataSource = $"access-control-benchmark-{Guid.NewGuid():N}";
                string connectionString = $"Data Source={dataSource};Mode=Memory;Cache=Shared";
                await using SqliteConnection keeper = new(connectionString);
                await keeper.OpenAsync();
                await using (BenchmarkFactory sqlite = new(BenchmarkStore.Sqlite, connectionString))
                {
                    using HttpClient client = sqlite.CreateClient();
                    await SeedAsync(sqlite.Services);
                    await MeasureAllAsync("SQLite store", client, DecisionKey, settings, cases, results);
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
            item.Request is null || string.IsNullOrWhiteSpace(item.Request.Module) ||
            string.IsNullOrWhiteSpace(item.Request.HttpMethod) ||
            string.IsNullOrWhiteSpace(item.Request.RouteTemplate) ||
            string.IsNullOrWhiteSpace(item.Request.TenantId) || item.Request.RoleIds is null ||
            (item.Request.SubjectId is not null && item.Request.UnitOrganizationId == Guid.Empty) ||
            item.Request.RoleIds.Any(id => id == Guid.Empty)) ||
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
        IAuthorizationStore store = scope.ServiceProvider.GetRequiredService<IAuthorizationStore>();
        await store.UpsertPolicyAsync(new EndpointPolicy("benchmark-tenant", "benchmark", "GET",
            "/benchmark/{id}", false, ["benchmark.read"], Organization), CancellationToken.None);
        await store.ReplaceRoleEntitlementsAsync("benchmark-tenant", Organization, Reader, ["benchmark.read"], CancellationToken.None);
    }

    private static async Task MeasureAllAsync(string scenario, HttpClient client, string key,
        Settings settings, IReadOnlyList<BenchmarkCase> cases, List<BenchmarkResult> results)
    {
        foreach (int concurrency in settings.Concurrency)
        {
            await SendBatchAsync(client, key, Math.Min(200, settings.RequestCount), concurrency, cases, null);
            long[] ticks = new long[settings.RequestCount];
            Stopwatch wall = Stopwatch.StartNew();
            await SendBatchAsync(client, key, settings.RequestCount, concurrency, cases, ticks);
            wall.Stop();
            Array.Sort(ticks);
            double ToMs(long value) => value * 1000.0 / Stopwatch.Frequency;
            results.Add(new BenchmarkResult(scenario, concurrency, settings.RequestCount, wall.Elapsed.TotalMilliseconds,
                settings.RequestCount / wall.Elapsed.TotalSeconds, ToMs(Percentile(ticks, 0.50)),
                ToMs(Percentile(ticks, 0.95)), ToMs(Percentile(ticks, 0.99)), ToMs(ticks[^1])));
        }
    }

    private static async Task SendBatchAsync(HttpClient client, string key, int count, int concurrency,
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
            if (workloadFile is not null && remoteUrl is null)
            {
                throw new ArgumentException("--workload-file requires --remote-url.");
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
            builder.UseSetting("ApiKeys:DecisionKey", DecisionKey);
            builder.UseSetting("ApiKeys:AdminKey", "local-benchmark-admin-key");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
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

    private sealed class FakeAuthorizationStore : IAuthorizationStore
    {
        private static readonly EndpointPolicy Policy = new("benchmark-tenant", "benchmark", "GET",
            "/benchmark/{id}", false, ["benchmark.read"], Organization);
        private static readonly IReadOnlySet<string> Grants = new HashSet<string> { "benchmark.read" };

        public Task<EndpointPolicy?> GetPolicyAsync(string tenantId, Guid unitOrganizationId, string module, string httpMethod,
            string routeTemplate, CancellationToken cancellationToken) => Task.FromResult<EndpointPolicy?>(
                tenantId == Policy.TenantId && unitOrganizationId == Policy.UnitOrganizationId && module == Policy.Module && httpMethod == Policy.HttpMethod &&
                routeTemplate == Policy.RouteTemplate ? Policy : null);

        public Task<IReadOnlySet<string>> GetEntitlementsAsync(string tenantId, Guid unitOrganizationId, IReadOnlyList<Guid> roleIds,
            CancellationToken cancellationToken) => Task.FromResult(tenantId == Policy.TenantId &&
                unitOrganizationId == Organization && roleIds.Contains(Reader) ? Grants :
                (IReadOnlySet<string>)new HashSet<string>());

        public Task UpsertPolicyAsync(EndpointPolicy policy, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeletePolicyAsync(string tenantId, Guid unitOrganizationId, string module, string httpMethod, string routeTemplate,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReplaceRoleEntitlementsAsync(string tenantId, Guid unitOrganizationId, Guid roleId, IReadOnlyList<string> entitlements,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private enum BenchmarkStore { Fake, Sqlite }
    private sealed record BenchmarkCase(string Name, DecisionRequest Request, bool ExpectedPermit, int Weight);
    private sealed record BenchmarkResult(string Scenario, int Concurrency, int Requests, double WallTimeMs,
        double RequestsPerSecond, double P50Ms, double P95Ms, double P99Ms, double MaxMs);
    private sealed record BenchmarkReport(DateTimeOffset TimestampUtc, string Runtime, string Os, int ProcessorCount,
        int RequestsPerRun, IReadOnlyList<BenchmarkCase> Workload, IReadOnlyList<BenchmarkResult> Results);
}
