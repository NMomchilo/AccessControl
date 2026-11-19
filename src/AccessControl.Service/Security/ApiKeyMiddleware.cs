using System.Security.Cryptography;
using AdminManagement.Security;
using System.Text;
using System.Text.Json;
using AccessControl.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AccessControl.Service.Security;

public sealed class ApiKeyMiddleware(RequestDelegate next, IOptions<ApiKeyOptions> options, ILogger<ApiKeyMiddleware> logger)
{
    public const string HeaderName = "X-Access-Control-Key";

    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsGet(context.Request.Method) &&
            (context.Request.Path == "/health" || context.Request.Path == "/ready"))
        {
            logger.LogDebug("Probe request bypassed API key authentication for {Path}; trace {TraceId}",
                context.Request.Path, context.TraceIdentifier);
            await next(context);
            return;
        }

        bool decision = HttpMethods.IsPost(context.Request.Method) &&
            context.Request.Path == "/v1/decisions";
        bool registration = HttpMethods.IsPost(context.Request.Method) &&
            context.Request.Path == "/v1/registrations/catalog";
        string provided = context.Request.Headers[HeaderName].ToString();
        DecisionClientCredential? decisionClient = decision || registration
            ? options.Value.DecisionClients.FirstOrDefault(value => KeysEqual(provided, value.Key)) : null;
        if (decision || registration ? decisionClient is null : !KeysEqual(provided, options.Value.AdminKey))
        {
            logger.LogWarning("API key rejected for {Method} {Path}; trace {TraceId}",
                context.Request.Method, context.Request.Path.Value, context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (registration)
        {
            if (decisionClient is not { CanRegisterCatalog: true })
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            context.Items[nameof(DecisionClientCredential)] = decisionClient;
            context.User = RegistrationClientIdentity.Create(decisionClient.ClientId);
        }
        else if (decision)
        {
            DecisionClientCredential scopedClient = decisionClient!;
            if (context.Request.ContentLength > 16 * 1024)
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                return;
            }
            DecisionRequest? request;
            try
            {
                context.Request.EnableBuffering(8192, 16 * 1024);
                request = await JsonSerializer.DeserializeAsync<DecisionRequest>(context.Request.Body,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web), context.RequestAborted);
                context.Request.Body.Position = 0;
            }
            catch (Exception exception) when (exception is JsonException or IOException)
            {
                logger.LogWarning("Malformed decision payload from client {ClientId}; trace {TraceId}",
                    scopedClient.ClientId, context.TraceIdentifier);
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            if (request is null)
            {
                logger.LogWarning("Empty decision payload from client {ClientId}; trace {TraceId}",
                    scopedClient.ClientId, context.TraceIdentifier);
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            if (!string.IsNullOrWhiteSpace(request.SubjectId))
            {
                AuthenticateResult authenticated = await context.AuthenticateAsync(
                    context.RequestServices.GetRequiredService<IOptions<DecisionEntraOptions>>().Value.AuthenticationScheme);
                string? oid = authenticated.Principal?.FindFirst("oid")?.Value ??
                    authenticated.Principal?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
                if (!authenticated.Succeeded || !Guid.TryParse(oid, out Guid verifiedOid) ||
                    !Guid.TryParse(request.SubjectId, out Guid requestedOid) || verifiedOid == Guid.Empty ||
                    requestedOid != verifiedOid)
                {
                    logger.LogWarning("Decision identity rejected for client {ClientId}; trace {TraceId}",
                        scopedClient.ClientId, context.TraceIdentifier);
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
                context.User = authenticated.Principal!;
            }
        }
        else if (context.User.Identity?.IsAuthenticated != true ||
             !Guid.TryParse(context.User.FindFirst("oid")?.Value ??
                 context.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value,
                 out Guid objectId) || objectId == Guid.Empty)
        {
            logger.LogWarning("Access Control administrator request lacks a validated actor; trace {TraceId}",
                context.TraceIdentifier);
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
