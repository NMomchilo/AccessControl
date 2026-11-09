namespace AccessControl.Infrastructure.Database;

public sealed class EndpointPolicyEntitlementEntity
{
    public long Id { get; set; }
    public long EndpointPolicyId { get; set; }
    public string Entitlement { get; set; } = string.Empty;
    public EndpointPolicyEntity EndpointPolicy { get; set; } = null!;
}
