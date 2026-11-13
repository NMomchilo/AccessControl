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
        Guid organizationId = NormalizeOrganizationId(request.UnitOrganizationId);
        string organizationName = NormalizeOrganizationName(request.UnitOrganizationName);
        string objectId = NormalizeObjectId(request.ObjectId);
        string? email = NormalizeOptional(request.Email, options.Value.MaximumEmailLength, nameof(request.Email));
        string? displayName = NormalizeOptional(request.DisplayName, options.Value.MaximumDisplayNameLength,
            nameof(request.DisplayName));
        ManagedUser user = await store.ResolveAsync(tenantId, organizationId, organizationName,
            objectId, email, displayName, cancellationToken);
        logger.LogDebug("Resolved user {UserId}; active {IsActive}, roles {RoleCount}", user.Id, user.IsActive, user.Roles.Count);
        return Map(user);
    }

    public async Task<UserIdentityResponse?> GetAsync(string tenantId, Guid unitOrganizationId, string objectId, CancellationToken cancellationToken)
    {
        ManagedUser? user = await store.GetAsync(NormalizeTenant(tenantId), NormalizeOrganizationId(unitOrganizationId),
            NormalizeObjectId(objectId), cancellationToken);
        return user is null ? null : Map(user);
    }

    public async Task<PagedResult<UserSummary>?> GetUsersPageAsync(string tenantId, Guid? unitOrganizationId,
        int page, int pageSize, CancellationToken cancellationToken, string? email = null)
    {
        string normalizedTenant = NormalizeTenant(tenantId);
        int offset = ValidatePage(page, pageSize);
        if (unitOrganizationId.HasValue && await store.GetUnitOrganizationAsync(normalizedTenant,
                NormalizeOrganizationId(unitOrganizationId.Value), cancellationToken) is null)
            return null;
        string? normalizedEmail = email is null ? null : NormalizeRequired(email, 320, nameof(email)).ToLowerInvariant();
        PageSlice<ManagedUserSummary> slice = await store.GetUsersAsync(normalizedTenant, unitOrganizationId,
            offset, pageSize, normalizedEmail, cancellationToken);
        logger.LogDebug("Listed users in tenant {TenantId}, organization {UnitOrganizationId}; page {Page}, count {Count}",
            normalizedTenant, unitOrganizationId, page, slice.Items.Count);
        return new PagedResult<UserSummary>(slice.Items.Select(user => new UserSummary(user.Id, user.ObjectId,
            user.DisplayName, user.Email, user.IsActive, user.Memberships.Select(membership =>
                new UserMembershipSummary(membership.UnitOrganizationId, membership.UnitOrganizationName,
                    membership.Roles.Select(role => new UserRoleAssignmentInfo(role.Id, role.Code,
                        role.DisplayName, role.IsActive)).ToArray())).ToArray())).ToArray(), slice.TotalCount, page, pageSize);
    }

    public async Task<bool> ReplaceRolesAsync(string tenantId, Guid unitOrganizationId, string objectId,
        ReplaceRolesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RoleIds is null || request.RoleIds.Count > options.Value.MaximumRolesPerUser ||
            request.RoleIds.Any(id => id == Guid.Empty))
        {
            throw new ArgumentException("Role IDs are required, nonempty, and within the configured limit.", nameof(request));
        }

        Guid[] roles = request.RoleIds.Distinct().ToArray();
        bool found = await store.ReplaceRolesAsync(NormalizeTenant(tenantId), NormalizeOrganizationId(unitOrganizationId),
            NormalizeObjectId(objectId), roles, cancellationToken);
        logger.LogInformation("Role replacement {Outcome} in organization {UnitOrganizationId}; role count {RoleCount}",
            found ? "saved" : "not found", unitOrganizationId, roles.Length);
        return found;
    }

    public async Task<bool> SetActiveAsync(string tenantId, string objectId, SetActiveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        bool found = await store.SetActiveAsync(NormalizeTenant(tenantId), NormalizeObjectId(objectId), request.IsActive, cancellationToken);
        logger.LogInformation("User active status change {Outcome}; active {IsActive}",
            found ? "saved" : "not found", request.IsActive);
        return found;
    }

    public Task<long> GetRevisionAsync(CancellationToken cancellationToken) =>
        store.GetRevisionAsync(cancellationToken);

    public async Task<RoleInfo> CreateRoleAsync(CreateRoleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string tenantId = NormalizeTenant(request.TenantId);
        Guid organizationId = NormalizeOrganizationId(request.UnitOrganizationId);
        string code = NormalizeRequired(request.Code, options.Value.MaximumRoleLength, nameof(request.Code));
        string displayName = NormalizeOptional(request.DisplayName, options.Value.MaximumDisplayNameLength,
            nameof(request.DisplayName)) ?? throw new ArgumentException("Display name is required.", nameof(request));
        ManagedRole role = await store.CreateRoleAsync(tenantId, organizationId, code, displayName, cancellationToken);
        logger.LogInformation("Created role {RoleId} in tenant {TenantId}", role.Id, tenantId);
        return new RoleInfo(role.Id, role.TenantId, role.UnitOrganizationId, role.Code, role.DisplayName, role.IsActive);
    }

    public async Task<bool> SetRoleActiveAsync(string tenantId, Guid unitOrganizationId, Guid roleId,
        SetActiveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (roleId == Guid.Empty) throw new ArgumentException("Role ID is required.", nameof(roleId));
        bool found = await store.SetRoleActiveAsync(NormalizeTenant(tenantId), NormalizeOrganizationId(unitOrganizationId),
            roleId, request.IsActive, cancellationToken);
        logger.LogInformation("Role {RoleId} active status set to {IsActive}; found {Found}", roleId, request.IsActive, found);
        return found;
    }

    public async Task<PagedResult<RoleInfo>?> GetRolesPageAsync(string tenantId, Guid? unitOrganizationId,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        string normalizedTenant = NormalizeTenant(tenantId);
        int offset = ValidatePage(page, pageSize);
        if (unitOrganizationId.HasValue && await store.GetUnitOrganizationAsync(normalizedTenant,
                NormalizeOrganizationId(unitOrganizationId.Value), cancellationToken) is null)
            return null;
        PageSlice<ManagedRole> slice = await store.GetRolesPageAsync(normalizedTenant, unitOrganizationId,
            offset, pageSize, cancellationToken);
        logger.LogDebug("Listed roles in tenant {TenantId}, organization {UnitOrganizationId}; page {Page}, count {Count}",
            normalizedTenant, unitOrganizationId, page, slice.Items.Count);
        return new PagedResult<RoleInfo>(slice.Items.Select(role => new RoleInfo(role.Id, role.TenantId,
            role.UnitOrganizationId, role.Code, role.DisplayName, role.IsActive)).ToArray(), slice.TotalCount, page, pageSize);
    }

    public async Task<RoleInfo?> GetRoleAsync(string tenantId, Guid unitOrganizationId, Guid roleId,
        CancellationToken cancellationToken)
    {
        if (roleId == Guid.Empty) throw new ArgumentException("Role ID is required.", nameof(roleId));
        ManagedRole? role = await store.GetRoleAsync(NormalizeTenant(tenantId),
            NormalizeOrganizationId(unitOrganizationId), roleId, cancellationToken);
        return role is null ? null : new RoleInfo(role.Id, role.TenantId, role.UnitOrganizationId,
            role.Code, role.DisplayName, role.IsActive);
    }

    public async Task<UnitOrganizationInfo> CreateUnitOrganizationAsync(CreateUnitOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ManagedUnitOrganization organization = await store.CreateUnitOrganizationAsync(
            NormalizeTenant(request.TenantId), NormalizeOrganizationId(request.Id),
            NormalizeOrganizationName(request.Name), cancellationToken);
        logger.LogInformation("Administrator created organization {UnitOrganizationId} in tenant {TenantId}",
            organization.Id, organization.TenantId);
        return Map(organization);
    }

    public async Task<UnitOrganizationInfo?> GetUnitOrganizationAsync(string tenantId, Guid id,
        CancellationToken cancellationToken)
    {
        ManagedUnitOrganization? organization = await store.GetUnitOrganizationAsync(NormalizeTenant(tenantId),
            NormalizeOrganizationId(id), cancellationToken);
        return organization is null ? null : Map(organization);
    }

    public async Task<IReadOnlyList<UnitOrganizationInfo>> GetUnitOrganizationsAsync(string tenantId,
        CancellationToken cancellationToken) =>
        (await store.GetUnitOrganizationsAsync(NormalizeTenant(tenantId), cancellationToken))
            .Select(Map).ToArray();

    public async Task<UnitOrganizationInfo?> UpdateUnitOrganizationAsync(string tenantId, Guid id,
        UpdateUnitOrganizationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ManagedUnitOrganization? organization = await store.UpdateUnitOrganizationAsync(NormalizeTenant(tenantId),
            NormalizeOrganizationId(id), NormalizeOrganizationName(request.Name), cancellationToken);
        if (organization is not null)
        {
            logger.LogInformation("Administrator updated organization {UnitOrganizationId} in tenant {TenantId}",
                organization.Id, organization.TenantId);
        }
        return organization is null ? null : Map(organization);
    }

    public async Task<bool> DeleteUnitOrganizationAsync(string tenantId, Guid id, CancellationToken cancellationToken)
    {
        bool deleted = await store.DeleteUnitOrganizationAsync(NormalizeTenant(tenantId),
            NormalizeOrganizationId(id), cancellationToken);
        logger.LogInformation("Administrator organization delete outcome {Deleted} for {UnitOrganizationId}",
            deleted, id);
        return deleted;
    }

    private static UserIdentityResponse Map(ManagedUser user) => new(user.Id, user.TenantId,
        user.UnitOrganizationId, user.UnitOrganizationName, user.IsActive,
        user.Roles.Select(role => new UserRoleInfo(role.Id, role.Code, role.DisplayName)).ToArray());

    private static UnitOrganizationInfo Map(ManagedUnitOrganization organization) =>
        new(organization.Id, organization.TenantId, organization.Name);

    private static Guid NormalizeOrganizationId(Guid id) => id == Guid.Empty
        ? throw new ArgumentException("Unit organization ID must be a nonempty GUID.", nameof(id)) : id;

    private static int ValidatePage(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100 || ((long)page - 1) * pageSize > int.MaxValue)
            throw new ArgumentException("Page must be at least 1, pageSize must be 1 through 100, and the offset must fit an integer.");
        return (page - 1) * pageSize;
    }

    private static string NormalizeOrganizationName(string name) =>
        NormalizeOptional(name, 200, nameof(name)) ??
        throw new ArgumentException("Unit organization name is required.", nameof(name));

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
