namespace AccessControl.Client;

public sealed class AccessControlClientOptions
{
    public const string SectionName = "AccessControl";
    public string ServiceUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string TenantClaimType { get; set; } = "tenant_id";
    public string DefaultTenantId { get; set; } = "default";
    public int TimeoutSeconds { get; set; } = 5;
    public string[] ExcludedPaths { get; set; } = [];
    public string? CatalogOutputPath { get; set; }
}
