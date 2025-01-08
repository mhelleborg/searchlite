using System.Collections.Concurrent;

namespace SearchLite.Postgres;

public class SearchManager : ISearchEngineManager
{
    private readonly string _connectionString;
    private readonly ConcurrentDictionary<string, object> _cache = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SearchManager(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<ISearchIndex<T>> Get<T>(string collectionName, CancellationToken cancellationToken)
        where T : ISearchableDocument
    {
        var tableName = SearchIndex<T>.GetTableName(collectionName);
        var cached = _cache.GetOrAdd(tableName, Create<T>);

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

    private SearchIndex<T> Create<T>(string tableName) where T : ISearchableDocument
    {
        return new SearchIndex<T>(_connectionString, tableName, this);
    }

    public void Remove<T>(SearchIndex<T> index) where T : ISearchableDocument
    {
        _cache.TryRemove(index.TableName, out _);
    }
}