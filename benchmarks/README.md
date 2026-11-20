# Decision API load benchmark

Fresh Rancher Desktop SQL Server decision, duty-mix, cache-sensitivity, and full middleware flow measurements are in [the 2026-11-20 report](../docs/sqlserver-rancher-benchmarks-2026-11-20.md), with links to every raw run. The [support-package move flow check](../docs/sqlserver-support-package-flow-2026-11-20.md) reran the live two-service path and administrator writes. [The 2026-11-18 report](../docs/sqlserver-live-benchmarks-2026-11-18.md) is historical. `LocalIdentityProvider` supplies short-lived, locally signed JWTs over loopback HTTPS for development-only tests.

Run from the repository root with a Release build:

```powershell
dotnet run --project benchmarks/AccessControl.Benchmarks -c Release -- --requests 5000 --concurrency 1,16,64
```

The harness sends `POST /v1/decisions` with a protected endpoint and a user who must receive Permit. It warms each scenario, then records requests per second and p50, p95, p99, and maximum end-to-end client latency. Any unexpected decision or non-200 result fails the run. Results are written to `artifacts/benchmarks/latest.json`.

Local mode runs the service in an ASP.NET Core in-process test server with two stores:

- **In-memory store:** deterministic policy and grant responses; measures HTTP and decision processing without database work.
- **SQLite store:** the real `SqlAuthorizationStore`, cache, and EF model against a shared local SQLite database. The request supplies the benchmark role ID. The first request for a policy/grant key loads it from SQLite; repeated requests use the bounded cache until invalidation or expiration.

The local test server skips the real network and TLS. It uses a test-only bearer handler for the fixed benchmark oid. SQLite is not SQL Server and its concurrency behavior differs. Logging providers are disabled so console output does not dominate the measurement. The default workload repeats **one** tenant, UO, route, and user combination, making the cache results a high-hit-rate case. These numbers are useful for comparing code changes on the same machine, not for predicting production capacity.

To exercise the current administrative override as well as role grants, run the synthetic [local duty mix](AccessControl.Benchmarks/workloads/local-duty-mix.json):

```powershell
dotnet run --project benchmarks/AccessControl.Benchmarks -c Release -- `
  --workload-file benchmarks/AccessControl.Benchmarks/workloads/local-duty-mix.json `
  --requests 5000 --concurrency 1,16,64 `
  --output artifacts/benchmarks/local-duty-mix.json
```

The weights are illustrative, not observed traffic. The direct decision benchmark sends role IDs and duty flags as JSON. It does not resolve duties through User Management; the full middleware flow below does that. A decision key holder can assert these flags under the current trust model.

## Benchmark a staging deployment

Apply the EF migrations to a dedicated Access Control staging database. Create an active `benchmark.read` entitlement for tenant `benchmark-tenant` and UO ID `33333333-3333-3333-3333-333333333333`, and use its returned ID in a protected policy for module `benchmark`, method `GET`, route `/benchmark/{id}`. Grant the entitlement to the role ID supplied by the benchmark request in Access Control. Use an Entra access token for the request oid with the audience configured in `DecisionEntra`, and a valid decision client key. Then run:

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

For a remote mix of routes and users, pass a JSON workload file. Each case has a name, weight, `DecisionRequest` with `subjectId`, `unitOrganizationId`, `roleIds`, optional `isSuperAdmin` and `isUnitAdmin`, and expected Permit/Deny result. The harness cycles through cases according to their weights and fails on an unexpected decision. The single bearer token must match every non-null `subjectId` in the workload; use separate runs for different users. Seed matching Access Control grants for role-permitted cases:

```powershell
dotnet run --project benchmarks/AccessControl.Benchmarks -c Release -- `
  --remote-url https://localhost:7219 `
  --workload-file benchmarks/AccessControl.Benchmarks/workloads/appdb-available.json `
  --requests 5000 --concurrency 1,16,64 `
  --output artifacts/benchmarks/appdb-available.json
```

The [available-policy benchmark report](../docs/available-policy-mix-benchmark-2026-11-09.md) records runs against the only configured `AppDb` policy and a separate uniform five-case test mix. The repository has no request-frequency data, so the uniform weights are not real traffic weights.

## Full JWT → User Management → Access Control → controller flow

The [access-flow benchmark](AccessFlow.Benchmarks/Program.cs) starts a temporary consuming API, validates locally signed RS256 JWTs, adds a benchmark-only `Gateway` identity for UO ID `33333333-3333-3333-3333-333333333333`, runs both real middleware clients, calls the two live services backed by Docker SQL Server, and checks that the controller ran. It measures one cached user and a configurable set of existing users. Create the UO and role explicitly, then provision each oid and UO membership and assign the role ID through User Management administrator APIs. Put one oid GUID per line in a text file. Configure the four service URL/key settings as environment variables. Seed `GET /integration/{id}` in the same UO with an `integration.view` grant for that role ID in Access Control. This benchmark exercises the role-grant path; duty overrides are covered by the direct decision workload above. Then run:

```powershell
dotnet run --project benchmarks/AccessFlow.Benchmarks -c Release -- `
  --requests 5000 --concurrency 16 --users 100 --user-ids-file .\provisioned-oids.txt `
  --output artifacts/benchmarks/access-flow.json
```

The [UO benchmark report](../docs/unit-organization-benchmark-2026-11-10.md) contains current local smoke measurements and raw JSON. The [tenant/role-ID benchmark report](../docs/tenant-role-ids-benchmark-2026-11-10.md) contains earlier measurements and raw JSON outputs; it predates UO scoping. The [earlier end-to-end report](../docs/end-to-end-access-flow-benchmark-2026-11-09.md) used string roles. The benchmark JWT is signed and verified locally; it is not an Entra token. The benchmark-only gateway identity is not a production gateway implementation.

## User Management benchmark

The direct user-resolution and cache benchmark lives in the separate `UserManagement` repository. This benchmark uses the `0.12.0` client NuGets from `.packages`, as does the rest of this repository. The combined access-flow benchmark above calls both running services.
