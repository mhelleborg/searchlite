using System.Collections.Concurrent;

namespace SearchLite.DuckDB;

public class SearchManager : ISearchEngineManager
{
    private readonly string? _connectionString;
    private readonly string? _extensionDirectory;
    private readonly ConcurrentDictionary<string, object> _cache = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Creates a new DuckDB backed search manager.
    /// </summary>
    /// <param name="connectionString">
    /// DuckDB connection string (e.g. <c>DataSource=/path/to/db.duckdb</c>). When null, a unique
    /// in-process file database is created per index collection.
    /// </param>
    /// <param name="extensionDirectory">
    /// Optional directory containing a pre-installed copy of the DuckDB <c>fts</c> extension. When set,
    /// DuckDB loads the extension from here instead of downloading it. This is required in environments
    /// without outbound access to the DuckDB extension repository.
    /// </param>
    public SearchManager(string? connectionString, string? extensionDirectory = null)
    {
        _connectionString = connectionString;
        _extensionDirectory = extensionDirectory;
    }

    public async Task<ISearchIndex<T>> Get<T>(string collectionName, CancellationToken cancellationToken = default)
        where T : ISearchableDocument
    {
        var tableName = SearchIndex<T>.GetTableName(collectionName);
        var cached = _cache.GetOrAdd(tableName, _ => Create<T>(collectionName));

        if (cached is not SearchIndex<T> searchEngine)
        {
            throw new InvalidOperationException(
                $"Unexpected type {cached.GetType().Name} in cache for {collectionName}");
        }

        if (searchEngine.Initialized) return searchEngine;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!searchEngine.Initialized)
            {
                await searchEngine.Init(cancellationToken);
            }
        }
        finally
        {
            _lock.Release();
        }

        return searchEngine;
    }

    private ISearchIndex<T> Create<T>(string collectionName) where T : ISearchableDocument
    {
        var cs = _connectionString
                 ?? $"DataSource={Path.Combine(Path.GetTempPath(), $"searchlite_{collectionName}_{Guid.NewGuid():N}.duckdb")}";

        return new SearchIndex<T>(cs, _extensionDirectory, SearchIndex<T>.GetTableName(collectionName), this);
    }

    public void Remove<T>(SearchIndex<T> searchIndex) where T : ISearchableDocument
    {
        _cache.TryRemove(searchIndex.TableName, out _);
    }
}
