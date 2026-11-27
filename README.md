# Access Control

This .NET 10 repository contains the Access Control API. The `AccessControl.Client` and `AccessControl.Contracts` NuGet source and package tests live in the separate Shared repository. The separate `UserManagement` repository owns users, unit organizations (UOs), UO memberships, business roles, and administrative duties. Access Control owns endpoint policies, an entitlement catalog, and entitlement grants to role IDs. It stores no user-to-role assignments.

## Request flow

1. The consuming API authenticates the Entra JWT and receives the UO ID/name in headers set by its trusted ingress. Route matching supplies the HTTP method and **route template**, such as `/customers/{id}`.
2. `UserManagement.Client` calls `POST /v1/users/resolve` for an authenticated `oid`, tenant, and UO, or reads its Redis cache. It adds the returned role IDs and SuperAdmin/current-UO Admin duties to a dedicated identity in `HttpContext.User`. It does not change the JWT.
3. `AccessControl.Client` sends the route, tenant, UO, subject `oid`, role IDs, and duty flags to `POST /v1/decisions`. It forwards the original bearer token for authenticated decisions.
4. Access Control requires the bearer `oid` to match a non-null `subjectId`. It finds the endpoint policy. A public policy permits. A protected policy permits an authenticated caller with an asserted SuperAdmin or current-UO Admin duty; otherwise a forwarded role ID must have a required, active entitlement. A missing policy denies. Only Permit reaches the business controller.

Ordinary decisions do not call User Management directly. For Access Control **administrator API** requests, the service calls User Management's `GET /v1/admin/access` with the administrator's Entra bearer token to check current management scope. That lookup is distinct from the business decision override.

An active SuperAdmin can resolve any active UO in their application tenant without membership or a business role. A UO Admin needs an active membership in that UO. Anonymous requests can reach global public policies; an **authenticated** request still goes through User Management first and needs valid ingress organization headers, even if its eventual policy is public. A UO-specific policy takes precedence over a global public fallback.

**Current trust boundary:** Access Control does not independently verify role IDs or the two duty flags on an ordinary decision. A caller that can reach the decision route and holds a matching user bearer token can assert `isSuperAdmin: true` or `isUnitAdmin: true`. Restrict the decision route to trusted consuming APIs; the proposed signed, short-lived authorization context is not implemented.

## Policies, entitlements, and administration

The policy key is `(tenantId, unitOrganizationId, httpMethod, routeTemplate)`. An administrator creates an entitlement with a required, immutable `code` and display name using `POST /v1/entitlements`, grants its ID to a User Management role ID with `PUT /v1/roles/entitlements`, and sets a protected route's required entitlement IDs with `PUT /v1/policies`. Codes are unique within tenant and UO, including disabled records; decisions still use GUID IDs. Required IDs use **OR** semantics. `GET /v1/policies` reads one exact key; `GET /v1/policies/search` lists active policies. `isPublic: true` requires an empty entitlement list. A global public policy uses the all-zero UO ID and applies only when no UO-specific policy takes precedence. Deleting a policy makes it inactive; upserting the same key reactivates it. Disabling an entitlement removes its effect after cache invalidation or expiry.

Administrator endpoints require a validated Entra bearer token and the relevant SuperAdmin or UO Admin duty from User Management. See the [API guide](docs/api-contract.md) and [OpenAPI 3 specification](src/AccessControl.Service/wwwroot/openapi/access-control.v1.json) for request bodies and all endpoints.

## Run locally

Install the .NET 10 SDK and Docker. From this repository root, start the SQL Server defined in [docker-compose.yml](docker-compose.yml) and then the service:

```powershell
# The updated client packages and their dependencies are included in .packages.
docker compose up -d sqlserver
docker compose up -d redis
dotnet run --project src/AccessControl.Service --launch-profile https
```

Local Redis listens on `localhost:6379`. Access Control uses `ConnectionStrings:LocalRedisServiceConnectionString` in Development; the CustomerApi sample uses the same endpoint through `UserManagement.Client`. Every consuming API that installs that client now needs its own Redis connection setting. For deployment, configure `ConnectionStrings:RedisServiceConnectionString`, a deployment-specific `Redis:KeyPrefix`, and optional `Redis:ApplicationDatabaseIndex`; use authenticated TLS and a bounded Redis deployment. Cache entries expire after five seconds. Redis failures make the service load from SQL Server and consuming APIs load from User Management; a source failure still denies continuation.

`KeyVault:Url` enables the shared Key Vault configuration provider in both services and the CustomerApi sample. Leave it empty for local runs. With an HTTPS vault URL, `DefaultAzureCredential` uses the deployed identity; optional `KeyVault:TenantId`, `ClientId`, and `ClientSecret` must come from a secure source. A vault secret named `ConnectionStrings--AccessControl` supplies the SQL connection string; `ConnectionStrings--RedisServiceConnectionString` supplies Redis. The provider loads configuration at startup, so restart the service after rotating a secret.

Open `https://localhost:7219/swagger`. Development startup can apply the fresh first-version migration. The example configuration is in `src/AccessControl.Service/appsettings.Development.json`. Replace example connection strings outside Development. `GET /health` is a liveness check; `GET /ready` checks database connectivity and returns `503` when it cannot connect. Neither GET probe needs an API key. Administrator API calls need a validated Entra token; protected decisions need a bearer token whose `oid` matches `subjectId`. Keyless service routes must be restricted to trusted network callers.

The local connection uses `localhost,1433`, database `AccessControlIsActiveLocalDev`, and the Docker compose `sa` password. Automatic migration runs **only in Development** when `AccessControlDatabase:ApplyMigrationsOnStartup` is true. This is a fresh first-version schema; an older local database made from a replaced migration is not upgraded in place. Run the User Management service separately on port 5220, with its SQL Server on port 1434, before testing administrator operations or a consuming API's startup registration. Swagger UI is available only in Development; its specification is also served at `/openapi/access-control.v1.json` then.

The [CustomerApi settings](samples/CustomerApi/appsettings.json) show the keyless consuming API configuration. A running API is not enough for a role-based protected decision: the policy, entitlement, role grant, and provisioned user must also exist. Startup registration creates missing protected policies with no entitlements, so those routes initially deny.

There are no Access Control API keys. Decision and registration requests rely on the deployment network boundary; authenticated decisions also validate the forwarded Entra token and its `oid`. Registration uses an `unattributed-registration` write actor because no caller ID is supplied.

## NuGet packages and consuming APIs

The service, tests, benchmarks, and CustomerApi sample use versioned Shared NuGet packages (AccessControl.Client and UserManagement.Client 0.21.0; other packages 0.17.0). This repository includes the seven package files in `.packages`: `AccessControl.Client`, `AccessControl.Contracts`, `UserManagement.Client`, `UserManagement.Contracts`, `Common.Persistence`, and `AdminManagement.Security`, and `Shared.Infrastructure`. Their versions must match the project references. When Shared releases a new version, pack it, update the project references, and replace these package files. For example, after packing Shared in any location:

```powershell
Copy-Item 'C:\path\to\Shared\packages\*.nupkg' .packages\
dotnet restore AccessControl.sln
```

`NuGet.config` maps these package IDs to `.packages`; no sibling checkout or package download is needed after cloning this repository with the package files committed. Once a private feed is chosen, replace that local source with the feed URL and supply its credentials through CI secrets; do not commit credentials.

Install `AccessControl.Client` 0.21.0 and `AccessControl.Contracts` 0.17.0. A consuming API using the full flow also installs `UserManagement.Client` 0.21.0 and `UserManagement.Contracts` 0.17.0. The host registers both clients and configures JWT authentication. The relevant ordering is:

```csharp
builder.Services.AddUserManagement(builder.Configuration);
builder.Services.AddAccessControl(builder.Configuration);

AccessControlCatalogRegistrationOptions registration = builder.Configuration
    .GetRequiredSection("AccessControl:Registration")
    .Get<AccessControlCatalogRegistrationOptions>()
    ?? throw new InvalidOperationException("AccessControl:Registration is required.");

app.UseRouting();
app.UseAuthentication();
// Trusted ingress supplies the organization headers before this point.
app.UseUserManagement();
app.UseAccessControl();
app.UseAuthorization();
app.MapControllers();
await app.RegisterAccessControlCatalogAsync(registration);
app.Run();
```

`UserManagement.Client` reads `X-Unit-Organization-Id` and `X-Unit-Organization-Name` directly from the incoming request. Configure `UnitOrganizationIdHeaderName` and `UnitOrganizationNameHeaderName` when ingress uses different names. Ingress must strip client-supplied copies, set exactly one value for each header, and prevent direct access to the API around ingress. The NuGet validates header values but cannot authenticate their origin. Missing, repeated, or malformed headers deny authenticated requests before user resolution. The [CustomerApi sample](samples/CustomerApi/Program.cs) expects ingress to supply the headers; local direct requests need those headers for testing. Its `/health` route is locally excluded from Access Control; `ExcludedPaths` matches a route for every HTTP method, so reserve it for infrastructure routes.

Set `AccessControl:Registration` to the consuming project's tenant ID and UO ID/name. `RegisterAccessControlCatalogAsync` first asks User Management to create or confirm the UO, then asks Access Control to insert missing policies for discovered method/route pairs. It uses the configured service URLs without keys. Registration fails startup if either service refuses it or a route lacks HTTP-method metadata. Existing policy settings are preserved; new routes start protected with no entitlements and deny until an administrator configures them. The registration endpoints must be reachable only from trusted service networks because the requests have no caller authentication.

## Build, tests, and benchmarks

Clone this repository anywhere; the committed `.packages` files are used during restore. Every project in this solution uses versioned package references for Shared code, including the middleware flow test and access-flow benchmark. The flow test uses fake service clients; the access-flow benchmark calls both running services over HTTP. Package-only tests and coverage live in Shared; this repository retains API, persistence, and flow tests. Build the Docker image from this repository root with `docker build -f src/AccessControl.Service/Dockerfile -t access-control:local .`.

```powershell
dotnet test AccessControl.sln -c Release
dotnet test tests/AccessControl.Tests -c Release --collect:"XPlat Code Coverage" --settings coverage.runsettings
dotnet run --project benchmarks/AccessControl.Benchmarks -c Release -- --requests 5000 --concurrency 1,16,64
```

See [benchmark instructions](benchmarks/README.md) and the [2026-11-20 live SQL Server results](docs/sqlserver-rancher-benchmarks-2026-11-20.md). Tests use SQLite and in-process HTTP. Validate migrations, revocation delay, Entra configuration, and load against dedicated SQL Server staging deployments before production use. Administrator writes use the validated actor `oid` for `LastUpdateUser`; keyless catalog registration uses the explicit `unattributed-registration` actor.
