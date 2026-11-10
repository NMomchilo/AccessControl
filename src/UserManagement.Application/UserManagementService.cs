using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UserManagement.Contracts;
using UserManagement.Domain;

namespace UserManagement.Application;

public sealed class UserManagementService(IUserStore store, IOptions<UserManagementOptions> options,
    ILogger<UserManagementService> logger)
{
    public async Task<UserIdentityResponse> ResolveAsync(UserIdentityRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string objectId = NormalizeObjectId(request.ObjectId);
        string? email = NormalizeOptional(request.Email, options.Value.MaximumEmailLength, nameof(request.Email));
        string? displayName = NormalizeOptional(request.DisplayName, options.Value.MaximumDisplayNameLength,
            nameof(request.DisplayName));
        ManagedUser user = await store.ResolveAsync(objectId, email, displayName, cancellationToken);
        logger.LogDebug("Resolved user {UserId}; active {IsActive}, roles {RoleCount}", user.Id, user.IsActive, user.Roles.Count);
        return new UserIdentityResponse(user.Id, user.IsActive, user.Roles);
    }

    public async Task<UserIdentityResponse?> GetAsync(string objectId, CancellationToken cancellationToken)
    {
        ManagedUser? user = await store.GetAsync(NormalizeObjectId(objectId), cancellationToken);
        return user is null ? null : new UserIdentityResponse(user.Id, user.IsActive, user.Roles);
    }

    public async Task<bool> ReplaceRolesAsync(string objectId, ReplaceRolesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Roles is null || request.Roles.Count > options.Value.MaximumRolesPerUser)
        {
            throw new ArgumentException("Role count exceeds the configured limit.", nameof(request));
        }

        string[] roles = request.Roles.Select(value => NormalizeRequired(value, options.Value.MaximumRoleLength, "role"))
            .Distinct(StringComparer.Ordinal).ToArray();
        bool found = await store.ReplaceRolesAsync(NormalizeObjectId(objectId), roles, cancellationToken);
        logger.LogInformation("Role replacement {Outcome} for {ObjectId}; role count {RoleCount}",
            found ? "saved" : "not found", objectId, roles.Length);
        return found;
    }

    public async Task<bool> SetActiveAsync(string objectId, SetActiveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        bool found = await store.SetActiveAsync(NormalizeObjectId(objectId), request.IsActive, cancellationToken);
        logger.LogInformation("User active status change {Outcome} for {ObjectId}; active {IsActive}",
            found ? "saved" : "not found", objectId, request.IsActive);
        return found;
    }

    public Task<long> GetRevisionAsync(CancellationToken cancellationToken) =>
        store.GetRevisionAsync(cancellationToken);

    private static string NormalizeObjectId(string value)
    {
        if (!Guid.TryParse(value, out Guid objectId) || objectId == Guid.Empty)
        {
            throw new ArgumentException("ObjectId must be a nonempty GUID.", nameof(value));
        }

        return objectId.ToString("D");
    }

    private static string NormalizeRequired(string value, int maxLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maxLength)
        {
            throw new ArgumentException($"{name} is required and must be at most {maxLength} characters.", name);
        }

        return value.Trim().ToLowerInvariant();
    }

    private static string? NormalizeOptional(string? value, int maxLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException($"{name} must be at most {maxLength} characters.", name);
        }

        return trimmed;
    }
}
