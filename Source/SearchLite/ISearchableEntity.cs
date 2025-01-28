namespace SearchLite;

/// <summary>
/// Indexable documents must implement this interface
/// </summary>
public interface ISearchableEntity
{
    /// <summary>
    /// Unique identifier within the index
    /// </summary>
    string Id { get; }

    /// <summary>
    /// The searchable text for the document
    /// </summary>
    /// <returns></returns>
    string GetSearchText();
}