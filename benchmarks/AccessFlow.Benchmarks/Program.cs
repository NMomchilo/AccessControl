using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using AccessControl.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using UserManagement.Client;
using UserManagement.Contracts;

BenchmarkSettings settings = BenchmarkSettings.Parse(args);
using RSA signingRsa = RSA.Create(2048);
using RSA validationRsa = RSA.Create();
validationRsa.ImportParameters(signingRsa.ExportParameters(false));
RsaSecurityKey signingKey = new(signingRsa) { KeyId = "access-flow-benchmark" };
RsaSecurityKey validationKey = new(validationRsa) { KeyId = signingKey.KeyId };
DirectoryInfo? repositoryDirectory = new(AppContext.BaseDirectory);
while (repositoryDirectory is not null &&
    !File.Exists(Path.Combine(repositoryDirectory.FullName, "AccessControl.sln")))
{
    repositoryDirectory = repositoryDirectory.Parent;
}

string outputDirectory = repositoryDirectory?.FullName ?? Environment.CurrentDirectory;
WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    EnvironmentName = Environments.Development,
    Args = []
});
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Logging.ClearProviders();
builder.Configuration["AccessControl:Module"] = "integration";
builder.Configuration["AccessControl:DefaultTenantId"] = "default";
builder.Configuration["AccessControl:RoleIdClaimType"] = UserManagementClaims.RoleId;
builder.Configuration["AccessControl:RoleIdentityAuthenticationType"] = UserManagementClaims.IdentityAuthenticationType;
builder.Configuration["UserManagement:AllowInsecureHttpForDevelopment"] = "true";
builder.Services.AddSingleton<FlowCounters>();
builder.Services.AddTransient<UserManagementCountHandler>();
builder.Services.AddTransient<AccessControlCountHandler>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = "access-flow-benchmark",
            ValidAudience = "access-flow-benchmark-api",
            IssuerSigningKey = validationKey,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddUserManagement(builder.Configuration);
builder.Services.AddAccessControl(builder.Configuration);
builder.Services.AddHttpClient(nameof(HttpUserManagementClient))
    .AddHttpMessageHandler<UserManagementCountHandler>();
builder.Services.AddHttpClient(nameof(HttpAccessControlDecisionClient))
    .AddHttpMessageHandler<AccessControlCountHandler>();

WebApplication app = builder.Build();
app.UseRouting();
app.UseAuthentication();
app.UseUserManagement();
app.UseAccessControl();
app.UseAuthorization();
app.MapControllers();
await app.StartAsync();
try
{
    using HttpClient client = new() { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(30) };
    FlowCounters counters = app.Services.GetRequiredService<FlowCounters>();
    string hotUser = CreateToken(Guid.NewGuid(), signingKey);
    await SendAsync(client, hotUser, "/integration/42", StatusCodes.Status200OK);
    long controllerBeforeDeny = counters.Snapshot().ControllerCalls;
    await SendAsync(client, hotUser, "/denied/42", StatusCodes.Status403Forbidden);
    if (counters.Snapshot().ControllerCalls != controllerBeforeDeny)
    {
        throw new InvalidOperationException("Denied request reached the controller.");
    }

    string[] parts = hotUser.Split('.');
    parts[2] = (parts[2][0] == 'A' ? "B" : "A") + parts[2][1..];
    await SendAsync(client, string.Join('.', parts), "/integration/42", StatusCodes.Status401Unauthorized);
    if (counters.Snapshot().ControllerCalls != controllerBeforeDeny)
    {
        throw new InvalidOperationException("Request with invalid JWT signature reached the controller.");
    }

    string[] manyUsers = Enumerable.Range(0, settings.UserCount)
        .Select(_ => CreateToken(Guid.NewGuid(), signingKey)).ToArray();
    foreach (string user in manyUsers)
    {
        await SendAsync(client, user, "/integration/42", StatusCodes.Status200OK);
    }

    for (int index = 0; index < Math.Min(200, settings.Requests); index++)
    {
        await SendAsync(client, hotUser, "/integration/42", StatusCodes.Status200OK);
    }

    List<FlowBenchmarkResult> results = [];
    foreach (int concurrency in settings.Concurrency)
    {
        results.Add(await MeasureAsync("one warm user", client, counters, [hotUser], settings.Requests,
            concurrency));
        results.Add(await MeasureAsync($"{settings.UserCount} existing users", client, counters,
            manyUsers, settings.Requests, concurrency));
    }

    string[] newUsers = Enumerable.Range(0, settings.NewUsers)
        .Select(_ => CreateToken(Guid.NewGuid(), signingKey)).ToArray();
    results.Add(await MeasureAsync("new user per request", client, counters, newUsers, settings.NewUsers,
        settings.Concurrency.Max()));

    FlowBenchmarkReport report = new(DateTimeOffset.UtcNow, RuntimeInformation.FrameworkDescription,
        RuntimeInformation.OSDescription, Environment.ProcessorCount,
        "Locally signed RS256 JWT, validated on every request", settings, results);
    string outputPath = Path.GetFullPath(settings.Output, outputDirectory);
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(report,
        new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Results saved to {outputPath}");
    Console.WriteLine("Scenario | concurrency | requests/s | p95 ms | UM resolves | UM revision polls | AC decisions | controller calls");
    foreach (FlowBenchmarkResult result in results)
    {
        Console.WriteLine($"{result.Scenario} | {result.Concurrency} | {result.RequestsPerSecond:F0} | " +
            $"{result.P95Ms:F2} | {result.UserResolutions} | {result.RevisionPolls} | " +
            $"{result.AccessDecisions} | {result.ControllerCalls}");
    }
}
finally
{
    await app.StopAsync();
    await app.DisposeAsync();
}

static string CreateToken(Guid oid, RsaSecurityKey signingKey)
{
    JwtSecurityToken token = new("access-flow-benchmark", "access-flow-benchmark-api",
        [new Claim("oid", oid.ToString("D")), new Claim("scp", "user_impersonation")],
        DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddHours(1),
        new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256));
    return new JwtSecurityTokenHandler().WriteToken(token);
}

static async Task<FlowBenchmarkResult> MeasureAsync(string scenario, HttpClient client, FlowCounters counters,
    IReadOnlyList<string> users, int requests, int concurrency)
{
    FlowCounterSnapshot before = counters.Snapshot();
    long[] ticks = new long[requests];
    int next = -1;
    Stopwatch wall = Stopwatch.StartNew();
    Task[] workers = Enumerable.Range(0, concurrency).Select(async _ =>
    {
        while (true)
        {
            int index = Interlocked.Increment(ref next);
            if (index >= requests)
            {
                return;
            }

            long started = Stopwatch.GetTimestamp();
            await SendAsync(client, users[index % users.Count], "/integration/42", StatusCodes.Status200OK);
            ticks[index] = Stopwatch.GetTimestamp() - started;
        }
    }).ToArray();
    await Task.WhenAll(workers);
    wall.Stop();
    Array.Sort(ticks);
    FlowCounterSnapshot after = counters.Snapshot();
    double ToMs(long value) => value * 1000.0 / Stopwatch.Frequency;
    return new FlowBenchmarkResult(scenario, concurrency, requests, requests / wall.Elapsed.TotalSeconds,
        ToMs(ticks[(int)Math.Ceiling(requests * 0.50) - 1]),
        ToMs(ticks[(int)Math.Ceiling(requests * 0.95) - 1]),
        ToMs(ticks[(int)Math.Ceiling(requests * 0.99) - 1]),
        after.UserResolutions - before.UserResolutions, after.RevisionPolls - before.RevisionPolls,
        after.AccessDecisions - before.AccessDecisions, after.ControllerCalls - before.ControllerCalls);
}

static async Task SendAsync(HttpClient client, string token, string route, int expectedStatus)
{
    using HttpRequestMessage request = new(HttpMethod.Get, route);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    using HttpResponseMessage response = await client.SendAsync(request);
    if ((int)response.StatusCode != expectedStatus)
    {
        throw new InvalidOperationException($"Expected HTTP {expectedStatus} for {route}, got {(int)response.StatusCode}.");
    }
}

internal sealed record BenchmarkSettings(int Requests, int[] Concurrency, int UserCount, int NewUsers, string Output)
{
    public static BenchmarkSettings Parse(string[] args)
    {
        int requests = 5000;
        int[] concurrency = [1, 16, 64];
        int userCount = 100;
        int newUsers = 200;
        string output = "artifacts/benchmarks/access-flow-docker.json";
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length)
            {
                throw new ArgumentException("Each option requires a value.");
            }

            switch (args[i])
            {
                case "--requests": requests = int.Parse(args[i + 1]); break;
                case "--concurrency": concurrency = args[i + 1].Split(',').Select(int.Parse).ToArray(); break;
                case "--users": userCount = int.Parse(args[i + 1]); break;
                case "--new-users": newUsers = int.Parse(args[i + 1]); break;
                case "--output": output = args[i + 1]; break;
                default: throw new ArgumentException($"Unknown option: {args[i]}");
            }
        }

        if (requests < 1 || userCount < 1 || newUsers < 1 || concurrency.Length == 0 ||
            concurrency.Any(value => value < 1))
        {
            throw new ArgumentException("Requests, users, new users, and concurrency must be positive.");
        }

        return new BenchmarkSettings(requests, concurrency, userCount, newUsers, output);
    }
}

public sealed class FlowCounters
{
    private long _userResolutions;
    private long _revisionPolls;
    private long _accessDecisions;
    private long _controllerCalls;

    public void UserResolution() => Interlocked.Increment(ref _userResolutions);
    public void RevisionPoll() => Interlocked.Increment(ref _revisionPolls);
    public void AccessDecision() => Interlocked.Increment(ref _accessDecisions);
    public void ControllerCall() => Interlocked.Increment(ref _controllerCalls);

    internal FlowCounterSnapshot Snapshot() => new(Interlocked.Read(ref _userResolutions),
        Interlocked.Read(ref _revisionPolls), Interlocked.Read(ref _accessDecisions),
        Interlocked.Read(ref _controllerCalls));
}

internal sealed record FlowCounterSnapshot(long UserResolutions, long RevisionPolls,
    long AccessDecisions, long ControllerCalls);

internal sealed class UserManagementCountHandler(FlowCounters counters) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.AbsolutePath == "/v1/users/resolve")
        {
            counters.UserResolution();
        }
        else if (request.RequestUri?.AbsolutePath == "/v1/users/revision")
        {
            counters.RevisionPoll();
        }

        return base.SendAsync(request, cancellationToken);
    }
}

internal sealed class AccessControlCountHandler(FlowCounters counters) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.AbsolutePath == "/v1/decisions")
        {
            counters.AccessDecision();
        }

        return base.SendAsync(request, cancellationToken);
    }
}

[ApiController]
public sealed class FlowBenchmarkController(FlowCounters counters) : ControllerBase
{
    [HttpGet("/integration/{id}")]
    public IActionResult Get(string id)
    {
        if (!User.HasClaim(UserManagementClaims.Role, "reviewer"))
        {
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        counters.ControllerCall();
        return Ok();
    }

    [HttpGet("/denied/{id}")]
    public IActionResult Denied(string id)
    {
        counters.ControllerCall();
        return Ok();
    }
}

internal sealed record FlowBenchmarkResult(string Scenario, int Concurrency, int Requests,
    double RequestsPerSecond, double P50Ms, double P95Ms, double P99Ms,
    long UserResolutions, long RevisionPolls, long AccessDecisions, long ControllerCalls);

internal sealed record FlowBenchmarkReport(DateTimeOffset TimestampUtc, string Runtime, string Os,
    int ProcessorCount, string Authentication, BenchmarkSettings Settings,
    IReadOnlyList<FlowBenchmarkResult> Results);
