# User Management cache benchmark — 2026-11-09

Command:

```powershell
dotnet run --project benchmarks/UserManagement.Benchmarks/UserManagement.Benchmarks.csproj -c Release --no-build -- --requests 5000 --concurrency 16 --output artifacts/benchmarks/user-management-sqlite.json
```

The benchmark resolves an existing user with a real EF Core SQLite in-memory database. It compares a database lookup on every request with warm hits in `UserResolutionCache`. Three runs each used 5,000 requests and 16 concurrent workers. The cache has a 100-user size limit and a five-second absolute lifetime. Individual JSON outputs are `user-management-sqlite-run1.json`, `run2.json`, and `run3.json` in `artifacts/benchmarks/`.

| Scenario | Median throughput | Throughput range | Median p95 | Database calls per run |
|---|---:|---:|---:|---:|
| SQLite lookup per request | 1,824 requests/s | 1,801–2,478 requests/s | 0.675 ms | 5,000 |
| Warm bounded cache | 1,113,115 requests/s | 915,717–1,889,502 requests/s | below timer resolution | 0 |

After replacing striped miss locks with per-user synchronization, a final run with the same settings produced 2,629 requests/s and p95 0.462 ms for SQLite lookups, and 1,749,414 requests/s with 0 database calls for warm cache hits. Its output is `artifacts/benchmarks/user-management-sqlite-final.json`. The warm-hit result does not measure concurrent first requests from distinct users; the test suite verifies those requests no longer block one another.

This shows that cache hits avoid database work in this process. These numbers are not end-to-end API latency or SQL Server production throughput: the benchmark excludes HTTP, TLS, network, JWT validation, cache expiry and invalidation during a run, and simultaneous distinct users. Test those separately in staging before setting capacity targets.
