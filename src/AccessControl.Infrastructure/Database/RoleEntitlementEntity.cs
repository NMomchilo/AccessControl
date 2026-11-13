namespace AccessControl.Infrastructure.Database;

public sealed class RoleEntitlementEntity
{
    public long Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public Guid UnitOrganizationId { get; set; }
    public Guid RoleId { get; set; }
    public Guid EntitlementId { get; set; }
    public EntitlementEntity Entitlement { get; set; } = null!;
}
