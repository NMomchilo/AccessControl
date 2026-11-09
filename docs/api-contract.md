# Access Control API contract

Use HTTPS. Every request except `GET /health` requires `X-Access-Control-Key`. The decision key can call only `POST /v1/decisions`; the separate admin key is for policies and role entitlements.

## Decision

`POST /v1/decisions`:

```json
{
  "subjectId": "alice",
  "roles": ["advisor"],
  "module": "customers",
  "httpMethod": "GET",
  "routeTemplate": "/customers/{id}",
  "tenantId": "tenant-a"
}
```

The client middleware uses the authenticated user's `oid` claim as `subjectId`; `subjectId` is `null` and `roles` is empty for an anonymous caller. An authenticated caller without `oid` receives HTTP 401 before the decision request. The response is always a decision when the request is valid and the store is available:

```json
{ "permit": true, "reason": "Permit", "authenticationRequired": false }
```

The evaluator looks up the single policy keyed by tenant, module, HTTP method, and normalized route template. A missing policy denies. A public policy permits an anonymous caller. A protected policy requires a subject and at least one matching entitlement granted to any supplied role. The required entitlement list uses **OR** semantics. A protected anonymous caller gets `authenticationRequired: true`; the middleware returns `401`. Other Deny decisions return `403`. Decision transport or store failures return `503` from the client middleware.

## Endpoint policy administration

`PUT /v1/policies` with the admin key creates or replaces a policy and returns `204`:

```json
{
  "tenantId": "tenant-a",
  "module": "customers",
  "httpMethod": "GET",
  "routeTemplate": "/customers/{id}",
  "isPublic": false,
  "requiredEntitlements": ["customers.view", "customers.admin"]
}
```

A public policy uses `"isPublic": true` and `"requiredEntitlements": []`. A protected policy requires at least one entitlement. Method is normalized to uppercase; route and other key parts to lowercase. Route parameters remain templates, never concrete customer IDs.

`GET /v1/policies?tenantId=tenant-a&module=customers&httpMethod=GET&routeTemplate=%2Fcustomers%2F%7Bid%7D` returns the policy (`200`) or `404`.

`DELETE /v1/policies` accepts only the key fields (`tenantId`, `module`, `httpMethod`, `routeTemplate`) in a JSON body and returns `204`.

## Role grants

`PUT /v1/roles/entitlements` replaces all grants for one tenant and role and returns `204`:

```json
{
  "tenantId": "tenant-a",
  "role": "advisor",
  "entitlements": ["customers.view"]
}
```

`GET /v1/roles/entitlements?tenantId=tenant-a&role=advisor` returns the current grants.

The decision service trusts the consuming service's authenticated subject and role claims. Keep decision credentials secret, use HTTPS, and limit the administration key to trusted operators.
