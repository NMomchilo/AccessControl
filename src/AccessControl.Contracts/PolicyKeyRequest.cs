using System.Text.Json.Serialization;

namespace AccessControl.Contracts;

public sealed record PolicyKeyRequest(string TenantId, string Module, string HttpMethod, string RouteTemplate,
    [property: JsonRequired] Guid UnitOrganizationId);
