# Access Control API contract

User resolution, UO-scoped role IDs, and administrative duty flags are documented in the separate User Management repository's `docs/user-management-api.md`. The consuming service validates its Entra JWT and trusted gateway UO assertion before Access Control. User Management middleware adds role IDs and duties to `HttpContext.User`; Access Control middleware forwards them for protected decisions.

Use HTTPS. `POST /v1/decisions` and `POST /v1/registrations/catalog` no longer require API keys. A non-anonymous decision requires an Entra bearer token whose validated `oid` matches `subjectId`. Administrator operations require a validated Entra bearer token. Access Control asks User Management to verify the actor's current duties for each administrator request by forwarding that bearer token. A missing or invalid required token returns `401`, insufficient duty returns `403`, and an unavailable User Management lookup returns `503`. Decision and registration routes must be restricted to trusted service networks because the service does not independently authenticate the calling application. `GET /ready` checks database connectivity; `GET /health` is liveness.

An active SuperAdmin can manage any UO in a tenant where User Management assigned that duty, tenant-wide searches, and global public policies. An active Admin can manage only assigned UOs. A UO-scoped administrator can use the UO-specific policy, entitlement, and role-grant operations for an assigned UO. Tenant-wide entitlement lists and policy searches without a UO filter require SuperAdmin. An explicit all-zero UO ID selects global public policies and also requires SuperAdmin. Access Control checks the selected tenant and UO before reading or changing administrator data.

`POST /v1/registrations/catalog` accepts the tenant, unit organization ID, and up to 1000 `{httpMethod, routeTemplate}` entries. It is keyless and uses an `unattributed-registration` write actor. Missing policies start protected with zero entitlements. Replays do not overwrite existing policy settings. A `409` is possible if a concurrent registration remains in conflict after retry. An administrator can edit a registered policy with `PUT /v1/policies`, including setting `isPublic: true` with no required entitlements. A UO-specific policy, even one with zero entitlements, takes precedence over a global public policy for authenticated callers in that UO.

## Decision

`POST /v1/decisions`:

```json
{
  "subjectId": "9155ff4d-2708-4770-b87e-e238645ec5df",
  "httpMethod": "GET",
  "routeTemplate": "/customers/{id}",
  "tenantId": "default",
  "unitOrganizationId": "33333333-3333-3333-3333-333333333333",
  "roleIds": ["c4227c8a-16ef-eb3c-aedb-8197bf7f39c2"],
  "isSuperAdmin": false,
  "isUnitAdmin": false
}
```

The client middleware uses the authenticated user's `oid` as `subjectId`, forwards the original bearer token, and includes role IDs and duties from the dedicated User Management identity. `subjectId` is null, `unitOrganizationId` is all-zero, `roleIds` is empty, and both duty flags are false for an anonymous caller. Access Control validates a bearer token against `DecisionEntra` for non-null subjects . A missing policy denies; a public policy permits. For a protected policy, a true `isSuperAdmin` or `isUnitAdmin` flag permits before entitlement comparison. `isUnitAdmin` is asserted for the selected UO only. The service trusts the network caller to assert these flags; it does not independently check them with User Management on ordinary decisions. The response is:

```json
{ "permit": true, "reason": "Permit", "authenticationRequired": false }
```

The evaluator looks up the policy keyed by tenant, UO ID, HTTP method, and normalized route template. If no UO-specific policy exists, it may use a global public policy with the all-zero UO ID; a specific policy takes precedence. A missing policy denies. A protected policy requires a subject and nonzero UO ID. An asserted `isSuperAdmin` or `isUnitAdmin` duty then permits; otherwise, at least one active entitlement ID must be granted to a forwarded role ID in that tenant and UO. The required entitlement IDs use **OR** semantics. An anonymous caller without a global public policy gets `authenticationRequired: true` and `401`; an authenticated caller with no policy gets `403`. Other Deny decisions return `403`. Decision transport or store failures return `503` from the client middleware. User Management resolves the user before this decision call; its cached role claims and duties may remain effective for up to five seconds after a change.

## Entitlement catalog

Create an entitlement before referencing it in a grant or policy. `POST /v1/entitlements` with an Entra bearer token returns `201` and an object containing its generated `id`:

```json
{ "tenantId": "default", "unitOrganizationId": "33333333-3333-3333-3333-333333333333", "displayName": "View customers", "code": "customers.view" }
```

Entitlements are identified by generated GUIDs within their tenant and UO. The required code is normalized to lowercase, uses `[a-z][a-z0-9._-]*`, and is unique within tenant and UO even when an entitlement is inactive; duplicate codes return `409`. Codes cannot be changed after creation. Display names may repeat. Authorization still uses entitlement GUIDs, not codes. `PUT /v1/entitlements/{id}/active?tenantId=default&unitOrganizationId=<uo-guid>` with `{ "isActive": false }` disables an entitlement; `true` re-enables it. Unknown IDs return `404`. Disabled entitlements remain in the catalog and existing grants, but they cannot satisfy a decision or be newly assigned to roles or protected policies. Each status change increments the shared revision and invalidates the local decision cache; other instances poll the revision and have a five-second cache lifetime as fallback.

To change only the human-readable name, call `PUT /v1/entitlements/{id}/display-name?tenantId=default&unitOrganizationId=<uo-guid>` with `{ "displayName": "View customer records" }` and an Entra bearer token. It returns the updated entitlement (`200`), or `404` if the ID is outside that scope. A missing, blank, or over-200-character name returns `400`. This operation preserves the entitlement ID, active status, role grants, and policies. It does not advance the authorization revision or clear decision caches because the name is not used to make decisions.

## Endpoint policy administration

`PUT /v1/policies` with an Entra bearer token creates or replaces a policy and returns `204`:

```json
{
  "tenantId": "default",
  "unitOrganizationId": "33333333-3333-3333-3333-333333333333",
  "httpMethod": "GET",
  "routeTemplate": "/customers/{id}",
  "isPublic": false,
  "requiredEntitlementIds": ["11111111-aaaa-aaaa-aaaa-aaaaaaaaaaaa"]
}
```

A public policy uses `"isPublic": true` and `"requiredEntitlementIds": []`; an all-zero UO ID makes it global. A protected policy requires a nonzero UO ID and 1–32 active entitlement IDs from the same tenant and UO. Method is normalized to uppercase; route and other key parts to lowercase. Route parameters remain templates, never concrete customer IDs.

`GET /v1/policies?tenantId=default&unitOrganizationId=<uo-guid>&httpMethod=GET&routeTemplate=%2Fcustomers%2F%7Bid%7D` returns the policy (`200`) or `404`.

`DELETE /v1/policies` accepts only the key fields (`tenantId`, `unitOrganizationId`, `httpMethod`, `routeTemplate`) in a JSON body and returns `204`, including if no active policy matches. Deletion sets `IsActive=false`: the policy row remains in the database, but the decision evaluator, `GET`, and search exclude it. The authorization revision advances so other instances invalidate cached decisions. A later `PUT` with the same key restores and replaces the policy.

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

Both return `{ "items": [{ "id": "11111111-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "tenantId": "default", "unitOrganizationId": "33333333-3333-3333-3333-333333333333", "displayName": "View customers", "code": "customers.view", "isActive": true }], "totalCount": 1, "page": 1, "pageSize": 100 }`. Rows are ordered by UO ID, code, and entitlement ID. `page` starts at 1, `pageSize` must be 1–100, and invalid or overflowing pagination returns `400`. The endpoint requires a validated Entra token and current administrator duty.

`GET /v1/policies` still reads one active policy by its complete tenant, UO ID, HTTP method, and route-template key. For frontend tables, `GET /v1/policies/search?tenantId=default&page=1&pageSize=100` returns a `PolicyPageResponse` with `items`, `totalCount`, `page`, and `pageSize`. Optional `unitOrganizationId` and `httpMethod` filters narrow the results; an explicit all-zero UO ID selects global public policies. Each item includes the required entitlement IDs. Search is read-only, excludes inactive policies, and queries the database directly rather than decision caches.

The decision service validates the forwarded subject token when present and uses the role IDs asserted by the consuming API. Network access to decision and registration routes must be restricted; without API keys, any caller reaching those routes can assert tenant, UO, roles, and duties. The fresh initial migration targets an empty first-version database and does not import legacy strings.
