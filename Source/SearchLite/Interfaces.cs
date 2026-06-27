namespace SearchLite;

/// <summary>
/// Common interface for searchable documents
/// </summary>
public interface ISearchableDocument
{
    /// <summary>
    /// Document key WITHIN the collection. Deletes and updates will be based on this key. This is not a global unique identifier, but must be unique within the collection.
    /// </summary>
    string Id { get; }
    /// <summary>
    /// The text that will be included as input to the full text search engine.
    /// Other parts of the document will be ignored for the purposes of full text search, but can be used for filtering and ordering.
    /// </summary>
    string GetSearchText();
}


public class SearchResult<T> where T: ISearchableDocument
{
    public required string Id { get; init; }
    public required float Score { get; init; }
    public required DateTimeOffset LastUpdated { get; init; }
    public T? Document { get; init; }
}

public class SearchResponse<T> where T: ISearchableDocument
{
    public required IReadOnlyList<SearchResult<T>> Results { get; init; }
    public int TotalCount { get; init; }
    public float MaxScore { get; init; }
    public TimeSpan SearchTime { get; init; }
}