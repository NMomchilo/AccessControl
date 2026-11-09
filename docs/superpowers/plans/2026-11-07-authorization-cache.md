# Bounded authorization cache plan

Revocation budget: a changed policy or role grant may affect another instance for at most five seconds after its database transaction commits. Local instance invalidation is immediate. Instances poll a database revision once per second; the five-second cache expiration is the fallback if polling fails.

- [x] Add relational revision row and an additive SQL Server migration. Increment revision in the same transaction as every policy or grant mutation.
- [x] Add one size-limited, five-second in-memory cache for endpoint policies and normalized role sets. Prevent concurrent cache misses from causing a query surge.
- [x] Invalidate local entries after successful administration writes and invalidate other instances when their poller sees a newer database revision.
- [x] Test cache hits, bounds, expiration, write invalidation, cross-instance revision observation, and transaction revision updates.
- [x] Re-run tests and benchmarks, document revocation behavior and measured effect, and leave changes uncommitted.
