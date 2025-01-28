namespace SearchLite;

public enum SortDirection
{
    Ascending,
    Descending
}

public class OrderByNode<T>
{
    public string PropertyName { get; init; } = null!;
    public SortDirection Direction { get; init; }
}