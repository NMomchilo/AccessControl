namespace AccessControl.Service.Security;

public sealed class ApiKeyOptions
{
    public const string SectionName = "ApiKeys";
    public List<DecisionClientCredential> DecisionClients { get; set; } = [];
    public string AdminKey { get; set; } = string.Empty;

    public static bool IsValid(ApiKeyOptions options) => options.AdminKey.Length >= 32 &&
        options.DecisionClients is { Count: > 0 } && options.DecisionClients.All(client =>
            !string.IsNullOrWhiteSpace(client.ClientId) && client.ClientId.Length <= 200 &&
            client.Key.Length >= 32) &&
        options.DecisionClients.Select(client => client.Key).Append(options.AdminKey)
            .Distinct(StringComparer.Ordinal).Count() == options.DecisionClients.Count + 1;
}
