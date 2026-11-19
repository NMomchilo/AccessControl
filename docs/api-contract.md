# Access Control API contract

User resolution and UO-scoped role IDs are documented in the separate User Management repository's `docs/user-management-api.md`. The consuming service validates its Entra JWT and trusted gateway UO assertion before Access Control. User Management middleware adds role IDs to `HttpContext.User`; Access Control middleware forwards them for protected decisions.

Use HTTPS. Every request except `GET /health` and `GET /ready` requires `X-Access-Control-Key`. A configured per-client decision key can call only `POST /v1/decisions` for its allowed modules, tenants, and UOs. Non-anonymous decisions also require an Entra bearer token whose validated `oid` matches `subjectId`. Administrator operations require the separate admin key and a validated Entra bearer token. Access Control asks User Management to verify the actor's current administrative duties for each request; the key and token alone do not grant a tenant or UO scope. A missing or invalid key or actor token returns `401`, an actor without the required scope returns `403`, and an unavailable User Management scope lookup returns `503`. `GET /ready` returns `200` when the database connection works and `503` when it does not; it does not check pending migrations.

An active SuperAdmin can manage any UO in a tenant where User Management assigned that duty, tenant-wide searches, and global public policies. An active Admin can manage only assigned UOs. A UO-scoped administrator can use the UO-specific policy, entitlement, and role-grant operations for an assigned UO. Tenant-wide entitlement lists and policy searches without a UO filter require SuperAdmin. An explicit all-zero UO ID selects global public policies and also requires SuperAdmin. Access Control checks the selected tenant and UO before reading or changing administrator data.

## Decision

`POST /v1/decisions`:

```json
{
  "subjectId": "9155ff4d-2708-4770-b87e-e238645ec5df",
  "module": "customers",
  "httpMethod": "GET",
  "routeTemplate": "/customers/{id}",
  "tenantId": "default",
  "unitOrganizationId": "33333333-3333-3333-3333-333333333333",
  "roleIds": ["c4227c8a-16ef-eb3c-aedb-8197bf7f39c2"]
}
```

The client middleware uses the authenticated user's `oid` as `subjectId`, forwards the original bearer token, and includes role IDs from the dedicated User Management identity. `subjectId` is null, `unitOrganizationId` is all-zero, and `roleIds` is empty for an anonymous caller. Access Control validates a bearer token against `DecisionEntra` for non-null subjects and checks the decision client scope. The response is:

```json
{ "permit": true, "reason": "Permit", "authenticationRequired": false }
```

The evaluator looks up the policy keyed by tenant, UO ID, module, HTTP method, and normalized route template. If no UO-specific policy exists, it may use a global public policy with the all-zero UO ID; a specific policy takes precedence. A missing policy denies. A protected policy requires a subject and at least one active entitlement ID granted to a forwarded role ID in that tenant and UO. The required entitlement IDs use **OR** semantics. An anonymous caller without a global public policy gets `authenticationRequired: true` and `401`; an authenticated caller with no policy gets `403`. Other Deny decisions return `403`. Decision transport or store failures return `503` from the client middleware. User Management resolves the user before this decision call; its cached role claims may remain effective for up to five seconds after a role change.

## Entitlement catalog

Create an entitlement before referencing it in a grant or policy. `POST /v1/entitlements` with the admin key returns `201` and an object containing its generated `id`:

```json
{ "tenantId": "default", "unitOrganizationId": "33333333-3333-3333-3333-333333333333", "code": "customers.view", "displayName": "View customers" }
```

Codes are normalized to lowercase and unique within `(tenantId, unitOrganizationId)`. Duplicate codes return `409`. `PUT /v1/entitlements/{id}/active?tenantId=default&unitOrganizationId=<uo-guid>` with `{ "isActive": false }` disables an entitlement; `true` re-enables it. Unknown IDs return `404`. Disabled entitlements remain in the catalog and existing grants, but they cannot satisfy a decision or be newly assigned to roles or protected policies. Each status change increments the shared revision and invalidates the local decision cache; other instances poll the revision and have a five-second cache lifetime as fallback.

To change only the human-readable name, call `PUT /v1/entitlements/{id}/display-name?tenantId=default&unitOrganizationId=<uo-guid>` with `{ "displayName": "View customer records" }` and the admin key. It returns the updated entitlement (`200`), or `404` if the ID is outside that scope. A missing, blank, or over-200-character name returns `400`. This operation preserves the entitlement ID, code, active status, role grants, and policies. It does not advance the authorization revision or clear decision caches because the name is not used to make decisions.

## Endpoint policy administration

`PUT /v1/policies` with the admin key creates or replaces a policy and returns `204`:

```json
{
  "tenantId": "default",
  "unitOrganizationId": "33333333-3333-3333-3333-333333333333",
  "module": "customers",
  "httpMethod": "GET",
  "routeTemplate": "/customers/{id}",
  "isPublic": false,
  "requiredEntitlementIds": ["11111111-aaaa-aaaa-aaaa-aaaaaaaaaaaa"]
}
```

A public policy uses `"isPublic": true` and `"requiredEntitlementIds": []`; an all-zero UO ID makes it global. A protected policy requires a nonzero UO ID and 1–32 active entitlement IDs from the same tenant and UO. Method is normalized to uppercase; route and other key parts to lowercase. Route parameters remain templates, never concrete customer IDs.

`GET /v1/policies?tenantId=default&unitOrganizationId=<uo-guid>&module=customers&httpMethod=GET&routeTemplate=%2Fcustomers%2F%7Bid%7D` returns the policy (`200`) or `404`.

`DELETE /v1/policies` accepts only the key fields (`tenantId`, `unitOrganizationId`, `module`, `httpMethod`, `routeTemplate`) in a JSON body and returns `204`, including if no active policy matches. Deletion sets `IsActive=false`: the policy row remains in the database, but the decision evaluator, `GET`, and search exclude it. The authorization revision advances so other instances invalidate cached decisions. A later `PUT` with the same key restores and replaces the policy.

## Role grants

`PUT /v1/roles/entitlements` replaces all grants for one tenant, UO, and role and returns `204`:

```json
{
  "tenantId": "default",
  "unitOrganizationId": "33333333-3333-3333-3333-333333333333",
  "roleId": "c4227c8a-16ef-eb3c-aedb-8197bf7f39c2",
  "entitlementIds": ["11111111-aaaa-aaaa-aaaa-aaaaaaaaaaaa"]
}
```

`GET /v1/roles/entitlements?tenantId=default&unitOrganizationId=<uo-guid>&roleId=<role-guid>` returns the assigned entitlement IDs, including disabled ones. `unitOrganizationId` is required in every Access Control request; use the all-zero GUID explicitly only for global public policies. Create or find the role ID in that UO in User Management first. The `PUT` request replaces the entire entitlement set, so include all grants you want to retain. It accepts up to 128 active entitlement IDs from the same tenant and UO; a missing, disabled, or foreign-scope ID returns `400` without changing existing grants.

## Entitlement listings

The administrator can list registered entitlements, including disabled and unreferenced entries:

- `GET /v1/entitlements?tenantId=default&page=1&pageSize=100` lists catalog entries across the tenant.
- `GET /v1/unit-organizations/{id}/entitlements?tenantId=default&page=1&pageSize=100` lists entries in one UO. The all-zero GUID is invalid for this filtered route.
- `GET /v1/entitlements/{id}?tenantId=default&unitOrganizationId=<uo-guid>` returns one entitlement in the selected scope, or `404`.

Both return `{ "items": [{ "id": "11111111-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "tenantId": "default", "unitOrganizationId": "33333333-3333-3333-3333-333333333333", "code": "customers.view", "displayName": "View customers", "isActive": true }], "totalCount": 1, "page": 1, "pageSize": 100 }`. Rows are ordered by UO ID, code, and entitlement ID. `page` starts at 1, `pageSize` must be 1–100, and invalid or overflowing pagination returns `400`. The endpoint requires the administrator key.

`GET /v1/policies` still reads one active policy by its complete tenant, UO ID, module, HTTP method, and route-template key. For frontend tables, `GET /v1/policies/search?tenantId=default&page=1&pageSize=100` returns a `PolicyPageResponse` with `items`, `totalCount`, `page`, and `pageSize`. Optional `unitOrganizationId`, `module`, and `httpMethod` filters narrow the results; an explicit all-zero UO ID selects global public policies. Each item includes the required entitlement IDs. Search is read-only, excludes inactive policies, and queries the database directly rather than decision caches.

The decision service validates the forwarded subject token and uses the role IDs asserted by the consuming API. Keep client credentials secret and scope each one to allowed modules, tenants, and UOs. `AllowAnyUnitOrganization` is an explicit choice for a trusted client that handles dynamic UOs. The fresh initial migration targets an empty first-version database and does not import legacy strings.
