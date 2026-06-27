using System.Collections.Concurrent;
using Microsoft.Azure.Cosmos;

namespace SearchLite.CosmosDB;

/// <summary>
/// Entry point for the Azure Cosmos DB (NoSQL API) provider.
///
/// Each logical index maps to one Cosmos container inside a single database. Containers are created
/// on demand (with a full-text index policy) the first time an index is requested.
/// </summary>
public class SearchManager : ISearchEngineManager, IAsyncDisposable
{
    private readonly CosmosClient _client;
    private readonly string _databaseId;
    private readonly bool _ownsClient;
    private readonly ConcurrentDictionary<string, object> _cache = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Database? _database;

    /// <summary>
    /// Creates a manager from a Cosmos connection string (the account endpoint + key form emitted by
    /// the emulator or the portal).
    /// </summary>
    /// <param name="connectionString">Cosmos account connection string.</param>
    /// <param name="databaseId">Database that holds one container per index. Defaults to <c>searchlite</c>.</param>
    public SearchManager(string connectionString, string databaseId = "searchlite")
        : this(BuildClient(connectionString), databaseId, ownsClient: true)
    {
    }

    /// <summary>
    /// Creates a manager around an existing <see cref="CosmosClient"/>. The client is not disposed by
    /// the manager in this case.
    /// </summary>
    public SearchManager(CosmosClient client, string databaseId, bool ownsClient = false)
    {
        _client = client;
        _databaseId = databaseId;
        _ownsClient = ownsClient;
    }

    private static CosmosClient BuildClient(string connectionString)
    {
        var options = new CosmosClientOptions
        {
            // SearchLite serializes documents with System.Text.Json before storing them under the
            // "doc" property; have the SDK use System.Text.Json for the envelope too so attribute
            // names ("id", "doc", "searchText", "lastUpdated") round-trip consistently.
            UseSystemTextJsonSerializerWithOptions = new System.Text.Json.JsonSerializerOptions(
                System.Text.Json.JsonSerializerDefaults.Web),
            ConnectionMode = ConnectionMode.Gateway
        };
        return new CosmosClient(connectionString, options);
    }

    public async Task<ISearchIndex<T>> Get<T>(string collectionName, CancellationToken cancellationToken)
        where T : ISearchableDocument
    {
        var containerId = SearchIndex<T>.GetContainerName(collectionName);
        var cached = _cache.GetOrAdd(containerId, _ => Create<T>(containerId));

        if (cached is not SearchIndex<T> searchEngine)
        {
            throw new InvalidOperationException(
                $"Unexpected type {cached.GetType().Name} in cache for {collectionName}");
        }

        if (searchEngine.Initialized) return searchEngine;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_database == null)
            {
                _database = await _client.CreateDatabaseIfNotExistsAsync(_databaseId, cancellationToken: cancellationToken);
            }

            if (!searchEngine.Initialized)
            {
                await searchEngine.Init(_database, cancellationToken);
            }
        }
        finally
        {
            _lock.Release();
        }

        return searchEngine;
    }

    private SearchIndex<T> Create<T>(string containerId) where T : ISearchableDocument
    {
        return new SearchIndex<T>(containerId, this);
    }

    internal void Remove<T>(SearchIndex<T> index) where T : ISearchableDocument
    {
        _cache.TryRemove(index.ContainerId, out _);
    }

    public async ValueTask DisposeAsync()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }

        await Task.CompletedTask;
        GC.SuppressFinalize(this);
    }
}
