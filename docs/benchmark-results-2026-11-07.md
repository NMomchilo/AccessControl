# Decision API benchmark, 2026-11-07

Three Release runs before and three after adding the bounded service cache, on Windows 10.0.22631, .NET 10.0.10, 20 logical processors. Each scenario used 5,000 successful Permit requests after warmup. Values are medians across three runs.

| Store | Concurrent requests | Before requests/s | After requests/s | Before p95 | After p95 |
| --- | ---: | ---: | ---: | ---: | ---: |
| In-memory fake | 1 | 4,458 | 4,345 | 0.25 ms | 0.27 ms |
| In-memory fake | 16 | 52,099 | 52,132 | 0.51 ms | 0.37 ms |
| In-memory fake | 64 | 57,404 | 57,177 | 3.46 ms | 3.45 ms |
| SQLite, real EF store | 1 | 1,222 | 3,519 | 0.94 ms | 0.32 ms |
| SQLite, real EF store | 16 | 3,083 | 37,354 | 8.14 ms | 1.76 ms |
| SQLite, real EF store | 64 | 2,550 | 40,465 | 29.48 ms | 3.23 ms |

In this high-hit-rate workload, cached SQLite throughput at concurrency 16 was about 12 times higher than before caching, with p95 latency falling from 8.14 ms to 1.76 ms. The workload repeats one tenant, endpoint, and role set, so most requests hit the cache. A broad production working set will have a lower hit rate and smaller improvement.

These are local comparison results, **not SQL Server capacity figures**. The harness uses an in-process HTTP server, a shared SQLite memory database, and no real network or TLS. Logging providers are disabled. The NuGet middleware and consuming service are not part of the timings. Run the [benchmark harness](../benchmarks/README.md) against staging SQL Server with representative policy and role diversity to establish deployment latency and throughput.

Raw runs are under `artifacts/benchmarks/`: `run-1.json` through `run-3.json` before caching, and `cache-run-final-1.json` through `cache-run-final-3.json` after caching. These generated files are ignored by source control.
