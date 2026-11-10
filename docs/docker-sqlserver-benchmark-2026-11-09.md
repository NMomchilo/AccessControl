# Docker SQL Server verification and benchmarks — 2026-11-09

## Environment and checks

The repository's `local-sqlserver` Docker container ran SQL Server 2022 on localhost port 1433. Benchmarks ran on Windows 10.0.22631 with .NET 10.0.10 and 20 visible processors. User Management and Access Control each used a dedicated Docker SQL Server database. EF Core applied both User Management migrations and all three Access Control migrations before the tests.

Live API checks against SQL Server passed:

- User Management created an unknown `oid` with `reviewer`, changed its roles, deactivated it, and advanced the revision after each administrator write. A missing credential returned 401.
- Twenty concurrent first sign-ins for another `oid` all returned HTTP 200, one user ID, and `reviewer`.
- Access Control permitted a user with the required role grant, denied a missing policy, permitted an anonymous request to a public policy, and returned 401 without a key.
- Two Access Control instances shared the database. After a grant was removed through one instance, a warmed decision on the other changed from Permit to Deny after its revision poll. Restoring the grant changed it back to Permit.
- A backend flow passed the `reviewer` role returned by User Management to Access Control, which permitted the protected endpoint. After replacing that user's roles with an empty array, the same decision was denied.

## Measurements

Each figure is the median of three Release runs. Throughput is requests per second; p95 is client-observed latency. Application logging was set to Warning during live measurements. The full JSON outputs are in `artifacts/benchmarks/` with the prefixes shown below.

| Scenario | Requests/run | Concurrency | Median throughput | Median p95 | Source calls/run |
|---|---:|---:|---:|---:|---:|
| User Management direct EF Core SQL Server lookup | 5,000 | 16 | 6,771/s | 4.36 ms | 5,000 DB lookups |
| User Management direct bounded cache, warm | 5,000 | 16 | 2,802,062/s | below timer resolution | 0 DB lookups |
| User Management live HTTP service backed by SQL Server | 5,000 | 16 | 6,042/s | 3.14 ms | 5,000 HTTP calls |
| User Management client cache in front of live HTTP service, warm | 5,000 | 16 | 2,979,560/s | below timer resolution | 0 HTTP calls |

User Management outputs: `user-management-docker-sqlserver-run1.json` through `run3.json`, and `user-management-docker-http-run1.json` through `run3.json`. The direct SQL and live HTTP scenarios use separate processes and warm-up states; their throughput is not an A/B comparison of network overhead.

| Access Control HTTPS decision endpoint | Requests/run | Concurrency | Median throughput | Median p95 |
|---|---:|---:|---:|---:|
| Normal bounded cache, repeated policy and role | 5,000 | 1 | 3,469/s | 0.40 ms |
| Normal bounded cache, repeated policy and role | 5,000 | 16 | 39,874/s | 0.54 ms |
| Normal bounded cache, repeated policy and role | 5,000 | 64 | 38,443/s | 3.16 ms |
| Cache constrained to one entry, repeated policy and role | 1,000 | 1 | 588/s | 2.27 ms |
| Cache constrained to one entry, repeated policy and role | 1,000 | 16 | 729/s | 29.05 ms |
| Cache constrained to one entry, repeated policy and role | 1,000 | 64 | 754/s | 104.71 ms |

Access Control outputs: `access-control-docker-sqlserver-run1.json` through `run3.json`, and `access-control-docker-cache-constrained-run1.json` through `run3.json`. A one-entry limit cannot retain both the policy and grant entries for this workload, so at least one lookup returns to SQL Server per decision. The same-key miss lock serializes those loads. This is a deliberate stress condition, not the default cache configuration.

## Interpretation and limits

The normal Access Control workload repeats one policy and one role, so it measures a high cache hit rate even though SQL Server is the backing store. The constrained run exposes the cost and queueing when that cache cannot retain both entries. The User Management warm-cache figures measure local memory lookups, not HTTP service capacity. Its direct SQL and HTTP runs use one existing user, so they do not measure concurrent new-user provisioning throughput.

These are localhost measurements on one workstation and one Docker SQL Server container. They exclude a real Entra JWT, a consuming service's middleware chain, remote network latency, multi-host deployment, and representative mixes of users, policies, misses, writes, and outages. Run those scenarios in staging before setting production capacity targets. Keep the five-second revocation bound and monitor cache hit rate, decision latency, SQL load, and revision poll failures.

During concurrent first sign-ins, EF Core logged insert failures consistent with unique-key races before the service reloaded the winning user. All 20 requests succeeded, but that recovery path can produce noisy error logs during sign-in bursts. Consider an insert-if-absent SQL pattern or a single-flight provisioning path if the noise or contention becomes material.
