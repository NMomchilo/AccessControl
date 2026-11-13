namespace AccessControl.Infrastructure.Database;

public sealed class EntitlementEntity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public Guid UnitOrganizationId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public List<RoleEntitlementEntity> RoleGrants { get; set; } = [];
    public List<EndpointPolicyEntitlementEntity> PolicyRequirements { get; set; } = [];
}
