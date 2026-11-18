# Live SQL Server flow and Access Control benchmarks — 2026-11-18

These are **single local runs** on Windows with .NET 10 and SQL Server 2022 in Docker. The two services used separate fresh databases, `UserManagementFlow20261118` and `AccessControlFlow20261118`, on the User Management Docker SQL Server exposed at port 1434. They ran as real HTTP services on `http://localhost:5220` and `https://localhost:7219`. This is synthetic local traffic, not a production capacity estimate.

## Full consuming API flow

`AccessFlow.Benchmarks` started a consuming API on loopback. Its JWT middleware validated RS256 tokens from the test-only HTTPS authority in `benchmarks/LocalIdentityProvider`. A benchmark-only authenticated `Gateway` identity supplied the unit organization. The middleware called the live User Management and Access Control services backed by SQL Server, then allowed or blocked the controller.

The **first cold request** took 168.76 ms, with one User Management resolution, one Access Control decision, and one controller call. This includes startup and authentication metadata work, so it is not a steady-state latency. A denied route returned 403 without reaching its controller, and a tampered JWT returned 401 without reaching a controller.

| Warm case | Concurrency | Requests | Throughput | p95 | UM resolutions during measurement | AC decisions | Controller calls |
|---|---:|---:|---:|---:|---:|---:|---:|
| One user | 1 | 1,000 | 1,094/s | 1.24 ms | 0 | 1,000 | 1,000 |
| Two users | 1 | 1,000 | 1,079/s | 1.37 ms | 0 | 1,000 | 1,000 |
| One user | 16 | 1,000 | 8,816/s | 4.07 ms | 0 | 1,000 | 1,000 |
| Two users | 16 | 1,000 | 10,143/s | 2.79 ms | 0 | 1,000 | 1,000 |

The zero resolution counts reflect the bounded User Management client cache after warming. Every measured request still made an Access Control decision HTTP call. Access Control had a warm policy and grant cache, so these warm cases do not issue SQL queries for each decision. [Raw flow output](../benchmarks/results/2026-11-18-live/access-flow-sqlserver.json).

## Access Control decision HTTP benchmark

The separate Access Control benchmark sent authenticated protected decisions to the live service using the `benchmark-tenant` policy and one role grant. It used the same local test authority and SQL Server backed service.

| Concurrency | Requests | Throughput | p50 | p95 | p99 |
|---:|---:|---:|---:|---:|---:|
| 1 | 1,000 | 1,730/s | 0.54 ms | 0.80 ms | 1.03 ms |
| 16 | 1,000 | 16,559/s | 0.73 ms | 2.51 ms | 5.39 ms |

This is a single hot policy and grant key. It measures HTTP, token validation, and cached decision evaluation; it does not represent a broad route or role mix. [Raw decision output](../benchmarks/results/2026-11-18-live/access-control-http-sqlserver.json).

## Reproduction

The fixture SQL is in `benchmarks/fixtures/live-flow/seed-ac.sql` and the User Management repository's `benchmarks/fixtures/live-flow/seed-um.sql`. The fixtures now target fresh `AccessControlFlowIsActive20261118` and `UserManagementFlowIsActive20261118` databases for the current schema; the recorded measurements above used the earlier schema and have not been rerun. Start the User Management SQL Server, the local identity provider with `ASPNETCORE_ENVIRONMENT=Development`, and both services with their connection strings set to those dedicated databases. Apply their initial migrations before running the fixture SQL. Use the Development client keys and configure both services' JWT authority and audience as `https://localhost:7250` and `api://local-flow-test`. Configure the Access Control decision client to allow modules `integration` and `benchmark` and tenants `default` and `benchmark-tenant`.

For the full flow, set `AccessFlowBenchmark__Authority=https://localhost:7250`, the `UserManagement__ServiceUrl` and `AccessControl__ServiceUrl` and their API keys, then run `AccessFlow.Benchmarks` with a file containing the two provisioned OIDs from the fixture and `--requests 1000 --concurrency 1,16 --users 2`. The test authority is restricted to loopback and generates a new key on each start; it must never be used as a production issuer.
