namespace AccessControl.Domain;

public sealed record EndpointPolicy(
    string TenantId,
    string HttpMethod,
    string RouteTemplate,
    bool IsPublic,
    IReadOnlyList<Guid> RequiredEntitlementIds,
    Guid UnitOrganizationId);
