# Unit organization benchmark — 2026-11-10

These are short local smoke benchmarks of the new UO-scoped request contracts. They verify the two individual service harnesses and the complete JWT → gateway identity → User Management NuGet → Access Control NuGet → controller path. They are **not** a production capacity estimate or a controlled before/after comparison.

The individual service runs used .NET 10 Release on one Windows host, 200 measured requests at concurrency 8, one tenant, one UO ID, one route, and one role. The Access Control harness used ASP.NET Core's in-process test server; its SQLite case exercised the EF store and authorization cache. The User Management harness used shared in-memory SQLite and a five-second bounded client cache. Those two runs did not include TLS, gateway validation, Entra JWT validation, Docker SQL Server, or network latency.

| Path | Requests/s | p95 latency | Source calls |
|---|---:|---:|---:|
| Access Control in-memory store | 7,398 | 0.94 ms | N/A |
| Access Control SQLite store | 10,568 | 1.03 ms | N/A |
| User Management SQLite lookup each request | 566 | 1.315 ms | 200 |
| User Management warm client cache | 421,585 | 0.002 ms | 0 |

The full-flow run used two fresh dedicated databases (`UoFlowUm20261110` and `UoFlowAc20261110`) in the local Docker SQL Server container. User Management ran over local HTTP and Access Control over local HTTPS; the temporary consuming API validated locally signed RS256 JWTs, added a benchmark-only `Gateway` identity with the UO ID/name, and ran both real middleware packages. It checked that a denied route and invalid JWT did not reach the controller. A `reviewer` role grant and protected endpoint policy were seeded in the same tenant and UO.

| Full flow case | Concurrency | Requests | Requests/s | p95 latency | User resolutions | Access decisions | Controller calls |
|---|---:|---:|---:|---:|---:|---:|---:|
| One warm user | 1 | 200 | 560 | 2.43 ms | 0 | 200 | 200 |
| Ten existing users | 1 | 200 | 522 | 2.62 ms | 0 | 200 | 200 |
| One warm user | 8 | 200 | 2,014 | 13.03 ms | 0 | 200 | 200 |
| Ten existing users | 8 | 200 | 3,312 | 2.79 ms | 0 | 200 | 200 |
| New user on each request | 8 | 20 | 121 | 110.97 ms | 20 | 20 | 20 |

The in-memory cache figure measures a local lookup, not full request throughput. The short runs and one UO produce a high cache hit rate. The new-user case includes database writes. Host scheduling, logging, and a shared Docker container can affect results. Test a representative mix of UOs, roles, routes, revocations, and first-time UO/user creation against a multi-instance SQL Server deployment before sizing production.

Raw outputs: [Access Control](../benchmarks/results/2026-11-10-uo/access-control-local.json), [User Management](../benchmarks/results/2026-11-10-uo/user-management-sqlite.json), and [full access flow](../benchmarks/results/2026-11-10-uo/access-flow-sqlserver.json).
