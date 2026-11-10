# User Management API and client

The consuming service validates the Entra access token first. `UserManagement.Client` reads the validated `oid`, the configured application tenant claim (default `default`), and the configured exact delegated `scp` scope. It does not edit or reissue the JWT. User Management stores tenant-scoped profiles, role definitions with stable GUID IDs, and user-role assignments. Access Control stores endpoint policies and entitlements granted to those IDs.

The first successful resolution for `(TenantId, oid)` creates a user with that tenant's `reviewer` role ID. The user is never matched by email. Email and display name are optional profile fields and may be updated on later resolution. An inactive user is denied before Access Control runs. The application `TenantId` is separate from Entra `tid`; if multiple Entra issuers can enter the same application tenant, include issuer or Entra `tid` in the external identity key before enabling that scenario.

The User Management service trusts the `oid` asserted by a caller holding a client credential; it does not validate the end-user JWT itself. Keep client credentials in a secret store, give each consuming service its own credential, and restrict network access to the service. Before treating this boundary as production hardened, replace API keys with authenticated workload identities bound to each service or validate a delegated user token at the User Management service. The automatically assigned `reviewer` role has only the access granted to that role in Access Control.

## Service endpoints

In Development, browse to `http://localhost:5220/swagger` after starting the `http` launch profile. Swagger UI loads the versioned [OpenAPI 3 specification](../src/UserManagement.Service/wwwroot/openapi/user-management.v1.json), which is also available at `/openapi/user-management.v1.json`. Select **Authorize** and enter the client credential for Client operations or the administrator credential for Administration operations. If both are entered, clear the one you are not using because both schemes write the same HTTP header.

Every request except `GET /health` and `GET /ready` requires `X-User-Management-Key`. Client credentials are accepted on `POST /v1/users/resolve` and `GET /v1/users/revision`. Administrator credentials are accepted on the other user endpoints. Credentials are configured by client or actor ID and must be distinct and at least 32 characters long. The repository includes fixed local Development values in `appsettings.Development.json`; keep production credentials in a secret store or environment variables.

| Method and path | Credential | Purpose |
|---|---|---|
| `POST /v1/users/resolve` | Client | Find or create a user; return active status and roles |
| `GET /v1/users/revision` | Client | Read the shared role and status revision for cache invalidation |
| `GET /v1/users/{objectId}?tenantId=default` | Administrator | Read user status and role IDs; `404` if unknown |
| `PUT /v1/users/{objectId}/roles?tenantId=default` | Administrator | Replace the complete role-ID set; `[]` removes all roles |
| `PUT /v1/users/{objectId}/active?tenantId=default` | Administrator | Enable or disable the user |
| `GET /v1/roles?tenantId=default` | Administrator | List role definitions and IDs for a tenant |
| `POST /v1/roles` | Administrator | Create a role with a tenant-unique code and generated GUID ID |
| `GET /health` | None | Liveness only |
| `GET /ready` | None | SQL Server connectivity |

Resolution request:

```json
{ "tenantId": "default", "objectId": "9155ff4d-2708-4770-b87e-e238645ec5df", "email": "user@example.com", "displayName": "Example User" }
```

Resolution response:

```json
{ "userId": "70f515ab-3c8d-42ab-92a3-3a06f1f9d938", "tenantId": "default", "isActive": true, "roles": [{ "roleId": "b78650b4-c248-cab1-3d80-9a7c0dad7059", "code": "reviewer", "displayName": "Reviewer" }] }
```

Create a role with `{ "tenantId": "default", "code": "manager", "displayName": "Manager" }`, then assign the returned ID with `{ "roleIds": ["<role-guid>"] }`. Display names may repeat; codes are unique within a tenant. Role replacement rejects IDs from another tenant. Active status body: `{ "isActive": false }`. Invalid requests return `400`, missing or wrong credentials return `401`, unknown administrative targets return `404`, and unexpected database failures return `500`.

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

The Access Control client must set `AccessControl:RoleIdClaimType` to `access_control_role_id` and `AccessControl:RoleIdentityAuthenticationType` to `UserManagement`. Both clients must share the application tenant claim type and default tenant. `UserManagement.Client` adds role IDs, role codes, tenant ID, and internal user ID to a dedicated identity on `HttpContext.User` for the current request. ASP.NET Core controller role checks can use role codes; Access Control decisions use IDs.

User Management returns `401` for an authenticated principal without a valid `oid`, `403` for an app-only principal without `scp` or an inactive user, and `503` if the service cannot be reached. Anonymous requests pass through to Access Control, which handles public policies.

## Cache and revocation

The NuGet client holds at most `UserManagement:MaximumCachedUsers` entries. Each user resolution has an absolute lifetime no longer than five seconds; concurrent misses for the same user share one service call. Administrator role and active-status changes increment a database revision in the same transaction. Every consuming service instance polls `GET /v1/users/revision` once per second by default and clears its cache when the revision changes. A role or active-status change can remain in an already cached response for up to five seconds if polling fails. Errors are never cached. Configure `UserManagement:RevisionPollInterval` no longer than `CacheLifetime`. The poll costs one small request and database read per client instance per interval.

The cache avoids repeated HTTP and SQL work, but the benchmark in `artifacts/benchmarks/user-management-sqlite.json` measures in-process EF Core SQLite lookups against warm cache hits. It does not include network latency, SQL Server contention, or JWT validation.
