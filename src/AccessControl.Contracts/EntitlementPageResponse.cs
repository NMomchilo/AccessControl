namespace AccessControl.Contracts;

public sealed record EntitlementPageResponse(IReadOnlyList<string> Items, int Page, int PageSize, int TotalCount);
