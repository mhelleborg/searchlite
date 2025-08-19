namespace SearchLite;

public interface ISearchIndex<T> where T : ISearchableDocument
{
    /// <summary>
    /// Get document by ID
    /// </summary>
    /// <param name="id">The document ID</param>
    /// <param name="ct"></param>
    /// <returns></returns>
    Task<T?> GetAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Index a document
    /// </summary>
    /// <param name="document"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    Task IndexAsync(T document, CancellationToken ct = default);

    /// <summary>
    /// Bulk index documents
    /// </summary>
    /// <param name="documents"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    Task IndexManyAsync(IEnumerable<T> documents, CancellationToken ct = default);

    /// <summary>
    /// Search the index
    /// </summary>
    /// <param name="request"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    Task<SearchResponse<T>> SearchAsync(SearchRequest<T> request, CancellationToken ct = default);

    /// <summary>
    /// Remove a document from the index
    /// </summary>
    /// <param name="id"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    Task DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Remove multiple documents from the index by their IDs
    /// </summary>
    /// <param name="ids"></param>
    /// <param name="ct"></param>
    /// <returns>The number of deleted documents</returns>
    Task<int> DeleteManyAsync(IEnumerable<string> ids, CancellationToken ct = default);

    /// <summary>
    /// Remove documents from the index that match the specified filters
    /// </summary>
    /// <param name="request"></param>
    /// <param name="ct"></param>
    /// <returns>The number of deleted documents</returns>
    Task<int> DeleteWhereAsync(SearchRequest<T> request, CancellationToken ct = default);

    /// <summary>
    /// Remove all documents from the index
    /// </summary>
    /// <param name="ct"></param>
    /// <returns>The number of deleted documents</returns>
    Task<int> ClearAsync(CancellationToken ct = default);

    /// <summary>
    /// Count all documents in the index
    /// </summary>
    /// <param name="ct"></param>
    /// <returns>The total number of documents in the index</returns>
    Task<long> CountAsync(CancellationToken ct = default);

    /// <summary>
    /// Count documents in the index that match the specified filters
    /// </summary>
    /// <param name="request">The search request containing filters</param>
    /// <param name="ct"></param>
    /// <returns>The number of documents matching the filters</returns>
    Task<long> CountAsync(SearchRequest<T> request, CancellationToken ct = default);

    /// <summary>
    /// Delete the index
    /// </summary>
    /// <param name="ct"></param>
    /// <returns></returns>
    Task DropIndexAsync(CancellationToken ct = default);

    /// <summary>
    /// Initialize the search index. Called by the manager when the index is created.
    /// Not required to be called by the user.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task Init(CancellationToken cancellationToken);
}