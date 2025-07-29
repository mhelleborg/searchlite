namespace SearchLite;

public interface ISearchableDocument
{
    string Id { get; }
    string GetSearchText();
}


public class SearchResult<T>
{
    public required string Id { get; init; }
    public required float Score { get; init; }
    public required DateTimeOffset LastUpdated { get; init; }
    public T? Document { get; init; }
}

public class SearchResponse<T>
{
    public required IReadOnlyList<SearchResult<T>> Results { get; init; }
    public int TotalCount { get; init; }
    public float MaxScore { get; init; }
    public TimeSpan SearchTime { get; init; }
}