namespace AccessControl.Contracts;

public sealed record EntitlementPageResponse(IReadOnlyList<EntitlementInfo> Items, int Page, int PageSize, int TotalCount);
