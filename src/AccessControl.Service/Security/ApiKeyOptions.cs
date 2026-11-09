namespace AccessControl.Service.Security;

public sealed class ApiKeyOptions
{
    public const string SectionName = "ApiKeys";
    public string DecisionKey { get; set; } = string.Empty;
    public string AdminKey { get; set; } = string.Empty;
}
