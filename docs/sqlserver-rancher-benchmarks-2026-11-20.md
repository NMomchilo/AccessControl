# Rancher Desktop SQL Server benchmarks — 2026-11-20

These measurements used the SQL Server container running in **Rancher Desktop's local Docker engine**, not a remote Rancher cluster. The database was SQL Server 2022 Developer Edition CU26 (16.0.4265.3) on Linux. The benchmark client and .NET services ran on Windows 10.0.22631 with .NET runtime 10.0.10 and 20 visible processors. All services were Release builds. The SQL container had no explicit Docker CPU or memory limit. These are synthetic, single-workstation measurements, not production capacity estimates.

Both services used fresh, migrated databases in the same SQL Server container on `localhost,1434`: `UMBench20261120A` and `ACBench20261120A`. The [User Management fixture](../../UserManagement/benchmarks/fixtures/live-flow/seed-um.sql) provisioned two users, one active UO, one role, two memberships, and role assignments. The [Access Control fixture](../benchmarks/fixtures/live-flow/seed-ac.sql) provisioned an `integration` and a `benchmark` protected policy, their entitlements, and role grants. The fixtures' `USE` statements were changed only in temporary copies to target these fresh databases. The services ran on `http://localhost:5220` and `https://localhost:7219`, and the test-only identity provider ran on `https://localhost:7250`. A second Access Control process used the same database on `https://localhost:7221` with `AccessControlCache:MaximumEntries=1` for the cache sensitivity run. The default Access Control cache limit was 10,000 entries with a five-second lifetime. Default Development information logging remained enabled and was redirected to files.

Every table value below is the **median of three independent runs** of 1,000 measured requests per scenario, with each run's normal warm-up. Each latency percentile is the median of that percentile across runs. The first full-flow request was recorded separately. All benchmark expectations passed; the service logs contained no failure or unhandled-exception markers, and both `/ready` endpoints remained ready after the runs.

## Access Control decision HTTP

`AccessControl.Benchmarks` called the live SQL Server-backed service through HTTPS with a valid decision client key and a bearer token issued by the local test authority. The role-only case repeatedly used one protected route, one UO, one role ID, and one grant. The [synthetic duty mix](../benchmarks/AccessControl.Benchmarks/workloads/local-duty-mix.json) used the same route and bearer subject with weights 7:1:1:1 for role Permit, asserted SuperAdmin Permit, asserted UO Admin Permit, and roleless Deny. It exercises the current raw duty-flag decision contract; it does **not** fetch those duties from User Management. The harness checks every expected decision.

| Workload | Concurrency | Median throughput | Median p50 | Median p95 | Median p99 |
| --- | ---: | ---: | ---: | ---: | ---: |
| Role Permit, normal cache | 1 | 1,944/s | 0.49 ms | 0.60 ms | 0.91 ms |
| Role Permit, normal cache | 16 | 17,197/s | 0.70 ms | 1.47 ms | 3.94 ms |
| Role/duty/deny mix, normal cache | 1 | 2,285/s | 0.41 ms | 0.54 ms | 0.85 ms |
| Role/duty/deny mix, normal cache | 16 | 22,923/s | 0.54 ms | 1.34 ms | 3.85 ms |
| Role Permit, cache limited to one entry | 1 | 623/s | 1.49 ms | 2.36 ms | 3.00 ms |
| Role Permit, cache limited to one entry | 16 | 1,092/s | 14.32 ms | 20.33 ms | 24.01 ms |

Raw outputs: [role run 1](../benchmarks/results/2026-11-20-rancher/ac-http-role-1.json), [2](../benchmarks/results/2026-11-20-rancher/ac-http-role-2.json), [3](../benchmarks/results/2026-11-20-rancher/ac-http-role-3.json); [duty mix run 1](../benchmarks/results/2026-11-20-rancher/ac-http-duty-mix-1.json), [2](../benchmarks/results/2026-11-20-rancher/ac-http-duty-mix-2.json), [3](../benchmarks/results/2026-11-20-rancher/ac-http-duty-mix-3.json); [one-entry cache run 1](../benchmarks/results/2026-11-20-rancher/ac-http-cache-one-1.json), [2](../benchmarks/results/2026-11-20-rancher/ac-http-cache-one-2.json), [3](../benchmarks/results/2026-11-20-rancher/ac-http-cache-one-3.json).

The one-entry cache forces frequent eviction between the policy and role-grant keys. At concurrency 16, its median throughput was about **16 times lower** and its p95 about **14 times higher** than the normal-cache role case. This is a sensitivity experiment, not the configured production setting. The normal-cache role throughput varied from 15,249/s to 20,519/s across the three runs, so its median is more representative than a single run. The duty mix is also one hot route; its higher throughput does not establish that duties are faster under a representative production mix.

## Full JWT → User Management → Access Control → controller flow

`AccessFlow.Benchmarks` started a temporary consuming API. It validated RS256 JWTs from the loopback test authority, added a **benchmark-only** authenticated `Gateway` identity, ran both real client middleware components, called the live services, and checked controller execution. The first request resolved the user, made one Access Control decision, and reached the controller; its median cold latency was **164.44 ms**. That cold value includes identity metadata and startup work and is not steady-state request latency. Each run also checked that a denied route returned `403` without reaching the controller and a tampered JWT returned `401` without reaching the controller.

| Warm case | Concurrency | Median throughput | Median p50 | Median p95 | Median p99 | UM resolutions | AC decisions | Controller calls |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| One user | 1 | 1,098/s | 0.85 ms | 1.16 ms | 1.77 ms | 0 | 1,000 | 1,000 |
| Two existing users | 1 | 1,142/s | 0.83 ms | 1.12 ms | 1.64 ms | 0 | 1,000 | 1,000 |
| One user | 16 | 10,458/s | 1.18 ms | 3.19 ms | 10.31 ms | 0 | 1,000 | 1,000 |
| Two existing users | 16 | 13,355/s | 1.08 ms | 1.72 ms | 3.06 ms | 0 | 1,000 | 1,000 |

Raw outputs: [flow run 1](../benchmarks/results/2026-11-20-rancher/access-flow-1.json), [2](../benchmarks/results/2026-11-20-rancher/access-flow-2.json), [3](../benchmarks/results/2026-11-20-rancher/access-flow-3.json). The [two synthetic OIDs](../benchmarks/results/2026-11-20-rancher/provisioned-oids.txt) are the fixture's users.

Zero UM resolution calls during the measured warm intervals show that the client cache served these users. Every measured request still made an Access Control decision HTTP call. The full-flow harness currently requires a role ID in the controller and therefore measures the role-grant path; it does not exercise a roleless SuperAdmin or UO Admin through both services.

## User Management resolution

The separate [User Management report](../../UserManagement/docs/sqlserver-rancher-benchmarks-2026-11-20.md) records three-run direct SQL Server, live HTTP, and warm-cache resolution results against `UMBench20261120A`. Its median direct SQL throughput was **1,918/s** with **11.20 ms p95**; live HTTP resolution was **1,980/s** with **10.27 ms p95**. The cache cases had zero source calls during measurement and represent only in-process memory lookups.

## Conclusion and limits

The normal five-second Access Control policy/grant cache is material to this workload. Constraining it to one entry exposed the SQL Server path and sharply reduced throughput. User Management's bounded cache eliminated repeated resolution HTTP calls for the warmed users in the full flow, but each request still paid for a decision HTTP call. The full-flow security checks passed under these conditions.

The tests used one workstation, one instance of each service, one active UO, two users, two policies, and synthetic weights. They did not measure multi-instance scaling, a representative route/user frequency distribution, revocation delay, outages, or a real Entra/gateway deployment. The local authority generated a fresh signing key and must not be used outside loopback development. Run those scenarios in a dedicated staging environment before making a production capacity or revocation claim.
