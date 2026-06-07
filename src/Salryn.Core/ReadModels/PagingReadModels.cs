namespace Salryn.Core.ReadModels;

public sealed record PageRequest(
    int PageNumber,
    int PageSize)
{
    public int Offset => Math.Max(0, PageNumber - 1) * PageSize;

    public static PageRequest FirstPage(int pageSize = 50)
    {
        return new PageRequest(1, pageSize);
    }
}

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int PageNumber,
    int PageSize)
{
    public int TotalPages =>
        PageSize <= 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);

    public bool HasNextPage => PageNumber < TotalPages;

    public bool HasPreviousPage => PageNumber > 1;
}

public sealed record SearchText(
    string? Value)
{
    public string Normalized => string.IsNullOrWhiteSpace(Value)
        ? string.Empty
        : Value.Trim();
}
