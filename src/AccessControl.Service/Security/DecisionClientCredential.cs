namespace AccessControl.Service.Security;

public sealed class DecisionClientCredential
{
    public string ClientId { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public List<string> Modules { get; set; } = [];
    public List<string> Tenants { get; set; } = [];
    public List<Guid> UnitOrganizationIds { get; set; } = [];
    public bool AllowAnyUnitOrganization { get; set; }
}
