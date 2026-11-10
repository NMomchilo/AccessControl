using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UserManagement.Client;
using UserManagement.Contracts;
using UserManagement.Infrastructure.Database;

const string oid = "9155ff4d-2708-4770-b87e-e238645ec5df";
UserIdentityRequest request = new("default", oid, "benchmark@example.com", "Benchmark User");
int count = 5000;
int concurrency = 16;
string database = "sqlite";
string? remoteUrl = null;
string output = "artifacts/benchmarks/user-management-sqlite.json";
for (int i = 0; i < args.Length; i += 2)
{
    if (i + 1 >= args.Length)
    {
        throw new ArgumentException("Every option requires a value.");
    }

    switch (args[i])
    {
        case "--requests": count = int.Parse(args[i + 1]); break;
        case "--concurrency": concurrency = int.Parse(args[i + 1]); break;
        case "--database": database = args[i + 1].ToLowerInvariant(); break;
        case "--remote-url": remoteUrl = args[i + 1]; break;
        case "--output": output = args[i + 1]; break;
        default: throw new ArgumentException($"Unknown option: {args[i]}");
    }
}

if (count < 1 || concurrency < 1)
{
    throw new ArgumentException("Requests and concurrency must be positive.");
}

if (database is not ("sqlite" or "sqlserver"))
{
    throw new ArgumentException("--database must be sqlite or sqlserver.");
}

SqliteConnection? keeper = null;
using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
ICountedUserClient source;
string sourceLabel;
if (remoteUrl is not null)
{
    string key = Environment.GetEnvironmentVariable("UserManagementBenchmark__ClientKey")
        ?? throw new ArgumentException("Set UserManagementBenchmark__ClientKey for remote mode.");
    http.BaseAddress = new Uri(remoteUrl.TrimEnd('/') + "/", UriKind.Absolute);
    source = new RemoteClient(http, key);
    sourceLabel = "HTTP service lookup";
}
else
{
    DbContextOptionsBuilder<UserManagementDbContext> dbOptionsBuilder = new();
    if (database == "sqlite")
    {
        string connectionString = $"Data Source=um-benchmark-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        dbOptionsBuilder.UseSqlite(connectionString);
    }
    else
    {
        string connectionString = Environment.GetEnvironmentVariable("UserManagementBenchmark__ConnectionString")
            ?? throw new ArgumentException("Set UserManagementBenchmark__ConnectionString for SQL Server mode.");
        dbOptionsBuilder.UseSqlServer(connectionString);
    }

    DbContextOptions<UserManagementDbContext> dbOptions = dbOptionsBuilder.Options;
    await using (UserManagementDbContext db = new(dbOptions))
    {
        if (database == "sqlite")
        {
            await db.Database.EnsureCreatedAsync();
        }
        else if (!await db.Database.CanConnectAsync() || (await db.Database.GetPendingMigrationsAsync()).Any())
        {
            throw new InvalidOperationException("SQL Server benchmark database must exist with all migrations applied.");
        }

        SqlUserStore store = new(db, NullLogger<SqlUserStore>.Instance);
        await store.ResolveAsync(request.TenantId, request.ObjectId, request.Email, request.DisplayName, CancellationToken.None);
    }

    source = new StoreClient(dbOptions);
    sourceLabel = $"{database} lookup per request";
}

using UserResolutionCache cached = new(source,
    Options.Create(new UserManagementClientOptions { MaximumCachedUsers = 100, CacheLifetime = TimeSpan.FromSeconds(5) }),
    NullLogger<UserResolutionCache>.Instance);
List<BenchmarkResult> results = [];
results.Add(await MeasureAsync(sourceLabel, source, source, request, count, concurrency));
await cached.ResolveAsync(request, CancellationToken.None);
results.Add(await MeasureAsync("bounded 5-second cache", cached, source, request, count, concurrency));

BenchmarkReport report = new(DateTimeOffset.UtcNow, RuntimeInformation.FrameworkDescription,
    RuntimeInformation.OSDescription, count, concurrency, results);
string fullPath = Path.GetFullPath(output);
Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
await File.WriteAllTextAsync(fullPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Benchmark results saved to {fullPath}");
foreach (BenchmarkResult result in results)
{
    Console.WriteLine($"{result.Scenario}: {result.RequestsPerSecond:F0} req/s, " +
        $"p50 {result.P50Ms:F3} ms, p95 {result.P95Ms:F3} ms, source calls {result.SourceCalls}");
}
if (keeper is not null)
{
    await keeper.DisposeAsync();
}

static async Task<BenchmarkResult> MeasureAsync(string scenario, IUserManagementClient client,
    ICountedUserClient source, UserIdentityRequest request, int count, int concurrency)
{
    int before = source.Calls;
    long[] ticks = new long[count];
    int next = -1;
    Stopwatch wall = Stopwatch.StartNew();
    Task[] workers = Enumerable.Range(0, concurrency).Select(async _ =>
    {
        while (true)
        {
            int index = Interlocked.Increment(ref next);
            if (index >= count)
            {
                return;
            }

            long started = Stopwatch.GetTimestamp();
            UserIdentityResponse response = await client.ResolveAsync(request, CancellationToken.None);
            if (!response.IsActive || !response.Roles.Any(role => role.Code == "reviewer" && role.RoleId != Guid.Empty))
            {
                throw new InvalidOperationException("Benchmark user resolution failed.");
            }

            ticks[index] = Stopwatch.GetTimestamp() - started;
        }
    }).ToArray();
    await Task.WhenAll(workers);
    wall.Stop();
    Array.Sort(ticks);
    double ToMs(long value) => value * 1000.0 / Stopwatch.Frequency;
    return new BenchmarkResult(scenario, count / wall.Elapsed.TotalSeconds,
        ToMs(ticks[(int)Math.Ceiling(count * 0.50) - 1]),
        ToMs(ticks[(int)Math.Ceiling(count * 0.95) - 1]), source.Calls - before);
}

internal interface ICountedUserClient : IUserManagementClient
{
    int Calls { get; }
}

internal sealed class StoreClient(DbContextOptions<UserManagementDbContext> options) : ICountedUserClient
{
    private int _calls;
    public int Calls => Volatile.Read(ref _calls);

    public async Task<UserIdentityResponse> ResolveAsync(UserIdentityRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        await using UserManagementDbContext db = new(options);
        SqlUserStore store = new(db, NullLogger<SqlUserStore>.Instance);
        UserManagement.Domain.ManagedUser user = await store.ResolveAsync(request.TenantId, request.ObjectId,
            request.Email, request.DisplayName, cancellationToken);
        return new UserIdentityResponse(user.Id, user.TenantId, user.IsActive,
            user.Roles.Select(role => new UserRoleInfo(role.Id, role.Code, role.DisplayName)).ToArray());
    }
}

internal sealed class RemoteClient(HttpClient http, string key) : ICountedUserClient
{
    private int _calls;
    public int Calls => Volatile.Read(ref _calls);

    public async Task<UserIdentityResponse> ResolveAsync(UserIdentityRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        using HttpRequestMessage message = new(HttpMethod.Post, "v1/users/resolve")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("X-User-Management-Key", key);
        using HttpResponseMessage response = await http.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UserIdentityResponse>(cancellationToken)
            ?? throw new InvalidOperationException("User Management returned no response.");
    }
}

internal sealed record BenchmarkResult(string Scenario, double RequestsPerSecond,
    double P50Ms, double P95Ms, int SourceCalls);
internal sealed record BenchmarkReport(DateTimeOffset TimestampUtc, string Runtime, string Os,
    int Requests, int Concurrency, IReadOnlyList<BenchmarkResult> Results);
