using System.Collections.Concurrent;

namespace SearchLite.Sqlite;

public class SearchManager(string? connectionString) : ISearchEngineManager
{
    private readonly ConcurrentDictionary<string, object> _cache = new();
    private readonly SemaphoreSlim _lock = new(1, 1);


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

    private ISearchIndex<T> Create<T>(string collectionName) where T : ISearchableDocument
    {
        var cs = connectionString ?? $"Data Source={collectionName};Mode=Memory;Cache=Shared";
        
        return new SearchIndex<T>(cs, collectionName, this);
    }

    public void Remove<T>(SearchIndex<T> searchIndex) where T : ISearchableDocument
    {
        _cache.TryRemove(searchIndex.TableName, out _);
    }
}