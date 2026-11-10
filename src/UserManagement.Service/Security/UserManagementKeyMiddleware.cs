using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace UserManagement.Service.Security;

public sealed class UserManagementKeyMiddleware(RequestDelegate next,
    IOptions<UserManagementKeyOptions> options, ILogger<UserManagementKeyMiddleware> logger)
{
    public const string HeaderName = "X-User-Management-Key";

    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsGet(context.Request.Method) &&
            (context.Request.Path == "/health" || context.Request.Path == "/ready"))
        {
            await next(context);
            return;
        }

        bool resolution = (HttpMethods.IsPost(context.Request.Method) &&
            context.Request.Path == "/v1/users/resolve") ||
            (HttpMethods.IsGet(context.Request.Method) && context.Request.Path == "/v1/users/revision");
        string provided = context.Request.Headers[HeaderName].ToString();
        string? actor = resolution
            ? options.Value.Clients.FirstOrDefault(value => KeysEqual(provided, value.Key))?.ClientId
            : options.Value.Administrators.FirstOrDefault(value => KeysEqual(provided, value.Key))?.ActorId;
        if (actor is null)
        {
            logger.LogWarning("User Management credential rejected for {Method} {Path}; trace {TraceId}",
                context.Request.Method, context.Request.Path.Value, context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        context.Items["UserManagementActor"] = actor;
        logger.LogDebug("User Management credential accepted for {Actor}; trace {TraceId}", actor, context.TraceIdentifier);
        await next(context);
    }

    private static bool KeysEqual(string provided, string expected)
    {
        if (string.IsNullOrEmpty(provided))
        {
            return false;
        }

        byte[] left = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        byte[] right = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(left, right);
    }
}
