namespace SearchLite;

/// <summary>
/// Indexable documents must implement this interface.
/// </summary>
public interface ISearchableDocument
{
    /// <summary>
    /// Unique identifier within the index.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// The searchable text for the document.
    /// </summary>
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
    public long TotalCount { get; init; }
    public float MaxScore { get; init; }
    public TimeSpan SearchTime { get; init; }
}