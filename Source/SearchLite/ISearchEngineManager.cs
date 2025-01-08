namespace SearchLite;

public interface ISearchEngineManager
{
    /// <summary>
    /// Get the search index for the specified type and collection name.
    /// If the index does not exist, it will be created.
    /// </summary>
    /// <param name="collectionName">There can be multiple indexes for the same type, differentiated by collection name</param>
    /// <param name="cancellationToken"></param>s
    /// <typeparam name="T">Indexed types must extend ISearchableDocument to provide ID and the searchable text</typeparam>
    /// <returns>The search index</returns>
    Task<ISearchIndex<T>> Get<T>(string collectionName, CancellationToken cancellationToken) where T : ISearchableDocument;
}