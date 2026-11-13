# Decision API load benchmark

Run from the repository root with a Release build:

```powershell
dotnet run --project benchmarks/AccessControl.Benchmarks -c Release -- --requests 5000 --concurrency 1,16,64
```

The harness sends `POST /v1/decisions` with a protected endpoint and a role that must receive Permit. It warms each scenario, then records requests per second and p50, p95, p99, and maximum end-to-end client latency. Any non-Permit or non-200 result fails the run. Results are written to `artifacts/benchmarks/latest.json`.

Local mode runs the service in an ASP.NET Core in-process test server with two stores:

- **In-memory store:** deterministic policy and grant responses; measures HTTP and decision processing without database work.
- **SQLite store:** the real `SqlAuthorizationStore`, cache, and EF model against a shared local SQLite database. The first request for a policy/grant key loads it from SQLite; repeated requests use the bounded cache until invalidation or expiration.

The local test server skips the real network and TLS. SQLite is not SQL Server and its concurrency behavior differs. Logging providers are disabled so console output does not dominate the measurement. The default workload repeats **one** tenant, UO, route, and role combination, making the cache results a high-hit-rate case. These numbers are useful for comparing code changes on the same machine, not for predicting production capacity.

## Benchmark a staging deployment

Apply the EF migration, create an active `benchmark.read` entitlement for tenant `benchmark-tenant` and UO ID `33333333-3333-3333-3333-333333333333`, and use its returned ID in a protected policy for module `benchmark`, method `GET`, route `/benchmark/{id}`. Grant that entitlement ID to role ID `10954aa4-7532-80de-a80e-a7be3ad4c991` in the same tenant and UO. The local SQLite benchmark seeds its own fixed entitlement ID; the remote setup uses the ID returned by the staging API. This benchmark exercises Access Control only; it does not require a corresponding User Management user. Then run:

```powershell
$env:AccessControlBenchmark__DecisionKey = '<staging decision key>'
dotnet run --project benchmarks/AccessControl.Benchmarks -c Release -- `
  --remote-url https://staging-access-control.example/ `
  --requests 5000 --concurrency 1,16,64 `
  --output artifacts/benchmarks/staging.json
```

Remote mode uses a real HTTPS connection and the deployment's SQL Server. Use a staging environment with representative policies, grants, network placement, and logging. Keep the decision key in an environment variable; the harness does not include it in output. Repeat runs and compare medians because host scheduling and database state can move individual measurements.

The benchmark sends only decision reads. It does not alter policies or role grants.

For a remote mix of routes and role IDs, pass a JSON workload file. Each case has a name, weight, `DecisionRequest` with `roleIds` and `unitOrganizationId`, and expected Permit/Deny result. The harness cycles through cases according to their weights and fails on an unexpected decision. The supplied workload IDs are synthetic; seed matching grants for permitted cases or replace them with IDs returned by User Management:

```powershell
dotnet run --project benchmarks/AccessControl.Benchmarks -c Release -- `
  --remote-url https://localhost:7219 `
  --workload-file benchmarks/AccessControl.Benchmarks/workloads/appdb-available.json `
  --requests 5000 --concurrency 1,16,64 `
  --output artifacts/benchmarks/appdb-available.json
```

The [available-policy benchmark report](../docs/available-policy-mix-benchmark-2026-11-09.md) records runs against the only configured `AppDb` policy and a separate uniform five-case test mix. The repository has no request-frequency data, so the uniform weights are not real traffic weights.

## Full JWT → User Management → Access Control → controller flow

The [access-flow benchmark](AccessFlow.Benchmarks/Program.cs) starts a temporary consuming API, validates locally signed RS256 JWTs, adds a benchmark-only `Gateway` identity for UO ID `33333333-3333-3333-3333-333333333333`, runs both real middleware packages, calls the two live services backed by Docker SQL Server, and checks that the controller ran. It measures one cached user, 100 existing users, and new-user sign-ins. Configure the four service URL/key settings as environment variables. Use `GET /v1/roles?tenantId=default&unitOrganizationId=33333333-3333-3333-3333-333333333333` in User Management and read the `items` array to find that UO's `reviewer` role ID (create the UO and role first if needed), then seed `GET /integration/{id}` in the same UO with an `integration.view` grant for **that role ID** in Access Control. Then run:

```powershell
dotnet run --project benchmarks/AccessFlow.Benchmarks -c Release -- `
  --requests 5000 --concurrency 16 --users 100 --new-users 200 `
  --output artifacts/benchmarks/access-flow.json
```

The [UO benchmark report](../docs/unit-organization-benchmark-2026-11-10.md) contains current local smoke measurements and raw JSON. The [tenant/role-ID benchmark report](../docs/tenant-role-ids-benchmark-2026-11-10.md) contains earlier measurements and raw JSON outputs; it predates UO scoping. The [earlier end-to-end report](../docs/end-to-end-access-flow-benchmark-2026-11-09.md) used string roles. The benchmark JWT is signed and verified locally; it is not an Entra token. The benchmark-only gateway identity is not a production gateway implementation.

## User Management on Docker SQL Server

Apply the User Management migrations to a dedicated SQL Server database before running the direct database benchmark. Keep the connection string outside repository files:

```powershell
$env:UserManagementBenchmark__ConnectionString = '<dedicated SQL Server benchmark connection string>'
dotnet run --project benchmarks/UserManagement.Benchmarks -c Release -- `
  --database sqlserver --requests 5000 --concurrency 16 `
  --output artifacts/benchmarks/user-management-sqlserver.json
```

For the live service benchmark, start User Management against that database and use a client credential:

```powershell
$env:UserManagementBenchmark__ClientKey = '<client credential>'
dotnet run --project benchmarks/UserManagement.Benchmarks -c Release -- `
  --remote-url http://localhost:5220 --requests 5000 --concurrency 16 `
  --output artifacts/benchmarks/user-management-http.json
```

The first scenario calls SQL Server or HTTP on every request. The second scenario uses warm hits in the bounded client cache and reports zero source calls during measurement. This warm-hit figure is an in-process cache lookup rate, not service throughput. See the [Docker SQL Server benchmark report](../docs/docker-sqlserver-benchmark-2026-11-09.md) for measured results and limitations.
