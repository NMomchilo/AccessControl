# Live SQL Server flow after support package move — 2026-11-20

The six `Shared` projects were packed as version `0.12.0`. Release builds of both services and the consuming API benchmark succeeded with zero warnings or errors. Both services restored `AdminManagement.Security` and `Common.Persistence` from the local `Shared/packages` NuGet feed. Their output DLL hashes matched the corresponding Shared build DLLs.

The services ran against fresh migrated databases, `UMPkgFlow20261120P1` and `ACPkgFlow20261120P1`, in SQL Server 2022 in Rancher Desktop's local Docker engine on `localhost,1434`. The test-only RS256 authority ran at `https://localhost:7250`; User Management ran at `http://localhost:5222`; Access Control ran at `https://localhost:7223`. The [User Management fixture](../../UserManagement/benchmarks/fixtures/live-flow/seed-um.sql) provisioned two users and a role. The [Access Control fixture](../benchmarks/fixtures/live-flow/seed-ac.sql) provisioned a protected `GET /integration/{id}` policy, entitlement, and grant. Fixture copies were retargeted only to the fresh database names.

The [raw flow result](../benchmarks/results/2026-11-20-support-packages/flow.json) records one run of 1,000 measured requests in each warm scenario. The first request traversed User Management resolution, Access Control decision, and the controller exactly once; its cold latency was 764.68 ms. The benchmark also confirmed that a denied route returned `403` and a tampered JWT returned `401`, with neither reaching the controller.

| Warm flow | Concurrency | Requests/s | p95 | UM resolutions | AC decisions | Controller calls |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| One user | 1 | 766 | 1.84 ms | 0 | 1,000 | 1,000 |
| Two users | 1 | 994 | 1.17 ms | 0 | 1,000 | 1,000 |
| One user | 16 | 8,436 | 3.95 ms | 0 | 1,000 | 1,000 |
| Two users | 16 | 10,365 | 3.12 ms | 0 | 1,000 | 1,000 |

To exercise the moved server support packages beyond the ordinary decision path, a synthetic active SuperAdmin token was validated against the local authority. User Management's `POST /v1/unit-organizations` returned `201`, and Access Control's `POST /v1/entitlements` returned `201` after its administrator scope lookup through User Management. SQL queries confirmed that each new row's `LastUpdateUser` was the token's `oid` (`dddddddd-dddd-dddd-dddd-dddddddddddd`). Both `/ready` endpoints returned `200` after the flow, and the three service logs contained no failure or unhandled-exception markers.

This is a one-run integration smoke test, not a performance comparison with earlier three-run medians or a production capacity estimate. The identity provider and `Gateway` identity were local test stand-ins; no production Entra or gateway deployment was involved.
