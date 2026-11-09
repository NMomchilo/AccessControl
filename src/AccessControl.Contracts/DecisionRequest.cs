namespace AccessControl.Contracts;

public sealed record DecisionRequest(
    string? SubjectId,
    IReadOnlyList<string> Roles,
    string Module,
    string HttpMethod,
    string RouteTemplate,
    string TenantId);
