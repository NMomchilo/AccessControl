namespace AccessControl.Contracts;

public sealed record PolicyPageResponse(IReadOnlyList<PolicyRequest> Items, int Page, int PageSize, int TotalCount);
