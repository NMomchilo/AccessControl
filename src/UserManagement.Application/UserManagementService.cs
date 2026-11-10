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
        string tenantId = NormalizeTenant(request.TenantId);
        string objectId = NormalizeObjectId(request.ObjectId);
        string? email = NormalizeOptional(request.Email, options.Value.MaximumEmailLength, nameof(request.Email));
        string? displayName = NormalizeOptional(request.DisplayName, options.Value.MaximumDisplayNameLength,
            nameof(request.DisplayName));
        ManagedUser user = await store.ResolveAsync(tenantId, objectId, email, displayName, cancellationToken);
        logger.LogDebug("Resolved user {UserId}; active {IsActive}, roles {RoleCount}", user.Id, user.IsActive, user.Roles.Count);
        return Map(user);
    }

    public async Task<UserIdentityResponse?> GetAsync(string tenantId, string objectId, CancellationToken cancellationToken)
    {
        ManagedUser? user = await store.GetAsync(NormalizeTenant(tenantId), NormalizeObjectId(objectId), cancellationToken);
        return user is null ? null : Map(user);
    }

    public async Task<bool> ReplaceRolesAsync(string tenantId, string objectId, ReplaceRolesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RoleIds is null || request.RoleIds.Count > options.Value.MaximumRolesPerUser ||
            request.RoleIds.Any(id => id == Guid.Empty))
        {
            throw new ArgumentException("Role IDs are required, nonempty, and within the configured limit.", nameof(request));
        }

        Guid[] roles = request.RoleIds.Distinct().ToArray();
        bool found = await store.ReplaceRolesAsync(NormalizeTenant(tenantId), NormalizeObjectId(objectId), roles, cancellationToken);
        logger.LogInformation("Role replacement {Outcome} for {ObjectId}; role count {RoleCount}",
            found ? "saved" : "not found", objectId, roles.Length);
        return found;
    }

    public async Task<bool> SetActiveAsync(string tenantId, string objectId, SetActiveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        bool found = await store.SetActiveAsync(NormalizeTenant(tenantId), NormalizeObjectId(objectId), request.IsActive, cancellationToken);
        logger.LogInformation("User active status change {Outcome} for {ObjectId}; active {IsActive}",
            found ? "saved" : "not found", objectId, request.IsActive);
        return found;
    }

    public Task<long> GetRevisionAsync(CancellationToken cancellationToken) =>
        store.GetRevisionAsync(cancellationToken);

    public async Task<RoleInfo> CreateRoleAsync(CreateRoleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string tenantId = NormalizeTenant(request.TenantId);
        string code = NormalizeRequired(request.Code, options.Value.MaximumRoleLength, nameof(request.Code));
        string displayName = NormalizeOptional(request.DisplayName, options.Value.MaximumDisplayNameLength,
            nameof(request.DisplayName)) ?? throw new ArgumentException("Display name is required.", nameof(request));
        ManagedRole role = await store.CreateRoleAsync(tenantId, code, displayName, cancellationToken);
        logger.LogInformation("Created role {RoleId} in tenant {TenantId}", role.Id, tenantId);
        return new RoleInfo(role.Id, role.TenantId, role.Code, role.DisplayName);
    }

    public async Task<IReadOnlyList<RoleInfo>> GetRolesAsync(string tenantId, CancellationToken cancellationToken)
    {
        IReadOnlyList<ManagedRole> roles = await store.GetRolesAsync(NormalizeTenant(tenantId), cancellationToken);
        return roles.Select(role => new RoleInfo(role.Id, role.TenantId, role.Code, role.DisplayName)).ToArray();
    }

    private static UserIdentityResponse Map(ManagedUser user) => new(user.Id, user.TenantId, user.IsActive,
        user.Roles.Select(role => new UserRoleInfo(role.Id, role.Code, role.DisplayName)).ToArray());

    private static string NormalizeTenant(string tenantId) => NormalizeRequired(tenantId, 128, nameof(tenantId));

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
