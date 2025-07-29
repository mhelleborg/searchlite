using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;

namespace SearchLite.Postgres;

public partial class SearchIndex<T> : ISearchIndex<T> where T : ISearchableDocument
{
    private readonly string _connectionString;
    private readonly SearchManager _manager;
    public string TableName { get; }
    public bool Initialized { get; private set; }

    public SearchIndex(string connectionString, string tableName, SearchManager manager)
    {
        _connectionString = connectionString;
        _manager = manager;
        TableName = tableName;
    }

    public async Task Init(CancellationToken cancellationToken)
    {
        if (Initialized)
        {
            return;
        }

        await EnsureTableExistsAsync(cancellationToken);
        Initialized = true;
    }

    public Task<T?> GetAsync(string docId, CancellationToken ct = default)
    {
        return GetDocumentAsync(docId, ct);
    }

    private async Task<T?> GetDocumentAsync(string id, CancellationToken ct)
    {
        await using var conn = await CreateConnectionAsync(ct);
        var sql = $"""
                   SELECT document
                   FROM {TableName}
                   WHERE id = @id;
                   """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return default;
        }

        var json = reader.GetString(0);
        return JsonSerializer.Deserialize<T>(json);
    }

    public async Task IndexAsync(T document, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        var sql = $"""
                   INSERT INTO {TableName} (id, document, search_vector)
                   VALUES (@id, @doc, to_tsvector(@text))
                   ON CONFLICT (id) DO UPDATE 
                   SET document = @doc,
                       search_vector = to_tsvector(@text),
                       last_updated = now();
                   """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", document.Id);
        cmd.Parameters.AddWithValue("doc", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(document));
        cmd.Parameters.AddWithValue("text", NpgsqlDbType.Text, document.GetSearchText());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task IndexManyAsync(IEnumerable<T> documents, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        await using var transaction = await conn.BeginTransactionAsync(ct);
        try
        {
            // Temporary table for bulk import
            await using var cmd = new NpgsqlCommand("""
                CREATE TEMP TABLE bulk_import (
                    id TEXT,
                    document JSONB,
                    search_text TEXT
                ) ON COMMIT DROP
                """, conn);
            await cmd.ExecuteNonQueryAsync(ct);
            // Use COPY for bulk import
            await using var writer = await conn.BeginBinaryImportAsync("COPY bulk_import (id, document, search_text) FROM STDIN (FORMAT BINARY)", ct);
            foreach (var doc in documents)
            {
                await writer.StartRowAsync(ct);
                await writer.WriteAsync(doc.Id, NpgsqlDbType.Text, ct);
                await writer.WriteAsync(JsonSerializer.Serialize(doc), NpgsqlDbType.Jsonb, ct);
                await writer.WriteAsync(doc.GetSearchText(), NpgsqlDbType.Text, ct);
            }

            await writer.CompleteAsync(ct);
            await writer.CloseAsync(ct);
            // Insert from temp table with vector generation
            await using var insertCmd = new NpgsqlCommand($"""
                 INSERT INTO {TableName} (id, document, search_vector)
                 SELECT id, document, to_tsvector(search_text)
                 FROM bulk_import
                 ON CONFLICT (id) DO UPDATE
                 SET
                     document = EXCLUDED.document,
                     search_vector = EXCLUDED.search_vector
                 """, conn);
            await insertCmd.ExecuteNonQueryAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<SearchResponse<T>> SearchAsync(SearchRequest<T> request, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        var sw = Stopwatch.StartNew();
        var clauses = BuildWhereClauses(request);
        var orderClause = BuildOrderByClause(request) ?? "ORDER BY rank DESC";
        var offsetClause = request.Options.Skip < 1 ? "" :  $"OFFSET {request.Options.Skip}";
        var limitClause = $"LIMIT {request.Options.Take}";
        var query = request.Query ?? "";
        //websearch_to_tsquery
        var sql = $"""
                   WITH ranked_docs AS (
                      SELECT id, document, last_updated,
                             ts_rank(search_vector, query) as rank
                      FROM {TableName}, websearch_to_tsquery(@Query) query
                      {clauses.ToWhereClause()}
                   )
                   SELECT id, document, rank, COUNT(*) OVER() as total, last_updated
                   FROM ranked_docs
                   WHERE rank >= @minScore
                   {orderClause}
                   {offsetClause}
                   {limitClause}
                   """;
        var results = new List<SearchResult<T>>();
        var totalCount = 0;
        float maxScore = 0;
        await using var cmd = new NpgsqlCommand(sql, conn);

        cmd.Parameters.Add(new NpgsqlParameter("@Query", query));
        cmd.Parameters.AddWithValue("minScore", request.Options.MinScore);
        cmd.AddParameters(clauses);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var score = reader.GetFloat(2);
            maxScore = Math.Max(maxScore, score);
            totalCount = reader.GetInt32(3);
            var json = reader.GetString(1);
            results.Add(new SearchResult<T> { Id = reader.GetString(0), LastUpdated = reader.GetDateTime(4), Score = score, Document = request.Options.IncludeRawDocument && !string.IsNullOrEmpty(json) ? JsonSerializer.Deserialize<T>(json) : default });
        }

        return new SearchResponse<T>
        {
            Results = results,
            TotalCount = totalCount,
            MaxScore = maxScore,
            SearchTime = sw.Elapsed
        };
    }

    private static string? BuildOrderByClause(SearchRequest<T> request)
    {
        if (request.OrderBys.Count == 0)
        {
            return null;
        }

        var orderClauses = request.OrderBys.Select(order =>
        {
            // Convert property name to jsonb path and direction
            var direction = order.Direction == SortDirection.Ascending ? "ASC" : "DESC";
            return $"document->'{order.PropertyName}' {direction}";
        });
        return $"ORDER BY {string.Join(", ", orderClauses)}";
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        var sql = $"""
                   DELETE FROM {TableName}
                   WHERE id = @id;
                   """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        var sql = $"DELETE FROM {TableName};";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<NpgsqlConnection> CreateConnectionAsync(CancellationToken ct)
    {
        var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    public async Task DropIndexAsync(CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        var sql = $"DROP TABLE IF EXISTS {TableName}";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
        _manager.Remove(this);
    }

    private async Task EnsureTableExistsAsync(CancellationToken cancellationToken)
    {
        await using var conn = await CreateConnectionAsync(cancellationToken);
        await using var transaction = await conn.BeginTransactionAsync(cancellationToken);
        try
        {
            var sql = $"""
                       CREATE TABLE IF NOT EXISTS {TableName} (
                           id TEXT,
                           document JSONB,
                           search_vector tsvector,
                           last_updated timestamptz default now(),
                           PRIMARY KEY (id)
                       );
                       -- GIN
                       CREATE INDEX IF NOT EXISTS {TableName}_filter_idx on {TableName} using GIN(document jsonb_path_ops);
                       CREATE INDEX IF NOT EXISTS {TableName}_search_idx on {TableName} using GIN(search_vector);
                       """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await CreateConnectionAsync(cancellationToken);
        var sql = $"SELECT COUNT(*) FROM {TableName}";
        await using var cmd = new NpgsqlCommand(sql, conn);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result);
    }

    public async Task<long> CountAsync(SearchRequest<T> request, CancellationToken cancellationToken = default)
    {
        await using var conn = await CreateConnectionAsync(cancellationToken);
        var clauses = BuildWhereClauses(request);
        var query = request.Query ?? "";
        var sql = $"""
                   SELECT COUNT(*)
                   FROM {TableName}, websearch_to_tsquery(@Query) query
                   {clauses.ToWhereClause()}
                   """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("@Query", query));
        cmd.AddParameters(clauses);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result);
    }

    private static List<Clause> BuildWhereClauses(SearchRequest<T> request)
    {
        List<Clause> clauses = [];
        if (!string.IsNullOrEmpty(request.Query))
        {
            clauses.Add(new Clause { Sql = "search_vector @@ query" });
        }

        foreach (var clause in WhereClauseBuilder<T>.BuildClauses(request.Filters))
        {
            clauses.Add(clause);
        }

        return clauses;
    }

    public static string GetTableName(string collectionName)
    {
        var sanitizedTypeName = IdentifierRegex().Replace(typeof(T).Name, "").ToLowerInvariant();
        collectionName = IdentifierRegex().Replace(collectionName, "").TrimEnd('_').ToLowerInvariant();
        
        var budget = 63 - collectionName.Length - 11;
        
        if (budget > 0 && sanitizedTypeName.Length > budget)
        {
            sanitizedTypeName = sanitizedTypeName[..budget];
        }
        
        var sanitized = $"searchlite_{sanitizedTypeName}_{collectionName}";
  
        
         // Postgres has a 63-byte limit for identifiers
        if (sanitized.Length > 63)
        {
            sanitized = sanitized[..63];
        }


        return sanitized;
    }

    [GeneratedRegex(@"[^a-zA-Z0-9_]")]
    private static partial Regex IdentifierRegex();
}