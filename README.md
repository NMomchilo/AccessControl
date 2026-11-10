# Standalone Access Control

This solution also includes a standalone User Management service and reusable client NuGet package. It provisions authenticated Entra users by `oid`, assigns `reviewer` on first sign-in, caches their application roles for at most five seconds, and adds them to the current request before Access Control runs. Administrator role changes trigger revision polling across client instances; cache expiry bounds staleness if polling fails. See [User Management API and client](docs/user-management-api.md).

.NET 10 Access Control service and reusable ASP.NET Core NuGet packages. The solution has no Asseco dependency. Endpoint permissions are stored centrally; consuming controllers need no Access Control attributes.

## Projects

- `src/AccessControl.Contracts`: request and response models.
- `src/AccessControl.Client`: middleware NuGet package for ASP.NET Core hosts.
- `src/AccessControl.Domain`, `Application`, `Infrastructure`: policy model, evaluator, and SQL Server persistence.
- `src/AccessControl.Service`: decision and administration API.
- `samples/CustomerApi`: host integration example.

## Run the service

To run **User Management** locally with the SQL Server container from this repository:

```powershell
docker compose up -d sqlserver
dotnet run --project src/UserManagement.Service --launch-profile http
```

The [Development settings](src/UserManagement.Service/appsettings.Development.json) point to the Docker container, use a dedicated `UserManagementLocalDev` database, and provide separate local client and administrator keys. Development startup creates or updates that database through EF migrations. The service listens on `http://localhost:5220` and opens [Swagger UI](http://localhost:5220/swagger). Its importable [OpenAPI 3 specification](src/UserManagement.Service/wwwroot/openapi/user-management.v1.json) is served at `http://localhost:5220/openapi/user-management.v1.json`. In Swagger UI, use **Authorize** with the client key for resolve and revision, or the administrator key for user read and writes. `GET /ready` checks database connectivity. The [sample API Development settings](samples/CustomerApi/appsettings.Development.json) use the matching client key. These fixed values are only for local Development; configure production credentials and the production connection string through environment variables or a secret store. Production requires HTTPS and does not apply migrations on startup. Pack the contracts and client with:

```powershell
dotnet pack src/UserManagement.Contracts -c Release -o artifacts/packages
dotnet pack src/UserManagement.Client -c Release -o artifacts/packages
dotnet add <consumer-project.csproj> package UserManagement.Client --version 0.1.0 --source artifacts/packages
```

The sample [CustomerApi](samples/CustomerApi/Program.cs) shows `UseAuthentication()` → `UseUserManagement()` → `UseAccessControl()` and sets `AccessControl:RoleClaimType` to `access_control_role` and `AccessControl:RoleIdentityAuthenticationType` to `UserManagement`. Access Control reads roles only from the User Management identity. Assign Access Control entitlements to the `reviewer` role separately; automatic user provisioning does not create role grants.

For local end-to-end testing, start Access Control with its `https` launch profile (`https://localhost:7219`) and User Management with its `http` Development profile (`http://localhost:5220`). The sample points to those URLs and has matching Development API keys. Set `Authentication__Authority` and `Authentication__Audience` for your Entra application. Trust the ASP.NET Core development certificate for local HTTPS if needed (`dotnet dev-certs https --trust`).

For a reproducible cache benchmark, run `dotnet run --project benchmarks/UserManagement.Benchmarks -c Release -- --requests 5000 --concurrency 16`. Results go to `artifacts/benchmarks/user-management-sqlite.json`; see [measured results](docs/benchmark-results-2026-11-09.md).

Docker SQL Server and live HTTP/HTTPS benchmark results, including cache-constrained measurements, are in [the Docker benchmark report](docs/docker-sqlserver-benchmark-2026-11-09.md).

The [available route and role mix benchmark](docs/available-policy-mix-benchmark-2026-11-09.md) uses the configured Docker `AppDb` policy and a separate uniform test mix; the repository has no real request-frequency data.

The [end-to-end access-flow benchmark](docs/end-to-end-access-flow-benchmark-2026-11-09.md) measures a locally signed JWT through User Management, Access Control, and a controller against Docker SQL Server.

## Run Access Control

To run Access Control locally against the same Docker SQL Server container:

```powershell
docker compose up -d sqlserver
dotnet run --project src/AccessControl.Service --launch-profile https
```

Development startup applies EF migrations to the configured `AppDb` database. Open `https://localhost:7219/swagger` for the interactive OpenAPI 3 documentation. The importable specification is at `src/AccessControl.Service/wwwroot/openapi/access-control.v1.json` and is served at `https://localhost:7219/openapi/access-control.v1.json`. In Swagger UI, use **Authorize** with `ApiKeys:AdminKey` for policy and role endpoints, or `ApiKeys:DecisionKey` for the decision endpoint. The Swagger UI and specification are served only in Development. For production, override the local connection string and API keys through environment variables or a secret store and apply migrations as a deployment step; startup migration is disabled outside Development.

The migrations create `EndpointPolicies`, `EndpointPolicyEntitlements`, and `AuthorizationRevisions`. They retain the old `AccessPolicies` table and its data for a separate migration decision. Old resource/action rows do **not** authorize endpoints; create endpoint policies before directing traffic to the new middleware. `RoleEntitlements` remains the role grant table.

The service caches endpoint policies and role grant sets in memory. Each instance holds at most 10,000 cache entries by default, and each entry expires after five seconds. Policy and grant writes clear the writer's cache immediately and increment the shared `AuthorizationRevisions` row in the same database transaction. Other instances poll that row every second and clear their caches when it changes. **Five seconds is the maximum intended revocation delay for an already cached decision input** if polling fails; an in-flight request may still finish using data it read earlier. Configure `AccessControlCache:MaximumEntries`, `EntryLifetime`, and `RevisionPollInterval` in the service. Startup validation rejects lifetimes above five seconds. Development startup applies the revision migration; apply it as a deployment step in production.

## Use the NuGet package in another service

Pack `AccessControl.Contracts` and `AccessControl.Client`, place both packages in a private feed or local NuGet source, then install `AccessControl.Client` in the consuming ASP.NET Core service. The client package references contracts.

```powershell
dotnet pack src/AccessControl.Contracts -c Release -o artifacts/packages
dotnet pack src/AccessControl.Client -c Release -o artifacts/packages
dotnet add <consumer-project.csproj> package AccessControl.Client --version 0.2.3 --source artifacts/packages
```

The consuming service authenticates users as usual. Add the middleware after routing and authentication, before authorization and controllers:

The middleware uses the validated JWT's `oid` claim as `subjectId`, including its URI claim type when ASP.NET Core maps inbound claims. An authenticated caller without an object ID receives HTTP 401. It reads application roles from `ClaimTypes.Role` or `role` claims on `HttpContext.User`.

```csharp
builder.Services.AddAuthentication(/* existing scheme */);
builder.Services.AddAuthorization();
builder.Services.AddAccessControl(builder.Configuration);
builder.Services.AddControllers();

app.UseRouting();
app.UseAuthentication();
app.UseAccessControl();
app.UseAuthorization();
app.MapControllers();
app.ExportAccessControlCatalog(); // optional: writes a catalog when configured
```

Example configuration (put `ApiKey` in a secret store):

```json
{
  "AccessControl": {
    "ServiceUrl": "https://access-control.example/",
    "Module": "customers",
    "TimeoutSeconds": 5,
    "TenantClaimType": "tenant_id",
    "DefaultTenantId": "default",
    "ExcludedPaths": ["/health"],
    "CatalogOutputPath": "endpoint-catalog.json"
  }
}
```

The middleware uses the matched route template, such as `/customers/{id}`, and the HTTP method. It sends that endpoint identity, tenant, authenticated subject, and role claims to `POST /v1/decisions`. The service finds the endpoint policy and compares its required entitlements with grants for those roles. A Permit reaches the controller. Protected anonymous requests return `401`; missing policies and missing grants return `403`; decision service failures return `503`.

All matched endpoints require a central policy, including public endpoints. A public policy has `isPublic: true` and no required entitlements. `ExcludedPaths` is an explicit local bypass intended for infrastructure health checks only. Unmatched routes proceed to the host's normal `404` handling.

The optional endpoint catalog lists method and route templates so a deployment process can compare them with configured policies. Exporting does not create or approve policies. Review new catalog entries and provision their policies before deployment. See [API contract](docs/api-contract.md) and [architecture](docs/architecture.md).

## Logs

Structured logs cover API key acceptance or rejection, route identification, outbound decision timing and HTTP status, policy lookup, grant counts, decision outcome, and policy/role changes. Correlate service and consuming service logs with request trace IDs and your distributed tracing setup. API keys and subject IDs are not logged. Configure ASP.NET Core logging levels and sinks in each host; `Debug` adds per-step detail, while `Information` records decisions and changes.

## Verify

```powershell
dotnet test AccessControl.sln -c Release
dotnet pack src/AccessControl.Contracts -c Release -o artifacts/packages
dotnet pack src/AccessControl.Client -c Release -o artifacts/packages
```

For repeatable decision API load measurements, see the [benchmark harness](benchmarks/README.md) and [local results](docs/benchmark-results-2026-11-07.md).
