# Access Control API contract

User provisioning and UO-scoped role IDs are documented in [User Management API and client](user-management-api.md). The consuming service runs User Management after JWT and trusted gateway authentication and before Access Control, then configures `AccessControl:RoleIdClaimType` as `access_control_role_id` and `AccessControl:RoleIdentityAuthenticationType` as `UserManagement`.

Use HTTPS. Every request except `GET /health` requires `X-Access-Control-Key`. The decision key can call only `POST /v1/decisions`; the separate admin key is for policies, role grants, and entitlement administration.

## Decision

`POST /v1/decisions`:

```json
{
  "subjectId": "alice",
  "roleIds": ["c4227c8a-16ef-eb3c-aedb-8197bf7f39c2"],
  "module": "customers",
  "httpMethod": "GET",
  "routeTemplate": "/customers/{id}",
  "tenantId": "default",
  "unitOrganizationId": "33333333-3333-3333-3333-333333333333"
}
```

The client middleware uses the authenticated user's `oid` claim as `subjectId`; `subjectId` is `null`, `roleIds` is empty, and `unitOrganizationId` is all-zero for an anonymous caller. It forwards only GUID role IDs and the UO ID from the configured User Management identity. An authenticated caller without `oid` receives HTTP 401 before the decision request. The response is always a decision when the request is valid and the store is available:

```json
{ "permit": true, "reason": "Permit", "authenticationRequired": false }
```

The evaluator looks up the policy keyed by tenant, UO ID, module, HTTP method, and normalized route template. If no UO-specific policy exists, it may use a global public policy with the all-zero UO ID; a specific policy takes precedence. A missing policy denies. A protected policy requires a subject and at least one active entitlement ID granted to a supplied role ID in that tenant and UO. The required entitlement IDs use **OR** semantics. An anonymous caller without a global public policy gets `authenticationRequired: true` and `401`; an authenticated caller with no policy gets `403`. Other Deny decisions return `403`. Decision transport or store failures return `503` from the client middleware.

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

`DELETE /v1/policies` accepts only the key fields (`tenantId`, `unitOrganizationId`, `module`, `httpMethod`, `routeTemplate`) in a JSON body and returns `204`.

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

Both return `{ "items": [{ "id": "11111111-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "tenantId": "default", "unitOrganizationId": "33333333-3333-3333-3333-333333333333", "code": "customers.view", "displayName": "View customers", "isActive": true }], "totalCount": 1, "page": 1, "pageSize": 100 }`. Rows are ordered by UO ID, code, and entitlement ID. `page` starts at 1, `pageSize` must be 1–100, and invalid or overflowing pagination returns `400`. The endpoint requires the administrator key.

The decision service trusts the consuming service's authenticated subject and role-ID claims; it does not query User Management. Keep decision credentials secret, use HTTPS, and bind callers to allowed modules and tenants before accepting mutually untrusted services. The initial migration creates the catalog and ID-based grant and policy relationships in an empty database; it does not import legacy strings.
