namespace AccessControl.Infrastructure.Database;

public sealed class PolicyEntity
{
    public long Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string Resource { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Entitlement { get; set; } = string.Empty;
}
