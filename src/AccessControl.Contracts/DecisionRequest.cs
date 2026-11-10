namespace AccessControl.Contracts;

public sealed record DecisionRequest(
    string? SubjectId,
    IReadOnlyList<Guid> RoleIds,
    string Module,
    string HttpMethod,
    string RouteTemplate,
    string TenantId);
