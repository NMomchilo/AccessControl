# Keyless local decision benchmark, 2026-11-27

Run: `dotnet run --no-build --project benchmarks/AccessControl.Benchmarks -c Release -- --requests 2000 --concurrency 1,16 --output artifacts/benchmarks/keyless-local-2026-11-27.json`.

| Store | Concurrency | Requests/s | p50 ms | p95 ms | p99 ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| In-memory test store | 1 | 1,843.7 | 0.50 | 0.65 | 0.92 |
| In-memory test store | 16 | 19,733.6 | 0.51 | 1.24 | 4.23 |
| SQLite with test cache | 1 | 1,055.3 | 0.78 | 1.56 | 2.49 |
| SQLite with test cache | 16 | 8,857.1 | 1.20 | 5.97 | 9.01 |

The harness sent authenticated decisions without API keys. It used an in-process ASP.NET Core server, a test bearer handler, and either a fake store or SQLite. Results confirm the updated benchmark runs; they are not SQL Server, Redis network, TLS, or production capacity measurements. Earlier benchmark reports document older keyed builds.
