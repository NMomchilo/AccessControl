# Decision role lookup and identity implementation plan

Historical plan: ordinary Access Control decisions now use role IDs forwarded by the consuming API; they do not call User Management for roles.

**Goal:** Establish administrator management and consistent entity update metadata. The decision role lookup and manual seed tool proposed here were later removed.

**Current architecture:** Access Control receives role IDs forwarded by the consuming API and uses those role IDs for grants. Administrator endpoints validate Entra bearer tokens and read current duties. Database writes require a validated actor ID. Existing databases are untouched; clean initial migrations target fresh development databases.

**Constraints:** Do not commit. Do not accept arbitrary claims from request headers. Keep anonymous global public policies fast. Keep cache revocation bounded and fail closed. Preserve scoped administrator behavior.

## Tasks

- [ ] Update decision contract, client, service, User Management authorization-context endpoint, and service-to-service authentication.
- [ ] Add bounded identity cache with revision invalidation and fail-closed tests.
- [ ] Remove portal, audit entities/transactions, startup bootstrap, and historical bootstrap marker. The later manual seed tool was removed.
- [ ] Add general entity metadata for applicable data entities, with explicit exceptions for composite join rows and singleton revision rows; stamp tracked and bulk writes.
- [ ] Configure administrator Entra authentication in both services, identity service, and scope checks.
- [ ] Regenerate first-version migrations without applying to existing databases.
- [ ] Update OpenAPI, README, tests, and benchmarks; run solution tests, coverage, package builds, migration model checks.
