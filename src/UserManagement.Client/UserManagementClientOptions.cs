namespace UserManagement.Client;

public sealed class UserManagementClientOptions
{
    public const string SectionName = "UserManagement";
    public string ServiceUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string TenantClaimType { get; set; } = "tenant_id";
    public string DefaultTenantId { get; set; } = "default";
    public string GatewayIdentityAuthenticationType { get; set; } = "Gateway";
    public string UnitOrganizationIdClaimType { get; set; } = "unit_organization_id";
    public string UnitOrganizationNameClaimType { get; set; } = "unit_organization_name";
    public string RequiredDelegatedScope { get; set; } = "user_impersonation";
    public int TimeoutSeconds { get; set; } = 5;
    public int MaximumCachedUsers { get; set; } = 10000;
    public TimeSpan CacheLifetime { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan RevisionPollInterval { get; set; } = TimeSpan.FromSeconds(1);
    public bool AllowInsecureHttpForDevelopment { get; set; }
}
