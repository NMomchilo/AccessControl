# Decision API load benchmark

Run from the repository root with a Release build:

```powershell
dotnet run --project benchmarks/AccessControl.Benchmarks -c Release -- --requests 5000 --concurrency 1,16,64
```

The harness sends `POST /v1/decisions` with a protected endpoint and a role that must receive Permit. It warms each scenario, then records requests per second and p50, p95, p99, and maximum end-to-end client latency. Any non-Permit or non-200 result fails the run. Results are written to `artifacts/benchmarks/latest.json`.

Local mode runs the service in an ASP.NET Core in-process test server with two stores:

- **In-memory store:** deterministic policy and grant responses; measures HTTP and decision processing without database work.
- **SQLite store:** the real `SqlAuthorizationStore`, cache, and EF model against a shared local SQLite database. The first request for a policy/grant key loads it from SQLite; repeated requests use the bounded cache until invalidation or expiration.

The local test server skips the real network and TLS. SQLite is not SQL Server and its concurrency behavior differs. Logging providers are disabled so console output does not dominate the measurement. The default workload repeats **one** tenant, route, and role combination, making the cache results a high-hit-rate case. These numbers are useful for comparing code changes on the same machine, not for predicting production capacity.

## Benchmark a staging deployment

Apply the EF migration and create a protected policy for tenant `benchmark-tenant`, module `benchmark`, method `GET`, route `/benchmark/{id}`, requiring `benchmark.read`. Grant `benchmark.read` to role `reader` in the same tenant. Then run:

```powershell
$env:AccessControlBenchmark__DecisionKey = '<staging decision key>'
dotnet run --project benchmarks/AccessControl.Benchmarks -c Release -- `
  --remote-url https://staging-access-control.example/ `
  --requests 5000 --concurrency 1,16,64 `
  --output artifacts/benchmarks/staging.json
```

Remote mode uses a real HTTPS connection and the deployment's SQL Server. Use a staging environment with representative policies, grants, network placement, and logging. Keep the decision key in an environment variable; the harness does not include it in output. Repeat runs and compare medians because host scheduling and database state can move individual measurements.

The benchmark sends only decision reads. It does not alter policies or role grants.
