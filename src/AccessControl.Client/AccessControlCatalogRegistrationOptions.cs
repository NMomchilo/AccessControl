namespace AccessControl.Client;

public sealed class AccessControlCatalogRegistrationOptions
{
    public string TenantId { get; set; } = string.Empty;
    public Guid UnitOrganizationId { get; set; }
    public string UnitOrganizationName { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
}
