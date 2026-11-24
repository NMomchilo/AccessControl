# Local Redis decision benchmark, 2026-11-24

Command: `AccessControlBenchmark__RedisConnectionString=localhost:6379 dotnet run --project benchmarks/AccessControl.Benchmarks -c Release -- --requests 1000 --concurrency 1,16 --output artifacts/benchmarks/access-control-redis-local.json`. Redis was a local `redis:7-alpine` container. The SQLite scenario ran the Access Control service in a test server with real EF persistence and real Redis cache operations. The in-memory store is a synthetic no-database comparator.

| Scenario | Concurrency | Requests/s | p50 | p95 |
| --- | ---: | ---: | ---: | ---: |
| Synthetic in-memory store | 1 | 2,987 | 0.31 ms | 0.42 ms |
| Synthetic in-memory store | 16 | 30,026 | 0.36 ms | 0.63 ms |
| SQLite store with Redis | 1 | 661 | 1.41 ms | 2.17 ms |
| SQLite store with Redis | 16 | 7,714 | 1.81 ms | 3.58 ms |

This run verifies a local Redis-backed decision path. It does not measure SQL Server, a deployed Redis network, Entra authentication, or the complete User Management to Access Control flow. Historical benchmark results used a different cache design and are not directly comparable capacity claims.
