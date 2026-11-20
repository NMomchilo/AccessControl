# Access Control

This .NET 10 repository contains the Access Control API and the `AccessControl.Client` and `AccessControl.Contracts` NuGet packages. The separate `UserManagement` repository owns users, unit organizations (UOs), UO memberships, business roles, and administrative duties. Access Control owns endpoint policies, an entitlement catalog, and entitlement grants to role IDs. It stores no user-to-role assignments.

## Request flow

1. The consuming API authenticates the Entra JWT and a trusted gateway assertion identifying the UO. Route matching supplies the HTTP method and **route template**, such as `/customers/{id}`.
2. `UserManagement.Client` calls `POST /v1/users/resolve` for an authenticated `oid`, tenant, and UO, or reads its bounded cache. It adds the returned role IDs and SuperAdmin/current-UO Admin duties to a dedicated identity in `HttpContext.User`. It does not change the JWT.
3. `AccessControl.Client` sends the route, module, tenant, UO, subject `oid`, role IDs, and duty flags to `POST /v1/decisions`. It forwards the original bearer token as well as its decision client key.
4. Access Control checks the client key and requires the bearer `oid` to match a non-null `subjectId`. It finds the endpoint policy. A public policy permits. A protected policy permits an authenticated caller with an asserted SuperAdmin or current-UO Admin duty; otherwise a forwarded role ID must have a required, active entitlement. A missing policy denies. Only Permit reaches the business controller.

Ordinary decisions do not call User Management directly. For Access Control **administrator API** requests, the service calls User Management's `GET /v1/admin/access` with a separate lookup key and the administrator's Entra bearer token to check current management scope. That lookup is distinct from the business decision override.

An active SuperAdmin can resolve any active UO in their application tenant without membership or a business role. A UO Admin needs an active membership in that UO. Anonymous requests can reach global public policies; an **authenticated** request still goes through User Management first and needs valid gateway UO context, even if its eventual policy is public. A UO-specific policy takes precedence over a global public fallback.

**Current trust boundary:** Access Control does not independently verify role IDs or the two duty flags on an ordinary decision. A caller holding a valid decision key and a matching user bearer token can assert `isSuperAdmin: true` or `isUnitAdmin: true`. Keep decision keys with trusted consuming APIs; the proposed signed, short-lived authorization context is not implemented.

## Policies, entitlements, and administration

The policy key is `(tenantId, unitOrganizationId, module, httpMethod, routeTemplate)`. An administrator creates an entitlement with `POST /v1/entitlements`, grants its ID to a User Management role ID with `PUT /v1/roles/entitlements`, and sets a protected route's required entitlement IDs with `PUT /v1/policies`. Required IDs use **OR** semantics. `GET /v1/policies` reads one exact key; `GET /v1/policies/search` lists active policies. `isPublic: true` requires an empty entitlement list. A global public policy uses the all-zero UO ID and applies only when no UO-specific policy takes precedence. Deleting a policy makes it inactive; upserting the same key reactivates it. Disabling an entitlement removes its effect after cache invalidation or expiry.

Administrator endpoints require the Access Control administrator key, a validated Entra administrator bearer token, and the relevant SuperAdmin or UO Admin duty from User Management. See the [API guide](docs/api-contract.md) and [OpenAPI 3 specification](src/AccessControl.Service/wwwroot/openapi/access-control.v1.json) for request bodies and all endpoints.

## Run locally

Install the .NET 10 SDK and Docker. From this repository root, start the SQL Server defined in [docker-compose.yml](docker-compose.yml) and then the service:

```powershell
docker compose up -d sqlserver
dotnet run --project src/AccessControl.Service --launch-profile https
```

Open `https://localhost:7219/swagger`. Development startup can apply the fresh first-version migration. The example configuration is in `src/AccessControl.Service/appsettings.Development.json`. Replace example keys and connection strings outside Development. `GET /health` is a liveness check; `GET /ready` checks database connectivity and returns `503` when it cannot connect. Neither GET probe needs an API key. Administrator API calls need the administrator key and validated Entra token; decision calls need a client key, and protected decisions need a bearer token whose `oid` matches `subjectId`.

The local connection uses `localhost,1433`, database `AccessControlIsActiveLocalDev`, and the Docker compose `sa` password. Automatic migration runs **only in Development** when `AccessControlDatabase:ApplyMigrationsOnStartup` is true. This is a fresh first-version schema; an older local database made from a replaced migration is not upgraded in place. Run the [User Management service](../UserManagement/README.md) separately on port 5220, with its SQL Server on port 1434, before testing administrator operations or a consuming API's startup registration. Swagger UI is available only in Development; its specification is also served at `/openapi/access-control.v1.json` then.

The local Access Control decision key matches the [CustomerApi settings](samples/CustomerApi/appsettings.json); its User Management administrator lookup key matches User Management's Development settings. A running API is not enough for a role-based protected decision: the policy, entitlement, role grant, and provisioned user must also exist. Startup registration creates missing protected policies with no entitlements, so those routes initially deny.

`ApiKeys:DecisionClients` keys authorize decision calls and, with `CanRegisterCatalog`, startup catalog registration. `ApiKeys:AdminKey` is separate and is used with an Entra administrator token for management calls. `UserManagementAdminLookup:LookupKey` is a third credential for Access Control to check administrator scope in User Management. Keep these credentials distinct.

## NuGet packages and consuming APIs

```powershell
dotnet pack src/AccessControl.Contracts -c Release -o artifacts/packages
dotnet pack src/AccessControl.Client -c Release -o artifacts/packages
```

Install matching `AccessControl.Client` and `AccessControl.Contracts` versions (currently 0.12.0). A consuming API using the full flow also installs matching `UserManagement.Client` and `UserManagement.Contracts` packages. The host registers both clients and configures JWT authentication. The relevant ordering is:

```csharp
builder.Services.AddUserManagement(builder.Configuration);
builder.Services.AddAccessControl(builder.Configuration);

AccessControlCatalogRegistrationOptions registration = builder.Configuration
    .GetRequiredSection("AccessControl:Registration")
    .Get<AccessControlCatalogRegistrationOptions>()
    ?? throw new InvalidOperationException("AccessControl:Registration is required.");

app.UseRouting();
app.UseAuthentication();
// Authenticate the gateway assertion and add its Gateway identity here.
app.UseUserManagement();
app.UseAccessControl();
app.UseAuthorization();
app.MapControllers();
await app.RegisterAccessControlCatalogAsync(registration);
app.Run();
```

The comment represents host-specific gateway authentication code; the NuGets do not create or validate that assertion. A `Gateway` identity must carry `unit_organization_id` and `unit_organization_name`. Do not create it from untrusted request headers. The [CustomerApi sample](samples/CustomerApi/Program.cs) currently validates the JWT but does **not** add the `Gateway` identity, so authenticated requests through its User Management middleware receive `403` until the host implements that step. Its `/health` route is locally excluded from Access Control; `ExcludedPaths` matches a route for every HTTP method, so reserve it for infrastructure routes.

Set `AccessControl:Registration` to the consuming project's tenant ID, UO ID/name, and module name. `RegisterAccessControlCatalogAsync` first asks User Management to create or confirm the UO, then asks Access Control to create or confirm the module and insert missing policies for discovered method/route pairs. It reuses the configured User Management and Access Control service URLs and API keys; no separate registration client is needed. Enable `CanRegisterUnitOrganizations` and `CanRegisterCatalog` on those existing client credentials. A registration-enabled key can register any tenant, UO, and module, and each service records its configured client ID as the write actor. Registration fails startup if either service refuses it or a route lacks HTTP-method metadata. Existing policy settings are preserved; new routes start protected with no entitlements and deny until an administrator configures them. No JSON endpoint catalog is written.

## Build, tests, and benchmarks

Place this repository beside `UserManagement` under the same parent directory. The middleware flow test, CustomerApi sample, and full access-flow benchmark reference the sibling User Management client source by default, so local tests do not require prepacked NuGets. To verify the versioned package integration instead, first pack `UserManagement.Client` and `UserManagement.Contracts` 0.12.0 into the sibling repository's `artifacts/packages`, then run `dotnet test AccessControl.sln -c Release -p:UsePublishedClientPackages=true`. The flow test uses fake service clients; the access-flow benchmark calls both running services over HTTP. Coverage reports only this repository's assemblies.

```powershell
dotnet test AccessControl.sln -c Release
dotnet test tests/AccessControl.Tests -c Release --collect:"XPlat Code Coverage" --settings coverage.runsettings
dotnet run --project benchmarks/AccessControl.Benchmarks -c Release -- --requests 5000 --concurrency 1,16,64
```

See [benchmark instructions](benchmarks/README.md) and the [2026-11-20 live SQL Server results](docs/sqlserver-rancher-benchmarks-2026-11-20.md). Tests use SQLite and in-process HTTP. Validate migrations, revocation delay, Entra configuration, and load against dedicated SQL Server staging deployments before production use. The small `Common.Persistence` and `AdminManagement.Security` projects are maintained locally in this repository as server-only support code. Database writes require a validated actor `oid` for `LastUpdateUser`; no runtime fallback actor exists.
