namespace AccessControl.Infrastructure.Database;

public sealed class RoleEntitlementEntity
{
    public long Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public Guid RoleId { get; set; }
    public string Entitlement { get; set; } = string.Empty;
}
