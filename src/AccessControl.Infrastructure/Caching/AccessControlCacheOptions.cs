namespace AccessControl.Infrastructure.Caching;

public sealed class AccessControlCacheOptions
{
    public const string SectionName = "AccessControlCache";
    public int MaximumEntries { get; set; } = 10_000;
    public TimeSpan EntryLifetime { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan RevisionPollInterval { get; set; } = TimeSpan.FromSeconds(1);
}
