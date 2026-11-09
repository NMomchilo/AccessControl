# Endpoint Policy Authorization Implementation Plan

**Goal:** Authorize every matched service endpoint through centrally managed route and method policies, without controller attributes.

**Architecture:** Middleware reads the selected ASP.NET Core route template, method, tenant, and authenticated role claims. The decision API resolves one endpoint policy and compares its required entitlements with role grants. SQL Server stores endpoint policies and grants; legacy policy rows remain preserved. A catalog export lists the routes a service exposes.

**Tech stack:** .NET 10, ASP.NET Core middleware and controllers, EF Core SQL Server, xUnit.

## Tasks

- [x] Replace decision and policy contracts with endpoint identity and an explicit public mode. Cover Permit, Deny, anonymous, missing policy, and tenant isolation in tests.
- [x] Make middleware inspect `RouteEndpoint.RoutePattern` after routing, handle unknown URLs normally, support configured infrastructure exclusions, and log all outcome branches without logging identities or secrets.
- [x] Add SQL Server endpoint policy tables and a migration, preserving existing `AccessPolicies` data. Test persistence and entitlement replacement against a relational test database.
- [x] Keep `POST /v1/decisions` and policy administration paths, update their payloads, add structured diagnostics, and test the HTTP boundary.
- [x] Export an endpoint catalog from registered routes for deployment review. Update the sample, README, API contract, and architecture guide.
- [x] Format, test, build, pack both NuGet packages, and verify the tree has no Asseco dependency. Leave changes uncommitted.
