# Access Control API contract

User provisioning and tenant-scoped role IDs are documented in [User Management API and client](user-management-api.md). The consuming service runs User Management after JWT authentication and before Access Control, then configures `AccessControl:RoleIdClaimType` as `access_control_role_id` and `AccessControl:RoleIdentityAuthenticationType` as `UserManagement`.

Use HTTPS. Every request except `GET /health` requires `X-Access-Control-Key`. The decision key can call only `POST /v1/decisions`; the separate admin key is for policies and role entitlements.

## Decision

`POST /v1/decisions`:

```json
{
  "subjectId": "alice",
  "roleIds": ["c4227c8a-16ef-eb3c-aedb-8197bf7f39c2"],
  "module": "customers",
  "httpMethod": "GET",
  "routeTemplate": "/customers/{id}",
  "tenantId": "default"
}
```

The client middleware uses the authenticated user's `oid` claim as `subjectId`; `subjectId` is `null` and `roleIds` is empty for an anonymous caller. It forwards only GUID role IDs from the configured User Management identity. An authenticated caller without `oid` receives HTTP 401 before the decision request. The response is always a decision when the request is valid and the store is available:

```json
{ "permit": true, "reason": "Permit", "authenticationRequired": false }
```

The evaluator looks up the single policy keyed by tenant, module, HTTP method, and normalized route template. A missing policy denies. A public policy permits an anonymous caller. A protected policy requires a subject and at least one matching entitlement granted to any supplied role ID in that tenant. The required entitlement list uses **OR** semantics. A protected anonymous caller gets `authenticationRequired: true`; the middleware returns `401`. Other Deny decisions return `403`. Decision transport or store failures return `503` from the client middleware.

## Endpoint policy administration

`PUT /v1/policies` with the admin key creates or replaces a policy and returns `204`:

```json
{
  "tenantId": "default",
  "module": "customers",
  "httpMethod": "GET",
  "routeTemplate": "/customers/{id}",
  "isPublic": false,
  "requiredEntitlements": ["customers.view", "customers.admin"]
}
```

A public policy uses `"isPublic": true` and `"requiredEntitlements": []`. A protected policy requires at least one entitlement. Method is normalized to uppercase; route and other key parts to lowercase. Route parameters remain templates, never concrete customer IDs.

`GET /v1/policies?tenantId=default&module=customers&httpMethod=GET&routeTemplate=%2Fcustomers%2F%7Bid%7D` returns the policy (`200`) or `404`.

`DELETE /v1/policies` accepts only the key fields (`tenantId`, `module`, `httpMethod`, `routeTemplate`) in a JSON body and returns `204`.

## Role grants

`PUT /v1/roles/entitlements` replaces all grants for one tenant and role and returns `204`:

```json
{
  "tenantId": "default",
  "roleId": "c4227c8a-16ef-eb3c-aedb-8197bf7f39c2",
  "entitlements": ["customers.view"]
}
```

`GET /v1/roles/entitlements?tenantId=default&roleId=<role-guid>` returns the current grants. Create or find the role ID in User Management first. The `PUT` request replaces the entire entitlement set, so include all grants you want to retain.

The decision service trusts the consuming service's authenticated subject and role-ID claims; it does not query User Management. Keep decision credentials secret, use HTTPS, and bind callers to allowed modules and tenants before accepting mutually untrusted services. The tenant/role-ID migration maps legacy strings to matching IDs in both databases and is one-way; back up databases before deploying it.
