namespace UserManagement.Contracts;

public static class UserManagementClaims
{
    public const string IdentityAuthenticationType = "UserManagement";
    public const string Role = "access_control_role";
    public const string RoleId = "access_control_role_id";
    public const string UserId = "user_management_id";
    public const string TenantId = "user_management_tenant_id";
}
