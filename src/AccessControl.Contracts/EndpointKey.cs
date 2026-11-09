namespace AccessControl.Contracts;

public static class EndpointKey
{
    public static string NormalizeMethod(string method) => method.Trim().ToUpperInvariant();

    public static string NormalizeRoute(string route)
    {
        string value = route.Trim().Trim('/').ToLowerInvariant();
        return value.Length == 0 ? "/" : $"/{value}";
    }
}
