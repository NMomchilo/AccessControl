using AccessControl.Contracts;
using AccessControl.Domain;
using Microsoft.Extensions.Logging;

namespace AccessControl.Application;

public sealed class AuthorizationService(IAuthorizationStore store, ILogger<AuthorizationService> logger)
{
    public async Task<PolicyRequest?> GetPolicyAsync(string tenantId, Guid unitOrganizationId, string module, string httpMethod, string routeTemplate, CancellationToken cancellationToken)
    {
        PolicyKeyRequest key = NormalizeKey(new PolicyKeyRequest(tenantId, module, httpMethod, routeTemplate, unitOrganizationId));
        EndpointPolicy? policy = await store.GetPolicyAsync(key.TenantId, key.UnitOrganizationId, key.Module, key.HttpMethod, key.RouteTemplate, cancellationToken);
        logger.LogDebug("Endpoint policy lookup for {TenantId} {UnitOrganizationId} {Module} {HttpMethod} {RouteTemplate}: {Found}",
            key.TenantId, key.UnitOrganizationId, key.Module, key.HttpMethod, key.RouteTemplate, policy is not null);
        return policy is null ? null : new PolicyRequest(policy.TenantId, policy.Module, policy.HttpMethod,
            policy.RouteTemplate, policy.IsPublic, policy.RequiredEntitlements, policy.UnitOrganizationId);
    }

    public async Task<RoleEntitlementsRequest> GetRoleEntitlementsAsync(string tenantId, Guid unitOrganizationId, Guid roleId, CancellationToken cancellationToken)
    {
        string normalizedTenant = NormalizeName(tenantId, nameof(tenantId));
        if (roleId == Guid.Empty) throw new ArgumentException("Role ID is required.", nameof(roleId));
        if (unitOrganizationId == Guid.Empty) throw new ArgumentException("Unit organization ID is required for role grants.", nameof(unitOrganizationId));
        IReadOnlySet<string> entitlements = await store.GetEntitlementsAsync(normalizedTenant, unitOrganizationId, [roleId], cancellationToken);
        logger.LogDebug("Read {Count} entitlements for role {RoleId} in tenant {TenantId}, organization {UnitOrganizationId}",
            entitlements.Count, roleId, normalizedTenant, unitOrganizationId);
        return new RoleEntitlementsRequest(normalizedTenant, roleId, entitlements.Order(StringComparer.Ordinal).ToList(), unitOrganizationId);
    }

    public async Task<DecisionResponse> DecideAsync(DecisionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.TenantId) || string.IsNullOrWhiteSpace(request.Module) ||
            string.IsNullOrWhiteSpace(request.HttpMethod) || string.IsNullOrWhiteSpace(request.RouteTemplate) ||
            request.RoleIds is null || request.RoleIds.Count > 16 || request.RoleIds.Any(id => id == Guid.Empty))
        {
            logger.LogWarning("Rejected invalid decision request");
            return new DecisionResponse(false, "Invalid authorization request");
        }

        PolicyKeyRequest key;

        try
        {
            key = NormalizeKey(new PolicyKeyRequest(request.TenantId, request.Module, request.HttpMethod, request.RouteTemplate, request.UnitOrganizationId));
        }
        catch (ArgumentException)
        {
            logger.LogWarning("Rejected decision request with invalid endpoint key");
            return new DecisionResponse(false, "Invalid authorization request");
        }

        logger.LogDebug("Evaluating endpoint {TenantId} {UnitOrganizationId} {Module} {HttpMethod} {RouteTemplate} with {RoleCount} roles",
            key.TenantId, key.UnitOrganizationId, key.Module, key.HttpMethod, key.RouteTemplate, request.RoleIds.Count);
        EndpointPolicy? policy = await store.GetPolicyAsync(key.TenantId, key.UnitOrganizationId, key.Module, key.HttpMethod, key.RouteTemplate, cancellationToken);
        if (policy is null && key.UnitOrganizationId != Guid.Empty)
        {
            EndpointPolicy? global = await store.GetPolicyAsync(key.TenantId, Guid.Empty, key.Module, key.HttpMethod,
                key.RouteTemplate, cancellationToken);
            policy = global?.IsPublic == true ? global : null;
        }

        if (policy is null)
        {
            logger.LogWarning("No policy for endpoint {TenantId} {UnitOrganizationId} {Module} {HttpMethod} {RouteTemplate}",
                key.TenantId, key.UnitOrganizationId, key.Module, key.HttpMethod, key.RouteTemplate);
            return new DecisionResponse(false, "No endpoint policy", string.IsNullOrWhiteSpace(request.SubjectId));
        }

        if (policy.IsPublic)
        {
            logger.LogInformation("Public endpoint permitted: {TenantId} {Module} {HttpMethod} {RouteTemplate}",
                key.TenantId, key.Module, key.HttpMethod, key.RouteTemplate);
            return new DecisionResponse(true, "Public endpoint");
        }

        if (string.IsNullOrWhiteSpace(request.SubjectId))
        {
            logger.LogInformation("Endpoint requires authentication: {TenantId} {Module} {HttpMethod} {RouteTemplate}",
                key.TenantId, key.Module, key.HttpMethod, key.RouteTemplate);
            return new DecisionResponse(false, "Authentication required", true);
        }

        if (request.UnitOrganizationId == Guid.Empty)
        {
            logger.LogWarning("Protected decision has no unit organization; trace {TenantId} {Module}", key.TenantId, key.Module);
            return new DecisionResponse(false, "Unit organization required");
        }

        Guid[] roleIds = request.RoleIds.Distinct().ToArray();
        IReadOnlySet<string> entitlements = await store.GetEntitlementsAsync(key.TenantId, key.UnitOrganizationId, roleIds, cancellationToken);
        bool permit = policy.RequiredEntitlements.Any(entitlements.Contains);
        logger.LogInformation("Endpoint decision {Decision} for {TenantId} {UnitOrganizationId} {Module} {HttpMethod} {RouteTemplate}; {RoleCount} roles, {GrantCount} grants",
            permit ? "Permit" : "Deny", key.TenantId, key.UnitOrganizationId, key.Module, key.HttpMethod, key.RouteTemplate,
            roleIds.Length, entitlements.Count);
        return new DecisionResponse(permit, permit ? "Permit" : "Required entitlement missing");
    }

    public async Task UpsertPolicyAsync(PolicyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        PolicyKeyRequest key = NormalizeKey(new PolicyKeyRequest(request.TenantId, request.Module, request.HttpMethod, request.RouteTemplate, request.UnitOrganizationId));
        if (!request.IsPublic && key.UnitOrganizationId == Guid.Empty)
        {
            throw new ArgumentException("Protected policies require a unit organization ID.", nameof(request));
        }

        if (request.RequiredEntitlements is null ||
            (request.IsPublic && request.RequiredEntitlements.Count != 0) ||
            (!request.IsPublic && request.RequiredEntitlements.Count == 0))
        {
            throw new ArgumentException("Public policies have no entitlements; protected policies require at least one entitlement.", nameof(request));
        }

        string[] entitlements = request.RequiredEntitlements.Select(value => NormalizeName(value, "entitlement"))
            .Distinct(StringComparer.Ordinal).ToArray();
        await store.UpsertPolicyAsync(new EndpointPolicy(key.TenantId, key.Module, key.HttpMethod,
            key.RouteTemplate, request.IsPublic, entitlements, key.UnitOrganizationId), cancellationToken);
        logger.LogInformation("Saved endpoint policy {TenantId} {Module} {HttpMethod} {RouteTemplate}; public {IsPublic}, entitlements {Count}",
            key.TenantId, key.Module, key.HttpMethod, key.RouteTemplate, request.IsPublic, entitlements.Length);
    }

    public async Task DeletePolicyAsync(PolicyKeyRequest request, CancellationToken cancellationToken)
    {
        PolicyKeyRequest key = NormalizeKey(request);
        await store.DeletePolicyAsync(key.TenantId, key.UnitOrganizationId, key.Module, key.HttpMethod, key.RouteTemplate, cancellationToken);
        logger.LogInformation("Deleted endpoint policy {TenantId} {Module} {HttpMethod} {RouteTemplate}",
            key.TenantId, key.Module, key.HttpMethod, key.RouteTemplate);
    }

    public async Task ReplaceRoleEntitlementsAsync(RoleEntitlementsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string tenantId = NormalizeName(request.TenantId, nameof(request.TenantId));
        Guid roleId = request.RoleId != Guid.Empty ? request.RoleId :
            throw new ArgumentException("Role ID is required.", nameof(request));
        if (request.UnitOrganizationId == Guid.Empty)
            throw new ArgumentException("Unit organization ID is required for role grants.", nameof(request));
        IReadOnlyList<string> values = request.Entitlements ?? throw new ArgumentException("Entitlements are required.", nameof(request));
        string[] entitlements = values.Select(value => NormalizeName(value, "entitlement"))
            .Distinct(StringComparer.Ordinal).ToArray();
        await store.ReplaceRoleEntitlementsAsync(tenantId, request.UnitOrganizationId, roleId, entitlements, cancellationToken);
        logger.LogInformation("Replaced entitlements for role {RoleId} in tenant {TenantId}; count {Count}", roleId, tenantId, entitlements.Length);
    }

    private static PolicyKeyRequest NormalizeKey(PolicyKeyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string tenantId = NormalizeName(request.TenantId, nameof(request.TenantId));
        string module = NormalizeName(request.Module, nameof(request.Module));
        string method = EndpointKey.NormalizeMethod(NormalizeName(request.HttpMethod, nameof(request.HttpMethod)));
        if (string.IsNullOrWhiteSpace(request.RouteTemplate))
        {
            throw new ArgumentException("Route template is required.", nameof(request));
        }
        string route = EndpointKey.NormalizeRoute(request.RouteTemplate);

        if (method.Length > 16 || route.Length > 512)
        {
            throw new ArgumentException("Endpoint method or route is too long.", nameof(request));
        }

        return new PolicyKeyRequest(tenantId, module, method, route, request.UnitOrganizationId);
    }

    private static string NormalizeName(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 128)
        {
            throw new ArgumentException($"{field} is required and must be 128 characters or less.", field);
        }

        return value.Trim().ToLowerInvariant();
    }
}
