namespace AccessControl.Infrastructure.Database;

public sealed class EndpointPolicyEntity
{
    public long Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public Guid UnitOrganizationId { get; set; }
    public string Module { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = string.Empty;
    public string RouteTemplate { get; set; } = string.Empty;
    public bool IsPublic { get; set; }
    public List<EndpointPolicyEntitlementEntity> RequiredEntitlements { get; set; } = [];
}
