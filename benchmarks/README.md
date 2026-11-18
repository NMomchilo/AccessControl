# Decision API load benchmark

Run from the repository root with a Release build:

```powershell
dotnet run --project benchmarks/AccessControl.Benchmarks -c Release -- --requests 5000 --concurrency 1,16,64
```

The harness sends `POST /v1/decisions` with a protected endpoint and a user who must receive Permit. It warms each scenario, then records requests per second and p50, p95, p99, and maximum end-to-end client latency. Any unexpected decision or non-200 result fails the run. Results are written to `artifacts/benchmarks/latest.json`.

Local mode runs the service in an ASP.NET Core in-process test server with two stores:

- **In-memory store:** deterministic policy and grant responses; measures HTTP and decision processing without database work.
- **SQLite store:** the real `SqlAuthorizationStore`, cache, and EF model against a shared local SQLite database. The request supplies the benchmark role ID. The first request for a policy/grant key loads it from SQLite; repeated requests use the bounded cache until invalidation or expiration.

The local test server skips the real network and TLS. It uses a test-only bearer handler for the fixed benchmark oid. SQLite is not SQL Server and its concurrency behavior differs. Logging providers are disabled so console output does not dominate the measurement. The default workload repeats **one** tenant, UO, route, and user combination, making the cache results a high-hit-rate case. These numbers are useful for comparing code changes on the same machine, not for predicting production capacity.

## Benchmark a staging deployment

Apply the EF migrations to a dedicated Access Control staging database. Create an active `benchmark.read` entitlement for tenant `benchmark-tenant` and UO ID `33333333-3333-3333-3333-333333333333`, and use its returned ID in a protected policy for module `benchmark`, method `GET`, route `/benchmark/{id}`. Grant the entitlement to the role ID supplied by the benchmark request in Access Control. Use an Entra access token for the request oid with the audience configured in `DecisionEntra`, and a decision client key scoped to this module, tenant, and UO. Then run:

```powershell
$env:AccessControlBenchmark__DecisionKey = '<staging decision key>'
$env:AccessControlBenchmark__BearerToken = '<Entra access token for the benchmark oid>'
dotnet run --project benchmarks/AccessControl.Benchmarks -c Release -- `
  --remote-url https://staging-access-control.example/ `
  --requests 5000 --concurrency 1,16,64 `
  --output artifacts/benchmarks/staging.json
```

Remote mode uses a real HTTPS connection to Access Control only. The request supplies role IDs directly; this mode does not measure User Management or role enrichment. Use a staging environment with representative policies, grants, network placement, and logging. Keep the decision key and bearer token in environment variables; the harness does not include them in output. Repeat runs and compare medians because host scheduling and database state can move individual measurements.

The benchmark sends only decision reads. It does not alter policies or role grants.

For a remote mix of routes and users, pass a JSON workload file. Each case has a name, weight, `DecisionRequest` with `subjectId`, `unitOrganizationId`, and `roleIds`, and expected Permit/Deny result. The harness cycles through cases according to their weights and fails on an unexpected decision. The single bearer token must match every non-null `subjectId` in the workload; use separate runs for different users. Seed matching Access Control grants for permitted cases:

```powershell
dotnet run --project benchmarks/AccessControl.Benchmarks -c Release -- `
  --remote-url https://localhost:7219 `
  --workload-file benchmarks/AccessControl.Benchmarks/workloads/appdb-available.json `
  --requests 5000 --concurrency 1,16,64 `
  --output artifacts/benchmarks/appdb-available.json
```

The [available-policy benchmark report](../docs/available-policy-mix-benchmark-2026-11-09.md) records runs against the only configured `AppDb` policy and a separate uniform five-case test mix. The repository has no request-frequency data, so the uniform weights are not real traffic weights.

## Full JWT → User Management → Access Control → controller flow

The [access-flow benchmark](AccessFlow.Benchmarks/Program.cs) starts a temporary consuming API, validates locally signed RS256 JWTs, adds a benchmark-only `Gateway` identity for UO ID `33333333-3333-3333-3333-333333333333`, runs both real middleware packages, calls the two live services backed by Docker SQL Server, and checks that the controller ran. It measures one cached user and a configurable set of existing users. Create the UO and role explicitly, then provision each oid and UO membership and assign the role ID through User Management administrator APIs. Put one oid GUID per line in a text file. Configure the four service URL/key settings as environment variables. Seed `GET /integration/{id}` in the same UO with an `integration.view` grant for that role ID in Access Control. Then run:

```powershell
dotnet run --project benchmarks/AccessFlow.Benchmarks -c Release -- `
  --requests 5000 --concurrency 16 --users 100 --user-ids-file .\provisioned-oids.txt `
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

For the live service benchmark, create UO `33333333-3333-3333-3333-333333333333` and provision the fixed benchmark oid `9155ff4d-2708-4770-b87e-e238645ec5df` there with administrator APIs. No role is needed for this user-resolution benchmark. Then start User Management against that database and use a client credential:

```powershell
$env:UserManagementBenchmark__ClientKey = '<client credential>'
dotnet run --project benchmarks/UserManagement.Benchmarks -c Release -- `
  --remote-url http://localhost:5220 --requests 5000 --concurrency 16 `
  --output artifacts/benchmarks/user-management-http.json
```

The first scenario calls SQL Server or HTTP on every request. The second scenario uses warm hits in the bounded client cache and reports zero source calls during measurement. This warm-hit figure is an in-process cache lookup rate, not service throughput. See the [Docker SQL Server benchmark report](../docs/docker-sqlserver-benchmark-2026-11-09.md) for measured results and limitations.
