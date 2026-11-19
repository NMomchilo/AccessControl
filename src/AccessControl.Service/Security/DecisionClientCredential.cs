namespace AccessControl.Service.Security;

public sealed class DecisionClientCredential
{
    public string ClientId { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public bool CanRegisterCatalog { get; set; }
}
