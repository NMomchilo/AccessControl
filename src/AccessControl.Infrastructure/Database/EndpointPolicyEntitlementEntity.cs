using Common.Persistence;

namespace AccessControl.Infrastructure.Database;

public sealed class EndpointPolicyEntitlementEntity : GeneralEntityBase
{
    public Guid EndpointPolicyId { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public Guid UnitOrganizationId { get; set; }
    public Guid EntitlementId { get; set; }
    public EntitlementEntity Entitlement { get; set; } = null!;
    public EndpointPolicyEntity EndpointPolicy { get; set; } = null!;
}
