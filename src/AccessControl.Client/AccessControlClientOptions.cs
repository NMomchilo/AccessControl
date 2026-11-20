namespace AccessControl.Client;

public sealed class AccessControlClientOptions
{
    public const string SectionName = "AccessControl";
    public string ServiceUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string TenantClaimType { get; set; } = "tenant_id";
    public string DefaultTenantId { get; set; } = "default";
    public string RoleIdClaimType { get; set; } = "access_control_role_id";
    public string RoleIdentityAuthenticationType { get; set; } = "UserManagement";
    public string SuperAdminClaimType { get; set; } = "user_management_is_super_admin";
    public string UnitAdminClaimType { get; set; } = "user_management_is_unit_admin";
    public int TimeoutSeconds { get; set; } = 5;
    public string[] ExcludedPaths { get; set; } = [];
}
