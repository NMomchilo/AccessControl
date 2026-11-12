namespace UserManagement.Domain;

public sealed record PageSlice<T>(IReadOnlyList<T> Items, int TotalCount);
