using System.Text.Json.Serialization;

namespace AccessControl.Contracts;

public sealed record RoleEntitlementsRequest(string TenantId, Guid RoleId, IReadOnlyList<string> Entitlements,
    [property: JsonRequired] Guid UnitOrganizationId);
