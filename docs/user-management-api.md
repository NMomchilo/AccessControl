# User Management API and client

The consuming service validates the Entra access token and its trusted gateway assertion first. `UserManagement.Client` reads the validated `oid`, application tenant (default `default`), exact delegated `scp` scope, and UO ID/name from an authenticated `Gateway` identity. It never reads unverified organization headers. It does not edit or reissue the JWT. User Management stores tenant-scoped users, UOs, roles with stable GUID IDs, and UO-specific assignments. Access Control stores endpoint policies and entitlements granted to those IDs within the same UO.

Resolution for `(TenantId, UO ID, oid)` only reads existing data. An administrator must create the UO, user, and membership explicitly. A new user starts with no roles unless the administrator supplies IDs of existing active roles in the create request. A newly assigned membership starts without roles; an administrator can assign role IDs separately. Unknown UOs, users, or memberships return `404` and the client denies the request. Revoked memberships and deleted users remain tombstoned and cannot be restored by resolution. A deleted role is omitted from resolution and role listings. An existing user can have roles in more than one UO. The user is never matched by email. Profile changes use administrator operations. An inactive user is denied before Access Control runs. The application `TenantId` is separate from Entra `tid`; if multiple Entra issuers can enter the same application tenant, include issuer or Entra `tid` in the external identity key before enabling that scenario.

The User Management service trusts the `oid` and UO asserted by a caller holding a client credential; it does not validate the end-user JWT or gateway assertion itself. The consuming service must validate both before calling it. Keep client credentials in a secret store, give each consuming service its own credential, and restrict network access to the service. Before treating this boundary as production hardened, replace API keys with authenticated workload identities bound to each service or validate delegated identity at the User Management service.

## Service endpoints

In Development, browse to `http://localhost:5220/swagger` after starting the `http` launch profile. Swagger UI loads the versioned [OpenAPI 3 specification](../src/UserManagement.Service/wwwroot/openapi/user-management.v1.json), which is also available at `/openapi/user-management.v1.json`. Client operations require the client key. Administration operations require the administrator key and a validated Entra bearer token. Administrator duty lookup uses a separate key plus the same validated Entra token.

Every request except `GET /health` and `GET /ready` requires `X-User-Management-Key`. Client credentials are accepted on `POST /v1/users/resolve` and `GET /v1/users/revision`. Administrator credentials are accepted on administration endpoints. `GET /v1/admin/access` requires the separate administrator lookup key. Administrator and administrator-lookup requests require a validated Entra token with `oid`; current management duties come from the database. An actor ID supplied in a request is never trusted. Credentials are configured separately and must be distinct and at least 32 characters long. Keep production service credentials in a secret store.

SuperAdmin has tenant-wide management rights; Admin has rights only within assigned, active UOs. A plain User has no management rights. Only SuperAdmin can grant or revoke either duty, globally disable or delete users, and create, rename, or delete UOs. Admin can create users and manage memberships, business roles, and profiles for users who belong to an assigned UO. A profile change is visible in the user's other UOs because the profile is tenant-wide. The final active SuperAdmin cannot be revoked, disabled, or deleted. The services do not provision the first SuperAdmin; an existing SuperAdmin can assign further duties through the administrator API.

| Method and path | Credential | Purpose |
|---|---|---|
| `POST /v1/users/resolve` | Client | Read an existing UO, user, membership, active status, and assigned role IDs; no writes |
| `GET /v1/users/revision` | Client | Read the shared role and status revision for cache invalidation |
| `GET /v1/users?tenantId=default&page=1&pageSize=100` | Administrator | Page through all users in a tenant, including inactive users |
| `GET /v1/users/by-email?tenantId=default&email=user%40example.com` | Administrator | Page through case-insensitive email matches; optional `unitOrganizationId`, `page`, and `pageSize` filters |
| `GET /v1/unit-organizations/{id}/users?tenantId=default&page=1&pageSize=100` | Administrator | Page through users who belong to one active UO; `404` if UO is unknown or tombstoned |
| `GET /v1/users/{objectId}?tenantId=default&unitOrganizationId=<guid>` | Administrator | Read user status and role IDs for a UO; `404` if unknown |
| `PUT /v1/users/{objectId}/roles?tenantId=default&unitOrganizationId=<guid>` | Administrator | Replace role IDs in that UO; `[]` removes all roles there |
| `PUT /v1/users/{objectId}/active?tenantId=default` | Administrator | Enable or disable the user |
| `GET /v1/admin/access?tenantId=default&unitOrganizationId=<guid>` | Admin lookup key and Entra token | Return actor `isActive`, `isSuperAdmin`, `managedUnitOrganizationIds`, and `canManage` for requested UO |
| `POST /v1/users/{objectId}?tenantId=default&unitOrganizationId=<guid>` | UO Admin or SuperAdmin | Create user and first UO membership with optional existing role IDs; body `{ "displayName": "Name", "email": null, "roleIds": [] }` |
| `PUT /v1/users/{objectId}?tenantId=default&unitOrganizationId=<guid>` | UO Admin or SuperAdmin | Update user profile after confirming membership in the selected UO; profile is shared across that user's UOs |
| `DELETE /v1/users/{objectId}?tenantId=default` | SuperAdmin | Soft delete the user in the tenant |
| `PUT /v1/users/{objectId}/memberships/{unitOrganizationId}?tenantId=default` | UO Admin or SuperAdmin | Assign an existing user to that UO with no roles; restoration clears old roles and Admin duty |
| `DELETE /v1/users/{objectId}/memberships/{unitOrganizationId}?tenantId=default` | UO Admin or SuperAdmin | Revoke only that UO membership; other UOs remain available |
| `PUT /v1/users/{objectId}/duties/super-admin?tenantId=default` | SuperAdmin | Grant or revoke tenant SuperAdmin with `{ "isActive": true }` or `false` |
| `PUT /v1/users/{objectId}/duties/unit-admin/{unitOrganizationId}?tenantId=default` | SuperAdmin | Grant or revoke UO Admin duty |
| `GET /v1/roles?tenantId=default&page=1&pageSize=100` | Administrator | Page through roles in all active UOs of a tenant |
| `GET /v1/roles?tenantId=default&unitOrganizationId=<guid>&page=1&pageSize=100` | Administrator | Page through roles in one UO; `404` if UO is unknown or tombstoned |
| `GET /v1/roles/{roleId}?tenantId=default&unitOrganizationId=<guid>` | Administrator | Get one role, including its active status; `404` outside the selected scope |
| `GET /v1/unit-organizations/{id}/roles?tenantId=default&page=1&pageSize=100` | Administrator | Equivalent UO-scoped role list |
| `POST /v1/roles` | Administrator | Create a role with a UO-unique code and generated GUID ID |
| `PUT /v1/roles/{roleId}/active?tenantId=default&unitOrganizationId=<guid>` | Administrator | Disable or re-enable a role with `{ "isActive": false }` or `true` |
| `PUT /v1/roles/{roleId}?tenantId=default&unitOrganizationId=<guid>` | UO Admin or SuperAdmin | Update a business role display name |
| `DELETE /v1/roles/{roleId}?tenantId=default&unitOrganizationId=<guid>` | UO Admin or SuperAdmin | Soft delete a business role and remove it from future resolutions |
| `POST /v1/unit-organizations` | Administrator | Create a tenant-scoped UO with caller-supplied stable GUID ID and name |
| `GET /v1/unit-organizations?tenantId=default` | Administrator | List UOs in a tenant |
| `GET /v1/unit-organizations/{id}?tenantId=default` | Administrator | Read a UO |
| `PUT /v1/unit-organizations/{id}?tenantId=default` | Administrator | Rename a UO without changing its ID |
| `DELETE /v1/unit-organizations/{id}?tenantId=default` | Administrator | Tombstone a UO; retained users and roles are no longer usable through it |
| `GET /health` | None | Liveness only |
| `GET /ready` | None | SQL Server connectivity |

For existing list routes, tenant-wide `GET /v1/users` and `GET /v1/roles` require SuperAdmin. UO-scoped user and role reads, role assignment, and role active-status changes allow that UO's Admin or SuperAdmin. `GET /v1/users/by-email` requires SuperAdmin when no UO filter is supplied. `GET /v1/unit-organizations` returns all UOs for SuperAdmin and only managed UOs for Admin. User active-status changes and UO create/update/delete require SuperAdmin.

Resolution request:

```json
{ "tenantId": "default", "unitOrganizationId": "33333333-3333-3333-3333-333333333333", "objectId": "9155ff4d-2708-4770-b87e-e238645ec5df" }
```

Resolution response:

```json
{ "userId": "70f515ab-3c8d-42ab-92a3-3a06f1f9d938", "tenantId": "default", "unitOrganizationId": "33333333-3333-3333-3333-333333333333", "unitOrganizationName": "Customer Operations", "isActive": true, "roles": [] }
```

List responses use `{ "items": [...], "totalCount": 25, "page": 1, "pageSize": 100 }`. User summaries contain `userId`, `objectId`, `displayName`, nullable `email`, `isActive`, and `memberships`. Each membership contains the UO ID/name and its assigned roles with role ID, code, display name, and active status. A tenant user list shows active UO memberships; a UO-filtered list shows only the requested membership. Inactive role assignments remain visible for administration, while resolution omits them from authorization claims. Roles are loaded in batches for each user page. Email is a profile field, not a unique identity: email lookup returns a page of matches (possibly empty), never creates a user, and can be narrowed to one UO. SQL Server maintains an indexed computed lowercase email value for this search. Role summaries contain `roleId`, `tenantId`, `unitOrganizationId`, `code`, `displayName`, and `isActive`. A tenant user list includes inactive users; UO user lists include only members of that active UO. Role lists include inactive roles so administrators can re-enable them. `page` starts at 1; `pageSize` is 1–100. Invalid pagination or arithmetic overflow returns `400`; a page beyond the end has empty `items` and the same `totalCount`. Ordering is stable: users by oid then user ID; roles by UO ID, code, then role ID. The UO-scoped role alias and the optional `unitOrganizationId` filter on `/v1/roles` return the same page shape.

`GET /v1/roles` previously returned a bare array. It now returns a paged object; administrator clients must read `items` and handle pagination. The path stays `/v1/roles`.

Create a UO explicitly with `{ "tenantId": "default", "id": "33333333-3333-3333-3333-333333333333", "name": "Customer Operations" }`. Create a role with `{ "tenantId": "default", "unitOrganizationId": "33333333-3333-3333-3333-333333333333", "code": "manager", "displayName": "Manager" }`. You can supply its ID in `POST /v1/users/{objectId}` as `{ "displayName": "Example User", "email": null, "roleIds": ["<role-guid>"] }`, or omit `roleIds` to create a roleless user and later use `PUT /v1/users/{objectId}/roles` with `{ "roleIds": ["<role-guid>"] }`. Use `[]` to leave the user without roles. Display names may repeat; codes are unique within a UO. Role assignment rejects IDs from another tenant or UO and disabled or deleted role IDs. Active status body: `{ "isActive": false }`. Disabling a role retains existing assignments but omits that role from subsequent resolutions; re-enabling restores those assignments. A deleted role remains tombstoned and cannot be re-enabled. Changes increment the shared revision, invalidating other client caches after polling or five-second expiry. Invalid requests return `400`, missing or wrong credentials return `401`, missing management duty returns `403`, unknown administrative targets return `404`, conflicts including final SuperAdmin removal return `409`, and unexpected database failures return `500`.

Entity updates record `LastUpdateUser` and UTC `LastUpdateTime`. These fields identify the last writer; they are not a history of changes. Business transactions still keep mutations and revision increments consistent.

Deleting a UO marks it deleted permanently for that ID, even when users or roles still reference it. The rows remain tombstoned, but GET/list omit the UO and resolution and role assignment reject its ID. REST creation with that ID returns `409`; use a new GUID for a replacement UO. This prevents an old gateway assertion from reactivating Access Control grants that still refer to the deleted ID; clean up those policies and grants separately. Already cached resolutions can remain effective for up to five seconds.

## Consuming service order

```csharp
builder.Services.AddAuthentication(/* Entra JWT bearer authentication */);
builder.Services.AddUserManagement(builder.Configuration);
builder.Services.AddAccessControl(builder.Configuration);

app.UseRouting();
app.UseAuthentication();
app.UseUserManagement();
app.UseAccessControl();
app.UseAuthorization();
app.MapControllers();
```

The Access Control client sends the role IDs added to `HttpContext.User` by User Management middleware. Both clients must share the application tenant claim type and default tenant. The host must authenticate the gateway and add a `Gateway` identity carrying `unit_organization_id` and `unit_organization_name` before User Management middleware runs. Configure `UserManagement:GatewayIdentityAuthenticationType` and the two UO claim types if the gateway uses other names. `UserManagement.Client` adds UO ID/name, role IDs/codes, tenant ID, and internal user ID to a dedicated identity on `HttpContext.User` for the current request. ASP.NET Core controller role checks can use role codes; Access Control uses the role IDs with UO scope.

User Management returns `401` for an authenticated principal without a valid `oid`, `403` for an app-only principal without `scp`, a missing authenticated gateway UO assertion, an inactive user, or an unknown UO, user, or membership, and `503` if the service cannot be reached. The service returns `404` for an unknown UO, user, or membership; the client maps that refusal to `403`. Anonymous requests pass through to Access Control, which handles public policies.

## Cache and revocation

The NuGet client holds at most `UserManagement:MaximumCachedUsers` entries keyed by tenant, UO ID, and oid. Each user resolution has an absolute lifetime no longer than five seconds; concurrent misses for the same key share one service call. Administrator UO, role, and active-status changes increment a database revision. Every consuming service instance polls `GET /v1/users/revision` once per second by default and clears its cache when the revision changes. A role or active-status change can remain in an already cached response for up to five seconds if polling fails. Errors are never cached. Configure `UserManagement:RevisionPollInterval` no longer than `CacheLifetime`. The poll costs one small request and database read per client instance per interval.

The cache avoids repeated HTTP and SQL work, but the benchmark in `artifacts/benchmarks/user-management-sqlite.json` measures in-process EF Core SQLite lookups against warm cache hits. It does not include network latency, SQL Server contention, or JWT validation.
