namespace AccessControl.Tests;

internal static class TestEntitlements
{
    internal static readonly Guid View = Guid.Parse("11111111-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    internal static readonly Guid Edit = Guid.Parse("22222222-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    internal static readonly Guid Delete = Guid.Parse("33333333-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    internal static readonly Guid Billing = Guid.Parse("44444444-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    internal static readonly Guid Secret = Guid.Parse("55555555-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
}
