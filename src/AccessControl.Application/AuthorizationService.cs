using AccessControl.Contracts;
using AccessControl.Domain;
using Microsoft.Extensions.Logging;

namespace AccessControl.Application;

public sealed class AuthorizationService(IAuthorizationStore store, ILogger<AuthorizationService> logger)
{
    public async Task<PolicyRequest?> GetPolicyAsync(string tenantId, string module, string httpMethod, string routeTemplate, CancellationToken cancellationToken)
    {
        PolicyKeyRequest key = NormalizeKey(new PolicyKeyRequest(tenantId, module, httpMethod, routeTemplate));
        EndpointPolicy? policy = await store.GetPolicyAsync(key.TenantId, key.Module, key.HttpMethod, key.RouteTemplate, cancellationToken);
        logger.LogDebug("Endpoint policy lookup for {TenantId} {Module} {HttpMethod} {RouteTemplate}: {Found}",
            key.TenantId, key.Module, key.HttpMethod, key.RouteTemplate, policy is not null);
        return policy is null ? null : new PolicyRequest(policy.TenantId, policy.Module, policy.HttpMethod,
            policy.RouteTemplate, policy.IsPublic, policy.RequiredEntitlements);
    }

    public async Task<RoleEntitlementsRequest> GetRoleEntitlementsAsync(string tenantId, Guid roleId, CancellationToken cancellationToken)
    {
        string normalizedTenant = NormalizeName(tenantId, nameof(tenantId));
        if (roleId == Guid.Empty) throw new ArgumentException("Role ID is required.", nameof(roleId));
        IReadOnlySet<string> entitlements = await store.GetEntitlementsAsync(normalizedTenant, [roleId], cancellationToken);
        logger.LogDebug("Read {Count} entitlements for role {RoleId} in tenant {TenantId}", entitlements.Count, roleId, normalizedTenant);
        return new RoleEntitlementsRequest(normalizedTenant, roleId, entitlements.Order(StringComparer.Ordinal).ToList());
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
            key = NormalizeKey(new PolicyKeyRequest(request.TenantId, request.Module, request.HttpMethod, request.RouteTemplate));
        }
        catch (ArgumentException)
        {
            logger.LogWarning("Rejected decision request with invalid endpoint key");
            return new DecisionResponse(false, "Invalid authorization request");
        }

        logger.LogDebug("Evaluating endpoint {TenantId} {Module} {HttpMethod} {RouteTemplate} with {RoleCount} roles",
            key.TenantId, key.Module, key.HttpMethod, key.RouteTemplate, request.RoleIds.Count);
        EndpointPolicy? policy = await store.GetPolicyAsync(key.TenantId, key.Module, key.HttpMethod, key.RouteTemplate, cancellationToken);

        if (policy is null)
        {
            logger.LogWarning("No policy for endpoint {TenantId} {Module} {HttpMethod} {RouteTemplate}",
                key.TenantId, key.Module, key.HttpMethod, key.RouteTemplate);
            return new DecisionResponse(false, "No endpoint policy");
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

        Guid[] roleIds = request.RoleIds.Distinct().ToArray();
        IReadOnlySet<string> entitlements = await store.GetEntitlementsAsync(key.TenantId, roleIds, cancellationToken);
        bool permit = policy.RequiredEntitlements.Any(entitlements.Contains);
        logger.LogInformation("Endpoint decision {Decision} for {TenantId} {Module} {HttpMethod} {RouteTemplate}; {RoleCount} roles, {GrantCount} grants",
            permit ? "Permit" : "Deny", key.TenantId, key.Module, key.HttpMethod, key.RouteTemplate,
            roleIds.Length, entitlements.Count);
        return new DecisionResponse(permit, permit ? "Permit" : "Required entitlement missing");
    }

    public async Task UpsertPolicyAsync(PolicyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        PolicyKeyRequest key = NormalizeKey(new PolicyKeyRequest(request.TenantId, request.Module, request.HttpMethod, request.RouteTemplate));

        if (request.RequiredEntitlements is null ||
            (request.IsPublic && request.RequiredEntitlements.Count != 0) ||
            (!request.IsPublic && request.RequiredEntitlements.Count == 0))
        {
            throw new ArgumentException("Public policies have no entitlements; protected policies require at least one entitlement.", nameof(request));
        }

        string[] entitlements = request.RequiredEntitlements.Select(value => NormalizeName(value, "entitlement"))
            .Distinct(StringComparer.Ordinal).ToArray();
        await store.UpsertPolicyAsync(new EndpointPolicy(key.TenantId, key.Module, key.HttpMethod,
            key.RouteTemplate, request.IsPublic, entitlements), cancellationToken);
        logger.LogInformation("Saved endpoint policy {TenantId} {Module} {HttpMethod} {RouteTemplate}; public {IsPublic}, entitlements {Count}",
            key.TenantId, key.Module, key.HttpMethod, key.RouteTemplate, request.IsPublic, entitlements.Length);
    }

    public async Task DeletePolicyAsync(PolicyKeyRequest request, CancellationToken cancellationToken)
    {
        PolicyKeyRequest key = NormalizeKey(request);
        await store.DeletePolicyAsync(key.TenantId, key.Module, key.HttpMethod, key.RouteTemplate, cancellationToken);
        logger.LogInformation("Deleted endpoint policy {TenantId} {Module} {HttpMethod} {RouteTemplate}",
            key.TenantId, key.Module, key.HttpMethod, key.RouteTemplate);
    }

    public async Task ReplaceRoleEntitlementsAsync(RoleEntitlementsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string tenantId = NormalizeName(request.TenantId, nameof(request.TenantId));
        Guid roleId = request.RoleId != Guid.Empty ? request.RoleId :
            throw new ArgumentException("Role ID is required.", nameof(request));
        IReadOnlyList<string> values = request.Entitlements ?? throw new ArgumentException("Entitlements are required.", nameof(request));
        string[] entitlements = values.Select(value => NormalizeName(value, "entitlement"))
            .Distinct(StringComparer.Ordinal).ToArray();
        await store.ReplaceRoleEntitlementsAsync(tenantId, roleId, entitlements, cancellationToken);
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

        return new PolicyKeyRequest(tenantId, module, method, route);
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
