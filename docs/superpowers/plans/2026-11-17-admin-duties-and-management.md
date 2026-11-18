# Admin duties and management implementation plan

Historical plan: its Reviewer default is superseded. Current users and memberships start without roles.

**Goal:** Add tenant-wide SuperAdmin, unit-organization Admin, ordinary User, scoped management operations, and revocation of one UO membership while preserving the two-service authorization model.

**Architecture:** User Management owns people, memberships, business roles, and management duties. Access Control owns entitlements and endpoint policies. A server-side administration portal authenticates humans with Entra, keeps service credentials off the browser, and forwards a short-lived signed actor assertion. Each service checks actor scope; Access Control obtains current duties from User Management. Routes remain code-defined; an endpoint management record is its authorization policy.

**Constraints:** No Git commits. New users are ordinary Reviewer users. SuperAdmin is tenant-wide and Admin is UO-scoped. Only SuperAdmin changes administrative duties or globally disables a user. Admin revocation of a UO membership must leave other memberships intact. Deleted roles and policies deny after bounded cache invalidation. Missing policies deny. Preserve existing NuGet decision flow for consuming services.

## Tasks

- [x] Add User Management data model, migration, and tests for tenant SuperAdmin, UO Admin, UO membership soft deletion, user soft deletion, and business role soft deletion.
- [x] Add User Management management APIs for user create/update, UO assignment/revocation, role create/update/soft delete, duty assignment/revocation, and permission lookup. Validate tenant/UO scope and audit actor.
- [x] Add Access Control endpoint-policy soft delete and update behavior, scoped administration checks, permission lookup integration, and tests. Keep entitlement indirection for role-to-endpoint access.
- [x] Add server-side administration portal with Entra sign-in, scoped user/role/entitlement/policy screens, and no browser-visible service credentials.
- [x] Update contracts, OpenAPI 3 specifications, README, migrations, and local configuration guidance.
- [x] Run all tests, coverage gate, NuGet pack, and EF migration model checks. SQL Server deployment verification remains for an isolated staging database; existing local databases were not changed.
