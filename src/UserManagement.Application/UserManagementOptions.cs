namespace UserManagement.Application;

public sealed class UserManagementOptions
{
    public const string SectionName = "UserManagementRules";
    public int MaximumRolesPerUser { get; set; } = 16;
    public int MaximumRoleLength { get; set; } = 64;
    public int MaximumEmailLength { get; set; } = 320;
    public int MaximumDisplayNameLength { get; set; } = 200;

    public static bool IsValid(UserManagementOptions options) =>
        options.MaximumRolesPerUser is > 0 and <= 16 &&
        options.MaximumRoleLength is > 0 and <= 64 &&
        options.MaximumEmailLength is > 0 and <= 320 &&
        options.MaximumDisplayNameLength is > 0 and <= 200;
}
