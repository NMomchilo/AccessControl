using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace AccessControl.Service.Security;

public sealed class ApiKeyMiddleware(RequestDelegate next, IOptions<ApiKeyOptions> options, ILogger<ApiKeyMiddleware> logger)
{
    public const string HeaderName = "X-Access-Control-Key";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path == "/health")
        {
            logger.LogDebug("Health request bypassed API key authentication; trace {TraceId}", context.TraceIdentifier);
            await next(context);
            return;
        }

        string expected = context.Request.Path.StartsWithSegments("/v1/decisions")
            ? options.Value.DecisionKey
            : options.Value.AdminKey;
        string provided = context.Request.Headers[HeaderName].ToString();

        if (!KeysEqual(provided, expected))
        {
            logger.LogWarning("API key rejected for {Method} {Path}; trace {TraceId}",
                context.Request.Method, context.Request.Path.Value, context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        logger.LogDebug("API key accepted for {Method} {Path}; trace {TraceId}",
            context.Request.Method, context.Request.Path.Value, context.TraceIdentifier);
        await next(context);
    }

    private static bool KeysEqual(string provided, string expected)
    {
        byte[] left = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        byte[] right = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return !string.IsNullOrEmpty(provided) && CryptographicOperations.FixedTimeEquals(left, right);
    }
}
