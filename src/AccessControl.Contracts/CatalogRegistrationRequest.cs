namespace AccessControl.Contracts;

public sealed record CatalogEndpoint(string HttpMethod, string RouteTemplate);

public sealed record CatalogRegistrationRequest(string TenantId, Guid UnitOrganizationId,
    string Module, string ModuleName, IReadOnlyList<CatalogEndpoint> Endpoints);

public sealed record CatalogRegistrationResult(int ModulesCreated, int PoliciesCreated);
