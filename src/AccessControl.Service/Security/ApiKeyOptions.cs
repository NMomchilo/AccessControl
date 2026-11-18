namespace AccessControl.Service.Security;

public sealed class ApiKeyOptions
{
    public const string SectionName = "ApiKeys";
    public List<DecisionClientCredential> DecisionClients { get; set; } = [];
    public string AdminKey { get; set; } = string.Empty;

    public static bool IsValid(ApiKeyOptions options) => options.AdminKey.Length >= 32 &&
        options.DecisionClients is { Count: > 0 } && options.DecisionClients.All(client =>
            !string.IsNullOrWhiteSpace(client.ClientId) && client.Key.Length >= 32 &&
            client.Modules is { Count: > 0 } && client.Tenants is { Count: > 0 } &&
            client.UnitOrganizationIds is not null &&
            (client.AllowAnyUnitOrganization || client.UnitOrganizationIds.Count > 0) &&
            client.Modules.All(module => !string.IsNullOrWhiteSpace(module)) &&
            client.Tenants.All(tenant => !string.IsNullOrWhiteSpace(tenant))) &&
        options.DecisionClients.Select(client => client.Key).Append(options.AdminKey)
            .Distinct(StringComparer.Ordinal).Count() == options.DecisionClients.Count + 1;
}
