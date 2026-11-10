# User Management Service and Client Implementation Plan

**Goal:** Provision Entra users by `oid`, assign `Reviewer` on first sign-in, add current application roles to the consuming request, and provide bounded role caching with benchmarks.

**Architecture:** The consuming service validates the Entra JWT, then UserManagement.Client calls UserManagement.Service using a service credential. The service owns its SQL Server database and role administration. The client caches resolution responses for no longer than five seconds and adds a dedicated role claim before AccessControl.Client evaluates the endpoint.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core SQL Server, xUnit, SQLite integration tests, console benchmark.

## Tasks

- [x] Add contracts and a domain model for user identity, status, and role assignments. Test first-sign-in defaults and role validation.
- [x] Add application orchestration and an EF Core store. Test concurrent first sign-ins against a unique `oid` constraint, profile updates, disabling, and role replacement.
- [x] Add SQL Server migrations and a standalone API with separate per-client and administrator credentials. Test unauthorized and authorized API calls.
- [x] Add the NuGet client middleware after authentication and before Access Control. Test raw and mapped `oid`, anonymous requests, failures, inactive users, role claims, and bounded cache behavior.
- [x] Update AccessControl.Client to consume the dedicated User Management role claim when configured. Preserve its existing default for consumers not using User Management.
- [x] Add a reproducible benchmark comparing uncached and cached resolution calls. Save measured output and explain limits.
- [x] Update API contract, integration and local run documentation, package versions, and examples. Build, test, format, and validate migrations and package output.

No Git commit or push is part of this plan.
