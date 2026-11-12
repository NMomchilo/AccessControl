namespace AccessControl.Domain;

public sealed record EndpointPolicy(
    string TenantId,
    string Module,
    string HttpMethod,
    string RouteTemplate,
    bool IsPublic,
    IReadOnlyList<string> RequiredEntitlements,
    Guid UnitOrganizationId);
