# Forward roles without automatic user provisioning

Historical plan: its request-time UO creation is superseded. Current resolution reads existing UOs, users, memberships, and roles only.

**Goal:** Existing users are resolved without creating users or memberships, and Access Control evaluates protected requests using role IDs supplied by User Management middleware.

**Architecture:** Keep the service-key authenticated User Management resolve call, but make it return not found for unprovisioned users. The client middleware maps that to 403. Add role IDs to the decision request from the dedicated User Management identity; remove Access Control's decision-time User Management lookup, lookup cache, revision poller, and endpoint. Keep admin-duty lookup and UO administration intact.

## Steps

- [x] Add failing tests for no user or membership creation on resolve, and forwarded role IDs controlling protected decisions.
- [x] Make resolution read only for users and memberships; return 404 for unknown users and 403 to the consuming caller.
- [x] Restore role IDs to the decision contract and client; validate role count and GUIDs at the service.
- [x] Remove obsolete decision role lookup components and configuration, preserving admin lookup.
- [x] Update OpenAPI, README, benchmarks, and tests; run build, tests, coverage, and package validation.

No commits and no existing database operations.
