using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Azure.Cosmos;

namespace SearchLite.CosmosDB;

/// <summary>
/// A single search index backed by one Azure Cosmos DB (NoSQL API) container.
///
/// <para>Storage: each item is a <see cref="CosmosDocument{T}"/> envelope holding the original
/// document JSON under <c>doc</c>, a denormalized <c>searchText</c> field, the Cosmos <c>id</c>
/// (= the document's <see cref="ISearchableDocument.Id"/>), and a <c>lastUpdated</c> timestamp.</para>
///
/// <para>Partition key: <c>/id</c>. Each document is its own logical partition. This keeps point
/// reads/writes/deletes single-partition and cheap, and matches a search index that is keyed and
/// de-duplicated by document id within a collection. Cross-partition fan-out only happens on
/// <see cref="SearchAsync"/> / <see cref="CountAsync(CancellationToken)"/>, which are inherently
/// multi-document queries.</para>
///
/// <para>Full text: the container is provisioned with a <see cref="FullTextPolicy"/> over
/// <c>/searchText</c> plus a matching full-text index. Queries match with <c>FullTextContains</c>
/// and rank with <c>ORDER BY RANK FullTextScore(...)</c> (BM25). See <see cref="SearchAsync"/> for
/// the scoring caveat.</para>
///
/// <para>Pagination: <c>OFFSET</c>/<c>LIMIT</c> is used (Skip/Take) rather than continuation tokens,
/// because the <see cref="SearchRequest{T}"/> contract is offset based.</para>
/// </summary>
public partial class SearchIndex<T> : ISearchIndex<T> where T : ISearchableDocument
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SearchManager _manager;
    private Container? _container;

    public string ContainerId { get; }
    public bool Initialized { get; private set; }

    internal SearchIndex(string containerId, SearchManager manager)
    {
        ContainerId = containerId;
        _manager = manager;
    }

    private Container Container =>
        _container ?? throw new InvalidOperationException($"Index '{ContainerId}' has not been initialized.");

    internal async Task Init(Database database, CancellationToken cancellationToken)
    {
        if (Initialized)
        {
            return;
        }

        var properties = new ContainerProperties(ContainerId, partitionKeyPath: "/id")
        {
            FullTextPolicy = new FullTextPolicy
            {
                DefaultLanguage = "en-US",
                FullTextPaths = [new FullTextPath { Path = "/searchText", Language = "en-US" }]
            }
        };

        properties.IndexingPolicy.FullTextIndexes.Add(new FullTextIndexPath { Path = "/searchText" });

        var response = await database.CreateContainerIfNotExistsAsync(properties, cancellationToken: cancellationToken);
        _container = response.Container;
        Initialized = true;
    }

    public async Task<T?> GetAsync(string id, CancellationToken ct = default)
    {
        try
        {
            var response = await Container.ReadItemAsync<CosmosDocument<T>>(
                id, new PartitionKey(id), cancellationToken: ct);
            return response.Resource.Deserialize(JsonOptions);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }
    }

    public async Task IndexAsync(T document, CancellationToken ct = default)
    {
        var item = CosmosDocument<T>.From(document, JsonOptions);
        await Container.UpsertItemAsync(item, new PartitionKey(item.Id), cancellationToken: ct);
    }

    public async Task IndexManyAsync(IEnumerable<T> documents, CancellationToken ct = default)
    {
        // Cosmos has no native multi-partition bulk upsert in a single round trip; issue the upserts
        // concurrently. (For very large loads, enabling AllowBulkExecution on the client batches these.)
        var tasks = documents.Select(doc =>
        {
            var item = CosmosDocument<T>.From(doc, JsonOptions);
            return Container.UpsertItemAsync(item, new PartitionKey(item.Id), cancellationToken: ct);
        });

        await Task.WhenAll(tasks);
    }

    public async Task<SearchResponse<T>> SearchAsync(SearchRequest<T> request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var clauses = WhereClauseBuilder<T>.BuildClauses(request.Filters).ToList();
        var terms = TokenizeQuery(request.Query);
        var hasQuery = terms.Count > 0;

        var whereParts = new List<string>();
        if (hasQuery)
        {
            // FullTextContains(path, term) per term; ANY term (partial) vs ALL terms (full match).
            var termClauses = terms.Select((_, i) => $"FullTextContains(c.searchText, @ft{i})");
            var joiner = request.Options.IncludePartialMatches ? " OR " : " AND ";
            whereParts.Add($"({string.Join(joiner, termClauses)})");
        }

        whereParts.AddRange(clauses.Select(c => c.Sql));
        var whereClause = whereParts.Count > 0 ? "WHERE " + string.Join(" AND ", whereParts) : "";

        var orderClause = BuildOrderByClause(request, hasQuery, terms);
        var offset = request.Options.Skip < 1 ? "" : $"OFFSET {request.Options.Skip} ";
        var limit = $"LIMIT {request.Options.Take}";
        var offsetLimit = request.Options.Skip < 1 ? $"OFFSET 0 {limit}" : $"{offset}{limit}";

        var projection = request.Options.IncludeRawDocument
            ? "c.id, c.doc, c.lastUpdated"
            : "c.id, c.lastUpdated";

        var sql = $"SELECT {projection} FROM c {whereClause} {orderClause} {offsetLimit}".Trim();

        var query = new QueryDefinition(sql).AddParameters(clauses);
        for (var i = 0; i < terms.Count; i++)
        {
            query = query.WithParameter($"@ft{i}", terms[i]);
        }

        var results = new List<SearchResult<T>>();
        float maxScore = 0;

        using var iterator = Container.GetItemQueryIterator<CosmosDocument<T>>(query);
        // Relevance ranking is applied server-side via ORDER BY RANK FullTextScore. Cosmos does not
        // expose the BM25 score as a selectable value, so we synthesize a strictly-decreasing score
        // from the returned (already ranked) order. Scores are therefore relative, not absolute BM25.
        var rank = 0;
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(ct);
            foreach (var item in page)
            {
                var score = hasQuery ? 1.0f / (1 + rank) : 1.0f;
                rank++;
                maxScore = Math.Max(maxScore, score);

                if (score < request.Options.MinScore)
                {
                    continue;
                }

                results.Add(new SearchResult<T>
                {
                    Id = item.Id,
                    LastUpdated = item.LastUpdated,
                    Score = score,
                    Document = request.Options.IncludeRawDocument ? item.Deserialize(JsonOptions) : default
                });
            }
        }

        var totalCount = await CountAsync(request, ct);

        return new SearchResponse<T>
        {
            Results = results,
            TotalCount = (int)totalCount,
            MaxScore = maxScore,
            SearchTime = sw.Elapsed
        };
    }

    private static string BuildOrderByClause(SearchRequest<T> request, bool hasQuery, IReadOnlyList<string> terms)
    {
        if (request.OrderBys.Count > 0)
        {
            var parts = request.OrderBys.Select(order =>
            {
                var direction = order.Direction == SortDirection.Ascending ? "ASC" : "DESC";
                var accessor = "c.doc" + string.Concat(FieldPath.Split(order.PropertyName)
                    .Select(s => $"[\"{s.Replace("\"", "\\\"")}\"]"));
                return $"{accessor} {direction}";
            });
            return "ORDER BY " + string.Join(", ", parts);
        }

        if (hasQuery)
        {
            var termArgs = string.Join(", ", terms.Select((_, i) => $"@ft{i}"));
            return $"ORDER BY RANK FullTextScore(c.searchText, {termArgs})";
        }

        return "";
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        try
        {
            await Container.DeleteItemAsync<CosmosDocument<T>>(id, new PartitionKey(id), cancellationToken: ct);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Deleting a missing document is a no-op, matching the relational providers.
        }
    }

    public async Task<int> DeleteManyAsync(IEnumerable<string> ids, CancellationToken ct = default)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return 0;

        var deleted = 0;
        var tasks = idList.Select(async id =>
        {
            try
            {
                await Container.DeleteItemAsync<CosmosDocument<T>>(id, new PartitionKey(id), cancellationToken: ct);
                Interlocked.Increment(ref deleted);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
            }
        });

        await Task.WhenAll(tasks);
        return deleted;
    }

    public async Task<int> DeleteWhereAsync(SearchRequest<T> request, CancellationToken ct = default)
    {
        if (request.Filters.Count == 0)
        {
            throw new InvalidOperationException(
                "DeleteWhereAsync requires at least one filter. Use ClearAsync() to delete all documents.");
        }

        // Cosmos has no DELETE-WHERE; resolve matching ids then delete them point-wise.
        var ids = await QueryIdsAsync(request, ct);
        return await DeleteManyAsync(ids, ct);
    }

    public async Task<int> ClearAsync(CancellationToken ct = default)
    {
        var ids = await QueryIdsAsync(new SearchRequest<T>(), ct);
        return await DeleteManyAsync(ids, ct);
    }

    private async Task<List<string>> QueryIdsAsync(SearchRequest<T> request, CancellationToken ct)
    {
        var clauses = WhereClauseBuilder<T>.BuildClauses(request.Filters).ToList();
        var terms = TokenizeQuery(request.Query);

        var whereParts = new List<string>();
        if (terms.Count > 0)
        {
            whereParts.Add($"({string.Join(" OR ", terms.Select((_, i) => $"FullTextContains(c.searchText, @ft{i})"))})");
        }

        whereParts.AddRange(clauses.Select(c => c.Sql));
        var whereClause = whereParts.Count > 0 ? "WHERE " + string.Join(" AND ", whereParts) : "";

        var sql = $"SELECT c.id FROM c {whereClause}".Trim();
        var query = new QueryDefinition(sql).AddParameters(clauses);
        for (var i = 0; i < terms.Count; i++)
        {
            query = query.WithParameter($"@ft{i}", terms[i]);
        }

        var ids = new List<string>();
        using var iterator = Container.GetItemQueryIterator<IdProjection>(query);
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(ct);
            ids.AddRange(page.Select(p => p.Id));
        }

        return ids;
    }

    public async Task<long> CountAsync(CancellationToken ct = default)
    {
        var query = new QueryDefinition("SELECT VALUE COUNT(1) FROM c");
        using var iterator = Container.GetItemQueryIterator<long>(query);
        long count = 0;
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(ct);
            count += page.Sum();
        }

        return count;
    }

    public async Task<long> CountAsync(SearchRequest<T> request, CancellationToken ct = default)
    {
        var clauses = WhereClauseBuilder<T>.BuildClauses(request.Filters).ToList();
        var terms = TokenizeQuery(request.Query);

        var whereParts = new List<string>();
        if (terms.Count > 0)
        {
            whereParts.Add($"({string.Join(" OR ", terms.Select((_, i) => $"FullTextContains(c.searchText, @ft{i})"))})");
        }

        whereParts.AddRange(clauses.Select(c => c.Sql));
        var whereClause = whereParts.Count > 0 ? "WHERE " + string.Join(" AND ", whereParts) : "";

        var sql = $"SELECT VALUE COUNT(1) FROM c {whereClause}".Trim();
        var query = new QueryDefinition(sql).AddParameters(clauses);
        for (var i = 0; i < terms.Count; i++)
        {
            query = query.WithParameter($"@ft{i}", terms[i]);
        }

        using var iterator = Container.GetItemQueryIterator<long>(query);
        long count = 0;
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(ct);
            count += page.Sum();
        }

        return count;
    }

    public async Task DropIndexAsync(CancellationToken ct = default)
    {
        try
        {
            await Container.DeleteContainerAsync(cancellationToken: ct);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
        }

        Initialized = false;
        _container = null;
        _manager.Remove(this);
    }

    /// <summary>
    /// Splits the free-text query into individual terms for <c>FullTextContains</c>/<c>FullTextScore</c>.
    /// Quotation marks are stripped; whitespace separates terms.
    /// </summary>
    private static List<string> TokenizeQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        return query
            .Replace("\"", " ")
            .Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }

    /// <summary>
    /// Derives a deterministic, valid Cosmos container id from the document type and collection name.
    /// Cosmos ids may not contain '/', '\\', '#', '?' and are limited to 255 characters.
    /// </summary>
    public static string GetContainerName(string collectionName)
    {
        var sanitizedTypeName = IdentifierRegex().Replace(typeof(T).Name, "").ToLowerInvariant();
        collectionName = IdentifierRegex().Replace(collectionName, "").TrimEnd('_').ToLowerInvariant();

        var name = $"searchlite_{sanitizedTypeName}_{collectionName}";

        if (name.Length > 255)
        {
            name = name[..255];
        }

        return name;
    }

    [GeneratedRegex(@"[^a-zA-Z0-9_]")]
    private static partial Regex IdentifierRegex();

    private sealed class IdProjection
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string Id { get; set; } = "";
    }
}
