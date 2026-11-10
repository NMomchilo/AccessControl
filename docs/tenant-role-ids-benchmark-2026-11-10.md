# Tenant-scoped role-ID benchmark — 2026-11-10

These are **single local runs**, intended to verify that the updated service and NuGet paths work and to show the difference between repeated lookups and warm cache hits. They are not a production capacity estimate or a controlled before/after comparison with the previous string-role build.

SQL Server 2022 ran in the repository's Docker container. The two services used dedicated databases, `TenantRoleUmTest20261110` and `TenantRoleAcTest20261110`, on the same Windows host as the clients. At the time of this benchmark, the migration chain included a legacy role-string conversion; the current first-version migrations have since replaced that chain with clean initial schemas. These numbers measure the service and middleware code paths, not the new migration. The end-to-end benchmark validated locally signed RS256 JWTs, ran both NuGet middleware packages, and checked that denied or invalid-token requests never reached the controller.

| Path | Requests | Concurrency | Throughput | p95 | Source calls during measurement |
|---|---:|---:|---:|---:|---:|
| Access Control local in-memory store | 500 | 8 | 8,352/s | 1.15 ms | N/A |
| Access Control local SQLite store | 500 | 8 | 6,716/s | 1.37 ms | N/A |
| Access Control HTTP + SQL Server, warm policy/grant cache | 500 | 8 | 9,656/s | 0.74 ms | 500 decisions |
| User Management SQLite lookup on every request | 500 | 8 | 601/s | 2.12 ms | 500 resolutions |
| User Management SQL Server lookup on every request | 500 | 8 | 1,233/s | 9.51 ms | 500 resolutions |
| User Management HTTP + SQL Server lookup on every request | 500 | 8 | 1,608/s | 4.78 ms | 500 resolutions |
| User Management client warm cache over the HTTP source | 500 | 8 | 1,381,979/s | 0.001 ms | 0 resolutions |

The warm User Management cache number measures an **in-process memory lookup** and excludes JWT validation, middleware, HTTP, and SQL Server. The full flow includes those steps:

| Full JWT → User Management → Access Control → controller case | Requests | Concurrency | Throughput | p95 | User resolutions | Access decisions | Controller calls |
|---|---:|---:|---:|---:|---:|---:|---:|
| One warm user | 200 | 1 | 1,033/s | 1.21 ms | 0 | 200 | 200 |
| Ten existing users | 200 | 1 | 871/s | 1.46 ms | 0 | 200 | 200 |
| One warm user | 200 | 8 | 4,750/s | 3.62 ms | 0 | 200 | 200 |
| Ten existing users | 200 | 8 | 6,257/s | 1.46 ms | 0 | 200 | 200 |
| New user per request | 20 | 8 | 314/s | 41.51 ms | 20 | 20 | 20 |

The new-user case includes SQL inserts for both a user and its first role assignment. Each run was short and used one route, one policy, and a small role set. Windows scheduling, log output, and shared Docker resources can affect the measurements. Repeat longer runs with representative route, tenant, role-ID, denial, and revocation frequencies before making capacity decisions.

Raw outputs: [Access Control local](../benchmarks/results/2026-11-10-tenant-role-ids/access-control-role-ids-local.json), [Access Control HTTP/SQL Server](../benchmarks/results/2026-11-10-tenant-role-ids/access-control-role-ids-sqlserver-http.json), [User Management SQLite](../benchmarks/results/2026-11-10-tenant-role-ids/user-management-role-ids-sqlite.json), [User Management SQL Server](../benchmarks/results/2026-11-10-tenant-role-ids/user-management-role-ids-sqlserver.json), [User Management HTTP](../benchmarks/results/2026-11-10-tenant-role-ids/user-management-role-ids-http.json), and [full access flow](../benchmarks/results/2026-11-10-tenant-role-ids/access-flow-role-ids-sqlserver.json).
