# End-to-end access flow benchmark — 2026-11-09

## What was measured

The [benchmark consumer](../benchmarks/AccessFlow.Benchmarks/Program.cs) runs an ASP.NET Core controller with this order:

1. Validate a locally signed RS256 JWT containing `oid` and `scp`.
2. Run `UserManagement.Client` to resolve the user by `oid` or read its bounded cache, then add the `reviewer` role to `HttpContext.User`.
3. Run `AccessControl.Client` to send a decision request to the Access Control service.
4. Enter the controller only on Permit. The controller checks that the managed `reviewer` claim exists.

The benchmark consumer, User Management service, and Access Control service ran as separate local HTTP processes. User Management used HTTP in Development; Access Control used HTTPS. Both services used dedicated databases in the repository's Docker SQL Server 2022 container. The policy was `GET /integration/{id}` in module `integration`, requiring `integration.view`; `reviewer` had that grant. The benchmark authenticated with its own RSA key and validated each signature. It did **not** use an Entra-issued token or fetch Entra signing keys.

Before measuring, the harness checked that a valid token for a route without a policy returned 403 and did not reach the controller. A token with a tampered signature returned 401 and also did not reach the controller.

## Results

Each figure is the median of three Release runs on the same workstation. Warm scenarios sent 5,000 requests per run. New-user scenarios sent 200 requests with a different `oid` in each request. Throughput is completed controller requests per second; p95 is client-observed latency across the whole chain.

| Scenario | Concurrent clients | Median throughput | Median p95 | User Management resolves | Access Control decisions | Controller calls |
|---|---:|---:|---:|---:|---:|---:|
| One warm user | 1 | 1,620/s | 0.83 ms | 0 | 5,000 | 5,000 |
| One warm user | 16 | 15,499/s | 1.65 ms | 0 | 5,000 | 5,000 |
| One warm user | 64 | 14,921/s | 7.92 ms | 0 | 5,000 | 5,000 |
| 100 existing users | 1 | 2,014/s | 0.79 ms | 100 | 5,000 | 5,000 |
| 100 existing users | 16 | 15,480/s | 1.76 ms | 0 | 5,000 | 5,000 |
| 100 existing users | 64 | 16,359/s | 6.18 ms | 0 | 5,000 | 5,000 |
| 200 new users | 1 | 212/s | 6.10 ms | 200 | 200 | 200 |
| 200 new users | 16 | 1,510/s | 16.47 ms | 200 | 200 | 200 |
| 200 new users | 64 | 1,649/s | 67.20 ms | 200 | 200 | 200 |

All three runs for each scenario had the displayed User Management, Access Control, and controller call counts. In the 100-existing-user single-client scenario, the longer run outlasted the five-second client cache lifetime for users warmed before the hot-user run. The 100 users were each resolved again; this demonstrates expiry and refresh, not new-user provisioning. The revision poller also made a few background calls in the longer runs; those are recorded separately in the JSON outputs.

The output files are `artifacts/benchmarks/access-flow-final-1-run1.json` through `run3.json`, and similarly `access-flow-final-16-run*.json` and `access-flow-final-64-run*.json`. Each JSON file includes the per-run p50, p95, p99, throughput, and outbound call counts.

## Interpretation and limits

For warm users, the User Management cache avoided a resolve HTTP call on almost every request; the Access Control service still received one decision call per request. New-user provisioning is much slower because every request inserts a user in SQL Server. At 64 concurrent new users, p95 rose to 67.20 ms. That scenario is a sign-in burst stress test, not a typical steady-state authorization request.

The 100-user scenario should not be read as faster than the one-user scenario: the differences are small relative to benchmark ordering, process warm-up, and workstation noise. The run order and the five-second cache lifetime also affect whether a user requires refresh during measurement. These numbers measure localhost processes and one Docker database host; they are not production capacity targets. They exclude Entra discovery/key retrieval, remote network hops, multi-instance consuming services, and a measured route/role distribution from real traffic.

The benchmark requires `UserManagement__ServiceUrl`, `UserManagement__ApiKey`, `AccessControl__ServiceUrl`, and `AccessControl__ApiKey` in the environment. Its Access Control database must contain the policy and reviewer grant described above. For example:

```powershell
dotnet run --project benchmarks/AccessFlow.Benchmarks -c Release -- `
  --requests 5000 --concurrency 16 --users 100 --new-users 200 `
  --output artifacts/benchmarks/access-flow.json
```

The benchmark host binds to a random loopback port and shuts down after writing results. The Development-only HTTP allowance applies only to the benchmark's User Management client. Use HTTPS and representative infrastructure for a staging capacity test.
