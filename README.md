# Standalone Access Control

.NET 10 Access Control service and reusable ASP.NET Core NuGet packages. The solution has no Asseco dependency. Endpoint permissions are stored centrally; consuming controllers need no Access Control attributes.

## Projects

- `src/AccessControl.Contracts`: request and response models.
- `src/AccessControl.Client`: middleware NuGet package for ASP.NET Core hosts.
- `src/AccessControl.Domain`, `Application`, `Infrastructure`: policy model, evaluator, and SQL Server persistence.
- `src/AccessControl.Service`: decision and administration API.
- `samples/CustomerApi`: host integration example.

## Run the service

Set a SQL Server connection string and two distinct secrets through environment variables or a secret store:

```powershell
$env:ConnectionStrings__AccessControl = '<SQL Server connection string>'
$env:ApiKeys__DecisionKey = '<decision key>'
$env:ApiKeys__AdminKey = '<admin key>'
dotnet ef database update --project src/AccessControl.Infrastructure --startup-project src/AccessControl.Service
dotnet run --project src/AccessControl.Service
```

In Development, open `http://localhost:5219/swagger` for the interactive OpenAPI 3 documentation. The importable specification is at `src/AccessControl.Service/wwwroot/openapi/access-control.v1.json` and is served at `http://localhost:5219/openapi/access-control.v1.json`. In Swagger UI, use **Authorize** with `ApiKeys:AdminKey` for policy and role endpoints, or `ApiKeys:DecisionKey` for the decision endpoint. Configure the SQL Server connection string and apply migrations before trying database-backed operations. The Swagger UI and specification are served only in Development.

The migrations create `EndpointPolicies`, `EndpointPolicyEntitlements`, and `AuthorizationRevisions`. They retain the old `AccessPolicies` table and its data for a separate migration decision. Old resource/action rows do **not** authorize endpoints; create endpoint policies before directing traffic to the new middleware. `RoleEntitlements` remains the role grant table.

The service caches endpoint policies and role grant sets in memory. Each instance holds at most 10,000 cache entries by default, and each entry expires after five seconds. Policy and grant writes clear the writer's cache immediately and increment the shared `AuthorizationRevisions` row in the same database transaction. Other instances poll that row every second and clear their caches when it changes. **Five seconds is the maximum intended revocation delay for an already cached decision input** if polling fails; an in-flight request may still finish using data it read earlier. Configure `AccessControlCache:MaximumEntries`, `EntryLifetime`, and `RevisionPollInterval` in the service. Startup validation rejects lifetimes above five seconds. Apply the revision migration before starting the updated service.

## Use the NuGet package in another service

Pack `AccessControl.Contracts` and `AccessControl.Client`, place both packages in a private feed or local NuGet source, then install `AccessControl.Client` in the consuming ASP.NET Core service. The client package references contracts.

```powershell
dotnet pack src/AccessControl.Contracts -c Release -o artifacts/packages
dotnet pack src/AccessControl.Client -c Release -o artifacts/packages
dotnet add <consumer-project.csproj> package AccessControl.Client --version 0.2.1 --source artifacts/packages
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
