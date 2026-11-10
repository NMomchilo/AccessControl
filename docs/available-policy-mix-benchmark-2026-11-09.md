# Available route and role mix benchmark — 2026-11-09

## Source of the workload

The repository configures the Access Control service to use Docker SQL Server `AppDb`. Read-only inspection found one endpoint policy: tenant `default`, module `customers`, `GET /customers/{id}`, requiring `customers.view`. The only role grant was `advisor` → `customers.view`. There are no request-frequency logs in this repository. The [AppDb workload](../benchmarks/AccessControl.Benchmarks/workloads/appdb-available.json) therefore contains that one permitted combination.

The dedicated `AccessControlDockerBenchmark` database contains four policies and three role grants created during our earlier SQL Server tests. The [available-case workload](../benchmarks/AccessControl.Benchmarks/workloads/docker-available-uniform.json) exercises three protected permits with distinct roles, one anonymous public permit, and one protected denial. Each case has equal weight. **Those weights are synthetic; they do not represent observed user traffic.** Both workloads use a synthetic subject ID and the actual stored route templates and role names.

## Results

Three Release runs of 5,000 HTTPS decisions per concurrency level used the normal 10,000-entry, five-second cache. Each run warmed the endpoint first. Figures are medians of the three runs; p95 and p99 are client-observed latencies on localhost. SQL Server 2022 ran in the repository's Docker container.

| Workload | Concurrency | Median throughput | Median p95 | Median p99 |
|---|---:|---:|---:|---:|
| AppDb's one configured route and role | 1 | 3,548 decisions/s | 0.41 ms | 0.53 ms |
| AppDb's one configured route and role | 16 | 38,297 decisions/s | 0.68 ms | 1.39 ms |
| AppDb's one configured route and role | 64 | 38,863 decisions/s | 2.99 ms | 5.71 ms |
| Uniform five-case available mix | 1 | 3,396 decisions/s | 0.40 ms | 0.56 ms |
| Uniform five-case available mix | 16 | 35,983 decisions/s | 0.72 ms | 1.73 ms |
| Uniform five-case available mix | 64 | 40,185 decisions/s | 3.13 ms | 7.99 ms |

The JSON outputs are `artifacts/benchmarks/appdb-available-run1.json` through `run3.json` and `docker-available-uniform-run1.json` through `run3.json`. They include each case and its weight. The benchmark harness now accepts `--workload-file`; it verifies each expected Permit or Deny result and fails if the result differs.

## Conclusion and limits

The five-case mix did not show a material slowdown relative to the one-case workload with the default cache. Run-to-run variation overlaps the small difference at 16 clients. These data **do not establish real production capacity**: the available catalog is tiny, the cases are repeatedly warmed, and no real route/role frequencies or burst patterns were available. The earlier one-entry stress test remains valid as a forced cache-miss scenario, but it is not the normal configuration.

No production cache code was changed based on this measurement. To test an actual traffic mix later, provide sanitized counts by route template and role set, including anonymous and denied requests. Put those weights in a workload JSON file and rerun the same harness against a staging copy of the corresponding policies and grants. Include cold starts, policy changes, and expirations when measuring database pressure.
