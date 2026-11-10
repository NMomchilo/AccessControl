namespace UserManagement.Client;

public sealed class UserManagementClientOptions
{
    public const string SectionName = "UserManagement";
    public string ServiceUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 5;
    public int MaximumCachedUsers { get; set; } = 10000;
    public TimeSpan CacheLifetime { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan RevisionPollInterval { get; set; } = TimeSpan.FromSeconds(1);
    public bool AllowInsecureHttpForDevelopment { get; set; }
}
