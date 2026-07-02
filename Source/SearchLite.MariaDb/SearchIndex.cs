using System.Globalization;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MySqlConnector;

namespace SearchLite.MariaDb;

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
        await using var cmd = new MySqlCommand(sql, conn);
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
                   INSERT INTO {TableName} (id, document, search_text, last_updated)
                   VALUES (@id, @doc, @text, CURRENT_TIMESTAMP(6))
                   ON DUPLICATE KEY UPDATE
                       document = VALUES(document),
                       search_text = VALUES(search_text),
                       last_updated = CURRENT_TIMESTAMP(6);
                   """;
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", document.Id);
        cmd.Parameters.AddWithValue("doc", JsonSerializer.Serialize(document));
        cmd.Parameters.AddWithValue("text", document.GetSearchText());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task IndexManyAsync(IEnumerable<T> documents, CancellationToken ct = default)
    {
        var docs = documents.ToList();
        if (docs.Count == 0)
        {
            return;
        }

        await using var conn = await CreateConnectionAsync(ct);
        await using var transaction = await conn.BeginTransactionAsync(ct);
        try
        {
            const int batchSize = 500;
            for (var offset = 0; offset < docs.Count; offset += batchSize)
            {
                var batch = docs.Skip(offset).Take(batchSize).ToList();
                var valueRows = new List<string>(batch.Count);
                await using var cmd = new MySqlCommand { Connection = conn, Transaction = transaction };

                for (var i = 0; i < batch.Count; i++)
                {
                    var doc = batch[i];
                    valueRows.Add($"(@id{i}, @doc{i}, @text{i}, CURRENT_TIMESTAMP(6))");
                    cmd.Parameters.AddWithValue($"id{i}", doc.Id);
                    cmd.Parameters.AddWithValue($"doc{i}", JsonSerializer.Serialize(doc));
                    cmd.Parameters.AddWithValue($"text{i}", doc.GetSearchText());
                }

                cmd.CommandText = $"""
                                   INSERT INTO {TableName} (id, document, search_text, last_updated)
                                   VALUES {string.Join(", ", valueRows)}
                                   ON DUPLICATE KEY UPDATE
                                       document = VALUES(document),
                                       search_text = VALUES(search_text),
                                       last_updated = CURRENT_TIMESTAMP(6);
                                   """;
                await cmd.ExecuteNonQueryAsync(ct);
            }

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

        var hasQuery = !string.IsNullOrWhiteSpace(request.Query);
        var booleanQuery = hasQuery ? BuildBooleanQuery(request.Query!, request.Options.IncludePartialMatches) : null;
        // A query that tokenizes to nothing (e.g. only punctuation) cannot match anything via FTS.
        hasQuery = hasQuery && !string.IsNullOrEmpty(booleanQuery);

        var scoreExpression = hasQuery
            ? "MATCH(search_text) AGAINST(@Query IN BOOLEAN MODE)"
            : "CAST(0 AS DOUBLE)";

        var clauses = BuildWhereClauses(request, hasQuery);
        var orderClause = BuildOrderByClause(request) ?? "ORDER BY score DESC";
        var offsetClause = request.Options.Skip < 1 ? "" : $"OFFSET {request.Options.Skip}";
        var limitClause = $"LIMIT {request.Options.Take}";

        var sql = $"""
                   SELECT id, document, score, last_updated, COUNT(*) OVER() AS total
                   FROM (
                       SELECT id, document, last_updated,
                              {scoreExpression} AS score
                       FROM {TableName}
                       {clauses.ToWhereClause()}
                   ) AS ranked
                   WHERE score >= @minScore
                   {orderClause}
                   {limitClause}
                   {offsetClause}
                   """;

        var results = new List<SearchResult<T>>();
        long totalCount = 0;
        float maxScore = 0;

        await using var cmd = new MySqlCommand(sql, conn);
        if (hasQuery)
        {
            cmd.Parameters.AddWithValue("Query", booleanQuery);
        }
        cmd.Parameters.AddWithValue("minScore", request.Options.MinScore);
        cmd.AddParameters(clauses);

        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                // The score column is DOUBLE for FTS queries and CAST(0 AS DOUBLE) otherwise, but
                // read it tolerantly so an unexpected provider/server numeric type can't throw.
                var score = Convert.ToSingle(reader.GetValue(2), CultureInfo.InvariantCulture);
                maxScore = Math.Max(maxScore, score);
                var json = reader.GetString(1);
                totalCount = reader.GetInt64(4);
                results.Add(new SearchResult<T>
                {
                    Id = reader.GetString(0),
                    LastUpdated = reader.GetDateTime(3),
                    Score = score,
                    Document = request.Options.IncludeRawDocument && !string.IsNullOrEmpty(json)
                        ? JsonSerializer.Deserialize<T>(json)
                        : default
                });
            }
        }

        // If no rows were returned, the window function gave us no total; compute it separately.
        if (results.Count == 0)
        {
            var countSql = $"""
                            SELECT COUNT(*)
                            FROM (
                                SELECT {scoreExpression} AS score
                                FROM {TableName}
                                {clauses.ToWhereClause()}
                            ) AS ranked
                            WHERE score >= @minScore
                            """;
            await using var countCmd = new MySqlCommand(countSql, conn);
            if (hasQuery)
            {
                countCmd.Parameters.AddWithValue("Query", booleanQuery);
            }
            countCmd.Parameters.AddWithValue("minScore", request.Options.MinScore);
            countCmd.AddParameters(clauses);
            var countResult = await countCmd.ExecuteScalarAsync(ct);
            totalCount = Convert.ToInt64(countResult);
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
            var direction = order.Direction == SortDirection.Ascending ? "ASC" : "DESC";
            return $"{WhereClauseBuilder<T>.BuildOrderAccessor(order.PropertyName)} {direction}";
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
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> DeleteManyAsync(IEnumerable<string> ids, CancellationToken ct = default)
    {
        var idsList = ids.ToList();
        if (idsList.Count == 0) return 0;

        await using var conn = await CreateConnectionAsync(ct);
        var paramNames = new List<string>(idsList.Count);
        await using var cmd = new MySqlCommand { Connection = conn };
        for (var i = 0; i < idsList.Count; i++)
        {
            var name = $"@id{i}";
            paramNames.Add(name);
            cmd.Parameters.AddWithValue($"id{i}", idsList[i]);
        }

        cmd.CommandText = $"""
                           DELETE FROM {TableName}
                           WHERE id IN ({string.Join(", ", paramNames)});
                           """;
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> DeleteWhereAsync(SearchRequest<T> request, CancellationToken ct = default)
    {
        if (request.Filters.Count == 0)
        {
            throw new InvalidOperationException("DeleteWhereAsync requires at least one filter. Use ClearAsync() to delete all documents.");
        }

        await using var conn = await CreateConnectionAsync(ct);
        var hasQuery = !string.IsNullOrWhiteSpace(request.Query);
        var booleanQuery = hasQuery ? BuildBooleanQuery(request.Query!, request.Options.IncludePartialMatches) : null;
        hasQuery = hasQuery && !string.IsNullOrEmpty(booleanQuery);

        var clauses = BuildWhereClauses(request, hasQuery);
        var sql = $"DELETE FROM {TableName} {clauses.ToWhereClause()}";

        await using var cmd = new MySqlCommand(sql, conn);
        if (hasQuery)
        {
            cmd.Parameters.AddWithValue("Query", booleanQuery);
        }
        cmd.AddParameters(clauses);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> ClearAsync(CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        var sql = $"DELETE FROM {TableName};";
        await using var cmd = new MySqlCommand(sql, conn);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<MySqlConnection> CreateConnectionAsync(CancellationToken ct)
    {
        var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    public async Task DropIndexAsync(CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        var sql = $"DROP TABLE IF EXISTS {TableName}";
        await using var cmd = new MySqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
        _manager.Remove(this);
    }

    private async Task EnsureTableExistsAsync(CancellationToken cancellationToken)
    {
        await using var conn = await CreateConnectionAsync(cancellationToken);
        var sql = $"""
                   CREATE TABLE IF NOT EXISTS {TableName} (
                       id VARCHAR(255) NOT NULL,
                       document JSON,
                       search_text LONGTEXT,
                       last_updated TIMESTAMP(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                       PRIMARY KEY (id),
                       FULLTEXT KEY {TableName}_ft (search_text)
                   ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                   """;
        await using var cmd = new MySqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await CreateConnectionAsync(cancellationToken);
        var sql = $"SELECT COUNT(*) FROM {TableName}";
        await using var cmd = new MySqlCommand(sql, conn);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result);
    }

    public async Task<long> CountAsync(SearchRequest<T> request, CancellationToken cancellationToken = default)
    {
        await using var conn = await CreateConnectionAsync(cancellationToken);
        var hasQuery = !string.IsNullOrWhiteSpace(request.Query);
        var booleanQuery = hasQuery ? BuildBooleanQuery(request.Query!, request.Options.IncludePartialMatches) : null;
        hasQuery = hasQuery && !string.IsNullOrEmpty(booleanQuery);

        var clauses = BuildWhereClauses(request, hasQuery);
        var sql = $"""
                   SELECT COUNT(*)
                   FROM {TableName}
                   {clauses.ToWhereClause()}
                   """;
        await using var cmd = new MySqlCommand(sql, conn);
        if (hasQuery)
        {
            cmd.Parameters.AddWithValue("Query", booleanQuery);
        }
        cmd.AddParameters(clauses);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result);
    }

    private static List<Clause> BuildWhereClauses(SearchRequest<T> request, bool hasQuery)
    {
        List<Clause> clauses = [];
        if (hasQuery)
        {
            clauses.Add(new Clause { Sql = "MATCH(search_text) AGAINST(@Query IN BOOLEAN MODE)" });
        }

        foreach (var clause in WhereClauseBuilder<T>.BuildClauses(request.Filters))
        {
            clauses.Add(clause);
        }

        return clauses;
    }

    /// <summary>
    /// Builds a safe MariaDB boolean-mode full-text query from arbitrary user input.
    /// The raw query is tokenized into alphanumeric terms (so operator characters such as
    /// + - * " ( ) ~ &lt; &gt; cannot reach the parser), then rebuilt:
    ///   * IncludePartialMatches == true  -> each term gets a trailing '*' wildcard and terms
    ///     are OR-ed (space separated), so any prefix match contributes to the result.
    ///   * IncludePartialMatches == false -> each term is required ('+term'), so a document must
    ///     contain every term to match.
    /// Returns an empty string when the input tokenizes to nothing.
    /// </summary>
    internal static string BuildBooleanQuery(string query, bool includePartialMatches)
    {
        var terms = TokenizeRegex().Matches(query)
            .Select(m => m.Value)
            .Where(t => t.Length > 0)
            .ToList();

        if (terms.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var term in terms)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            if (includePartialMatches)
            {
                // Partial = match ANY term (boolean-mode OR of bare, exact tokens). This mirrors the
                // SQLite/Postgres providers: it is term-level OR, NOT prefix matching — a query that
                // tokenizes to "c" must match the token "c", not every word starting with c.
                builder.Append(term);
            }
            else
            {
                // Non-partial = every term required (AND).
                builder.Append('+').Append(term);
            }
        }

        return builder.ToString();
    }

    public static string GetTableName(string collectionName)
    {
        var sanitizedTypeName = IdentifierRegex().Replace(typeof(T).Name, "").ToLowerInvariant();
        collectionName = IdentifierRegex().Replace(collectionName, "").TrimEnd('_').ToLowerInvariant();

        var budget = 64 - collectionName.Length - 11;

        if (budget > 0 && sanitizedTypeName.Length > budget)
        {
            sanitizedTypeName = sanitizedTypeName[..budget];
        }

        var sanitized = $"searchlite_{sanitizedTypeName}_{collectionName}";

        // MariaDB has a 64-character limit for identifiers
        if (sanitized.Length > 64)
        {
            sanitized = sanitized[..64];
        }

        return sanitized;
    }

    [GeneratedRegex(@"[^a-zA-Z0-9_]")]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex(@"[\p{L}\p{N}_]+")]
    private static partial Regex TokenizeRegex();
}
