using System.Text.Json.Serialization;

namespace AccessControl.Contracts;

public sealed record PolicyRequest(
    string TenantId,
    string Module,
    string HttpMethod,
    string RouteTemplate,
    bool IsPublic,
    IReadOnlyList<Guid> RequiredEntitlementIds,
    [property: JsonRequired] Guid UnitOrganizationId);
