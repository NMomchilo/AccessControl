# Architecture

## User Management before authorization

`UserManagement.Service` in the separate User Management repository owns a SQL Server database with tenant-scoped users keyed by `(TenantId, oid)`, unit organizations (UOs), UO-scoped roles with GUID IDs, user-to-role assignments, and administrative duties. The consuming host validates Entra JWTs and receives organization headers from trusted ingress. Administrators create UOs, users, memberships, and roles explicitly; request-time resolution only reads existing data. `UserManagement.Client` adds UO, role-ID, SuperAdmin, and current-UO Admin claims to `HttpContext.User` for an active user. `AccessControl.Client` reads those claims from the dedicated User Management identity. User Management uses keyless service calls, a five-second Redis client cache keyed by tenant, UO, and oid, and a database revision polled after administrative changes. API and client details are in the User Management repository's `docs/user-management-api.md`.

A SuperAdmin may create a tenant user without a UO or role IDs. The user is visible in the tenant-wide user list. Before receiving SuperAdmin duty or an active UO membership, that user cannot resolve a UO for protected access. An active SuperAdmin can resolve any active UO in the same application tenant without membership or business roles. An ordinary user needs an active membership and a role with an entitlement required by the policy.

## Access Control decision flow

```mermaid
flowchart LR
    Caller[Caller] --> Route[ASP.NET Core matches route and HTTP method]
    Route --> Auth[Consuming service validates Entra JWT]
    Auth --> IngressHeaders[Read UO ID and name from trusted ingress headers]
    IngressHeaders --> UserClient[UserManagement.Client middleware and cache]
    UserClient -->|tenant, UO ID/name, oid and profile on cache miss| UserApi[UserManagement.Service]
    UserApi --> Users[(Users, UnitOrganizations, Roles, and UserRoles)]
    Users --> UserApi
    UserApi -->|active status, UO, role IDs and duties| UserClient
    UserClient -->|UO, role IDs and duty claims| NuGet[AccessControl.Client middleware]
    UserClient -->|401, 403, or 503| Stop[Controller does not run]
    NuGet -->|tenant, UO ID, method, route template, subject, role IDs and duties| API[AccessControl.Service]
    API --> Policy[(EndpointPolicies + required entitlement IDs)]
    API --> Grants[(RoleEntitlements)]
    Grants --> Catalog[(Entitlements catalog and active status)]
    Policy --> Evaluate[Evaluate policy]
    Grants --> Evaluate
    Evaluate -->|Permit, Deny, or authentication required| NuGet
    NuGet -->|Permit| Controller[Business controller runs]
    NuGet -->|401, 403, or 503| Stop[Controller does not run]
```

The packages sit after host authentication, trusted ingress headers, and route matching. Access Control reads the **route template** (`/customers/{id}`), not a concrete URL (`/customers/123`). There are no authorization attributes on controllers. The host owns token validation; ingress owns organization header injection and prevents direct bypass. The consuming API forwards subject, UO ID, role IDs, and duties from User Management together with tenant, method, and route template. Ordinary decisions do not call User Management; Access Control calls User Management to check current administrator duties for management requests. The decision service checks the matching Entra bearer `oid` for authenticated subjects but trusts the consuming API's role IDs and duty flags. A caller that reaches the decision route can assert `isSuperAdmin: true` or `isUnitAdmin: true`; these duties are not independently verified on a decision call.

Role disabling is enforced when User Management resolves the user: it stops returning that role ID. Access Control does not independently fetch role status, so a consuming API with a still-cached resolution can forward the old ID until its cache expires or observes the revision. Only trusted consuming APIs may call the decision endpoint.

`Entitlements` is a tenant- and UO-scoped catalog with stable GUID IDs, unique immutable codes, display names, and active status. User Management roles also have unique immutable codes within tenant and UO. Codes identify records for administrators; decisions, policy requirements, role grants, and user assignments still use GUID IDs. `EndpointPolicies` has one row per `(TenantId, UnitOrganizationId, HttpMethod, RouteTemplate)` with `IsPublic`; `EndpointPolicyEntitlements` references catalog IDs. `RoleEntitlements` grants catalog IDs to `(TenantId, UnitOrganizationId, RoleId)`; it has no user columns. Composite foreign keys prevent a grant or requirement from referencing an entitlement in another scope. A public policy permits. A protected policy permits an authenticated caller with an asserted SuperAdmin or current-UO Admin duty, or when at least one required ID is among the caller's active role grants in that scope. The all-zero UO ID is reserved for a global public policy, used by anonymous callers and as a fallback when no UO-specific policy exists. A UO-specific policy takes precedence. No policy means Deny.

The initial Access Control migration creates the catalog, endpoint policy tables, ID-based grant tables, and a shared `AuthorizationRevisions` row. Provision entitlements, grants, and endpoint policies before adopting the middleware.

Each service has a single initial migration for a fresh database. User Management creates the user, UO, role, and assignment tables but seeds no business role. Access Control stores policy and role grants by entitlement IDs and UO ID. No legacy string-role or entitlement data is copied. Role GUIDs identify roles within a UO, and display names may repeat. User Management enforces that assigned role IDs belong to the user's tenant and UO; Access Control has no cross-database foreign key to its role catalog. Disabling a role removes it from new user resolutions without deleting assignments. Disabling an entitlement removes its effect on decisions without deleting grants or policies. Deactivating a UO sets `IsActive=false`, retains its ID and referenced rows, and blocks resolution and role assignment. Access Control policy and grant rows for that ID require separate cleanup. Already cached resolutions may remain effective for up to five seconds.

Access Control instances share five-second Redis entries for policy rows and role grant sets. Concurrent misses within one instance share a database load. Policy, grant, and entitlement writes advance the SQL authorization revision; the writer changes its local Redis key namespace immediately. Other instances poll the revision every second and then use the new namespace. User Management clients do the same for user resolutions after role or status changes. Entry expiration is the five-second fallback if a poll fails or an out-of-band database write does not advance the revision. This defines the accepted revocation delay: up to five seconds for already cached inputs, except a request already in flight. The cache stores policy and grant data, not per-user Permit decisions.

If SQL Server becomes unavailable, an entry already in cache can still be used until it expires. After expiration, the database load fails and the consuming middleware returns `503`; the cache does not extend expired grants during an outage.
If Redis is unavailable, the cache wrapper loads from SQL Server and logs the degraded path. If both Redis and SQL Server are unavailable, it cannot permit a protected request. Production Redis needs TLS, authentication, and a memory limit; the short TTL bounds revocation delay but does not itself enforce a memory cap.

| Situation | Result |
| --- | --- |
| Matched public endpoint with public policy | Continue, including anonymous callers |
| Protected endpoint, caller not authenticated | `401` |
| Protected endpoint, matching role entitlement | Continue |
| Protected endpoint, asserted SuperAdmin or current-UO Admin duty | Continue without a business role |
| Protected endpoint, no matching entitlement | `403` |
| Matched endpoint without policy, authenticated caller | `403` |
| Matched endpoint without public policy, anonymous caller | `401` |
| Decision service unavailable or invalid response | `503` |
| Route not matched by ASP.NET Core | Host handles `404` |
| Path explicitly in local `ExcludedPaths` | Continue without central decision |

At consuming API startup, the endpoint catalog registers discovered HTTP methods and route templates in Access Control after registering the configured organization in User Management. Missing routes become protected policies with zero entitlements and deny until configured by an administrator. An administrator can edit a registered policy to make it public or require entitlements. Existing policy settings are preserved on subsequent registration, including registration by another client. A UO-specific policy takes precedence over a global public policy. The catalog is stored in the services' databases; no JSON file is written. Both registration endpoints are keyless and use an `unattributed-registration` write actor. Any caller that reaches them may supply a tenant and organization; ingress must restrict these routes to trusted services. Missing HTTP-method metadata fails startup. Keep `ExcludedPaths` limited to infrastructure paths such as `/health`.

Structured logs record endpoint identity, tenant, decision, counts, API status, and trace ID. They avoid subject IDs and keys. The model is role and entitlement based; it does not evaluate record ownership or ALFA/XACML rules. For production, restrict service network access, restrict decision and registration routes to trusted service networks, run migrations before traffic, and monitor `403` missing-policy and `503` dependency errors.
