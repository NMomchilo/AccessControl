# Architecture

## User Management before authorization

`UserManagement.Service` owns a separate SQL Server database with tenant-scoped users keyed by `(TenantId, oid)`, a role catalog with GUID IDs, and user-to-role assignments. A first delegated user request creates the user with that tenant's `reviewer` role. The reusable `UserManagement.Client` NuGet runs after JWT authentication and adds role-ID and role-code claims to `HttpContext.User` for that request. `AccessControl.Client` reads only role IDs from the dedicated User Management identity. User Management uses per-client and administrator credentials, a bounded five-second client cache keyed by tenant and oid, and a database revision polled after administrative changes. [API and client details](user-management-api.md).

## Access Control decision flow

```mermaid
flowchart LR
    Caller[Caller] --> Route[ASP.NET Core matches route and HTTP method]
    Route --> Auth[Consuming service validates Entra JWT]
    Auth --> UserClient[UserManagement.Client middleware and cache]
    UserClient -->|oid and profile on cache miss| UserApi[UserManagement.Service]
    UserApi --> Users[(Users, Roles, and UserRoles)]
    Users --> UserApi
    UserApi -->|active status and role IDs| UserClient
    UserClient -->|application role-ID claims| NuGet[AccessControl.Client middleware]
    UserClient -->|401, 403, or 503| Stop[Controller does not run]
    NuGet -->|tenant, module, method, route template, subject, role IDs| API[AccessControl.Service]
    API --> Policy[(EndpointPolicies + required entitlements)]
    API --> Grants[(RoleEntitlements)]
    Policy --> Evaluate[Evaluate policy]
    Grants --> Evaluate
    Evaluate -->|Permit, Deny, or authentication required| NuGet
    NuGet -->|Permit| Controller[Business controller runs]
    NuGet -->|401, 403, or 503| Stop[Controller does not run]
```

The packages sit after host authentication and route matching. Access Control reads the **route template** (`/customers/{id}`), not a concrete URL (`/customers/123`). There are no authorization attributes on controllers. The host owns token validation. The consuming API forwards subject and role IDs from User Management together with tenant, module, method, and route template. The services do not call one another. Access Control trusts the consuming API's assertions and should use scoped workload credentials before accepting mutually untrusted callers.

`EndpointPolicies` has one row per `(TenantId, Module, HttpMethod, RouteTemplate)` with `IsPublic`. `EndpointPolicyEntitlements` holds its allowed entitlement alternatives. `RoleEntitlements` grants entitlements to `(TenantId, RoleId)` pairs; it has no user columns. A policy permits when at least one required entitlement is in the union of the caller's role-ID grants. A public policy permits without authentication. No policy means Deny.

The initial Access Control migration creates endpoint policy tables and a shared `AuthorizationRevisions` row. The `AccessPolicies` table remains in the current model for the older resource/action API, but its rows have no effect on endpoint decisions. Provision endpoint policies before adopting the middleware.

Each service has a single initial migration for a fresh database. User Management creates tenant-scoped users and roles with generated role IDs; Access Control stores role grants by those IDs. No legacy string-role data is copied. Role codes are unique per tenant and display names may repeat. User Management enforces that assigned role IDs belong to the user's tenant; Access Control has no cross-database foreign key to its role catalog.

Each service instance holds a bounded, five-second cache of policy rows and role grant sets. Concurrent misses for the same key share one database load. An administration write updates the revision in the same transaction as the policy or grant change, then clears the local cache. Other instances poll the revision every second and clear their caches when it changes. Entry expiration is the five-second fallback if a poll fails or an out-of-band database write does not advance the revision. This defines the accepted revocation delay: up to five seconds for already cached inputs, except a request already in flight. The cache stores policy and grant data, not per-user Permit decisions.

If SQL Server becomes unavailable, an entry already in cache can still be used until it expires. After expiration, the database load fails and the consuming middleware returns `503`; the cache does not extend expired grants during an outage.

| Situation | Result |
| --- | --- |
| Matched public endpoint with public policy | Continue, including anonymous callers |
| Protected endpoint, caller not authenticated | `401` |
| Protected endpoint, matching role entitlement | Continue |
| Protected endpoint, no matching entitlement | `403` |
| Matched endpoint without policy | `403` |
| Decision service unavailable or invalid response | `503` |
| Route not matched by ASP.NET Core | Host handles `404` |
| Path explicitly in local `ExcludedPaths` | Continue without central decision |

The optional endpoint catalog exports discovered HTTP methods and route templates. Compare it against policies during deployment to find missing or obsolete entries. It does not approve endpoints automatically. Keep `ExcludedPaths` limited to infrastructure paths such as `/health`.

Structured logs record endpoint identity, tenant, decision, counts, API status, and trace ID. They avoid subject IDs and keys. The model is role and entitlement based; it does not evaluate record ownership or ALFA/XACML rules. For production, restrict service network access, secure decision and admin keys separately, run migrations before traffic, and monitor `403` missing-policy and `503` dependency errors.
