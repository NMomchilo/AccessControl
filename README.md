# Access Control

This .NET 10 repository contains the Access Control service and reusable `AccessControl.Client` and `AccessControl.Contracts` NuGet packages. User Management lives in the separate `UserManagement` repository. User Management owns users, unit organizations, business roles, and administrator duties; Access Control owns endpoint policies, entitlements, and grants to stable role IDs. Policy deletion sets `IsActive=false`; upserting the same policy key reactivates it.

## Request flow

The consuming API validates the Entra JWT and trusted gateway organization assertion. The separately packaged User Management middleware resolves the existing user and adds role IDs and administrative duty claims to `HttpContext.User`. Access Control middleware then sends the endpoint method, route template, tenant, unit organization, subject `oid`, role IDs, `isSuperAdmin`, and `isUnitAdmin` to `POST /v1/decisions`. Access Control first finds an active endpoint policy. Public policies permit; protected policies permit an authenticated SuperAdmin in any active UO of the tenant or a unit Admin in their active UO, even without business roles. Otherwise it compares role entitlements. The controller runs only after Permit. Missing policies fail closed. A tenant-wide public policy uses the all-zero organization ID and requires no role IDs.

Ordinary decisions do not call User Management. Access Control does call its `GET /v1/admin/access` endpoint to check current SuperAdmin or unit Admin duties before administrator changes. This lookup uses a scoped key and forwards the administrator's validated Entra bearer token. Its small local response type matches the User Management HTTP contract, so the Access Control service builds without a User Management source or package dependency.

User Management can hold a tenant user without a unit organization. An ordinary user needs an existing UO membership and role IDs to satisfy a protected policy. An active SuperAdmin may resolve any active UO in their tenant without membership or business roles. Anonymous requests still pass User Management middleware and can reach global public policies; an authenticated user without a UO does not bypass User Management middleware merely because an endpoint is public. Decision clients are trusted to assert both role IDs and duty flags: a valid client key plus a matching user bearer token is sufficient for the asserted administrative override.

## Run locally

Install the .NET 10 SDK and Docker. This repository's local SQL Server uses port **1433**. User Management has a separate local SQL Server on port 1434. The Development connection now points to `AccessControlIsActiveLocalDev`; an earlier local database with the replaced initial migration is not upgraded automatically.

```powershell
docker compose up -d sqlserver
dotnet run --project src/AccessControl.Service --launch-profile https
```

Open `https://localhost:7219/swagger`. Development startup can apply the fresh first-version migration. The example configuration is in `src/AccessControl.Service/appsettings.Development.json`. Replace example keys and connection strings outside Development. `GET /health` is a liveness check; `GET /ready` checks database connectivity and returns `503` when it cannot connect. Neither GET probe needs an API key. Administrator API calls need the administrator key and validated Entra token; decision calls need a client key, and protected decisions need a bearer token whose `oid` matches `subjectId`.

## NuGet packages and consuming APIs

```powershell
dotnet pack src/AccessControl.Contracts -c Release -o artifacts/packages
dotnet pack src/AccessControl.Client -c Release -o artifacts/packages
```

Install matching `AccessControl.Client` and `AccessControl.Contracts` versions. A consuming API that also uses User Management installs its `Client` and `Contracts` packages from the User Management repository or package feed. Register both with `builder.Services.AddUserManagement(configuration)` and `builder.Services.AddAccessControl(configuration)` and order the middleware after JWT and gateway authentication:

```csharp
AccessControlCatalogRegistrationOptions registration = builder.Configuration
    .GetRequiredSection("AccessControl:Registration")
    .Get<AccessControlCatalogRegistrationOptions>()
    ?? throw new InvalidOperationException("AccessControl:Registration is required.");

app.UseRouting();
app.UseAuthentication();
app.UseUserManagement();
app.UseAccessControl();
app.UseAuthorization();
app.MapControllers();
await app.RegisterAccessControlCatalogAsync(registration);
```

`UserManagement.Client` adds role IDs to the authenticated request before Access Control runs. The consuming API must authenticate the gateway assertion before adding a `Gateway` identity; untrusted organization headers are insufficient. See [the Access Control API guide](docs/api-contract.md), [OpenAPI 3](src/AccessControl.Service/wwwroot/openapi/access-control.v1.json), and the separate User Management README for full setup.

Set `AccessControl:Registration` to the consuming project's `TenantId`, `UnitOrganizationId`, `UnitOrganizationName`, and `ModuleName`, then pass the bound object to `RegisterAccessControlCatalogAsync`. Startup calls User Management to create or confirm the organization, then Access Control to create or confirm the module and insert missing endpoint policies. It reuses `UserManagement:ServiceUrl` and `UserManagement:ApiKey` and the existing `AccessControl:ServiceUrl` and `AccessControl:ApiKey`; no registration URL, key, or client is added. Enable `CanRegisterUnitOrganizations` and `CanRegisterCatalog` on those existing service clients. A registration-enabled key can supply any tenant, organization, and module; each service records its configured `ClientId` as the write actor. If either call fails, startup fails. New policies deny until an administrator edits them to be public or sets their required entitlements. Existing policy settings are preserved. The JSON catalog export is removed.

## Build, tests, and benchmarks

The middleware flow test in this repository uses versioned User Management Client and Contracts packages with fake service clients. The full access-flow benchmark uses those same packages and calls both running services over HTTP. Pack User Management packages into its sibling repository's `artifacts/packages` folder before local restore; `NuGet.config` points to that feed. CI can use a published package feed instead.

```powershell
dotnet test AccessControl.sln -c Release
dotnet run --project benchmarks/AccessControl.Benchmarks -c Release -- --requests 5000 --concurrency 1,16,64
```

See [benchmark instructions](benchmarks/README.md). Tests use SQLite and in-process HTTP. Validate migrations, revocation delay, Entra configuration, and load against dedicated SQL Server staging deployments before production use. The small `Common.Persistence` and `AdminManagement.Security` projects are maintained locally in this repository as server-only support code. Database writes require a validated actor `oid` for `LastUpdateUser`; no runtime fallback actor exists.
