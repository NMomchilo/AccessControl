# Access Control API contract

User provisioning and UO-scoped role IDs are documented in [User Management API and client](user-management-api.md). The consuming service runs User Management after JWT and trusted gateway authentication and before Access Control, then configures `AccessControl:RoleIdClaimType` as `access_control_role_id` and `AccessControl:RoleIdentityAuthenticationType` as `UserManagement`.

Use HTTPS. Every request except `GET /health` requires `X-Access-Control-Key`. The decision key can call only `POST /v1/decisions`; the separate admin key is for policies, role entitlements, and entitlement listings.

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

The evaluator looks up the policy keyed by tenant, UO ID, module, HTTP method, and normalized route template. If no UO-specific policy exists, it may use a global public policy with the all-zero UO ID; a specific policy takes precedence. A missing policy denies. A protected policy requires a subject and at least one matching entitlement granted to a supplied role ID in that tenant and UO. The required entitlement list uses **OR** semantics. An anonymous caller without a global public policy gets `authenticationRequired: true` and `401`; an authenticated caller with no policy gets `403`. Other Deny decisions return `403`. Decision transport or store failures return `503` from the client middleware.

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
  "requiredEntitlements": ["customers.view", "customers.admin"]
}
```

A public policy uses `"isPublic": true` and `"requiredEntitlements": []`; an all-zero UO ID makes it global. A protected policy requires a nonzero UO ID and at least one entitlement. Method is normalized to uppercase; route and other key parts to lowercase. Route parameters remain templates, never concrete customer IDs.

`GET /v1/policies?tenantId=default&unitOrganizationId=<uo-guid>&module=customers&httpMethod=GET&routeTemplate=%2Fcustomers%2F%7Bid%7D` returns the policy (`200`) or `404`.

`DELETE /v1/policies` accepts only the key fields (`tenantId`, `unitOrganizationId`, `module`, `httpMethod`, `routeTemplate`) in a JSON body and returns `204`.

## Role grants

`PUT /v1/roles/entitlements` replaces all grants for one tenant, UO, and role and returns `204`:

```json
{
  "tenantId": "default",
  "unitOrganizationId": "33333333-3333-3333-3333-333333333333",
  "roleId": "c4227c8a-16ef-eb3c-aedb-8197bf7f39c2",
  "entitlements": ["customers.view"]
}
```

`GET /v1/roles/entitlements?tenantId=default&unitOrganizationId=<uo-guid>&roleId=<role-guid>` returns the current grants. `unitOrganizationId` is required in every Access Control request; use the all-zero GUID explicitly only for global public policies. Create or find the role ID in that UO in User Management first. The `PUT` request replaces the entire entitlement set, so include all grants you want to retain.

## Entitlement listings

The administrator can list distinct entitlement names referenced by endpoint policy requirements or role grants:

- `GET /v1/entitlements?tenantId=default&page=1&pageSize=100` lists names across the tenant.
- `GET /v1/unit-organizations/{id}/entitlements?tenantId=default&page=1&pageSize=100` lists names in one UO. The all-zero GUID is invalid for this filtered route.

Both return `{ "items": ["customers.view"], "totalCount": 1, "page": 1, "pageSize": 100 }`. Names are sorted in SQL. These are **referenced names**, not a standalone entitlement catalog: removing the last policy requirement and grant removes a name from the list. `page` starts at 1, `pageSize` must be 1–100, and invalid or overflowing pagination returns `400`. The endpoint requires the administrator key.

The decision service trusts the consuming service's authenticated subject and role-ID claims; it does not query User Management. Keep decision credentials secret, use HTTPS, and bind callers to allowed modules and tenants before accepting mutually untrusted services. The initial migration creates role-ID grants in an empty database; it does not import legacy string roles.
