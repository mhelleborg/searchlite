using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using DuckDB.NET.Data;

namespace SearchLite.DuckDB;

/// <summary>
/// DuckDB backed full text search index. DuckDB is embedded (like SQLite), so a single connection is
/// shared for the lifetime of the index and all access is serialized.
/// </summary>
/// <remarks>
/// DuckDB's <c>fts</c> index is built over a snapshot of the data and is NOT maintained automatically
/// when rows change. This implementation tracks writes via <see cref="_ftsDirty"/> and rebuilds the
/// index (with <c>overwrite=1</c>) lazily, immediately before a full text search runs, so queries
/// always observe the current data.
/// </remarks>
public sealed partial class SearchIndex<T> : ISearchIndex<T> where T : ISearchableDocument
{
    private readonly string _connectionString;
    private readonly string? _extensionDirectory;
    private readonly SearchManager _manager;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly DuckDBConnection _connection;

    public string TableName { get; }
    public string FtsSchema { get; }
    public bool Initialized { get; private set; }

    /// <summary>Set whenever the table contents change so the fts index is rebuilt before the next search.</summary>
    private bool _ftsDirty = true;

    /// <summary>Whether the fts index currently exists (created at least once).</summary>
    private bool _ftsCreated;

    public SearchIndex(string connectionString, string? extensionDirectory, string tableName, SearchManager manager)
    {
        _connectionString = connectionString;
        _extensionDirectory = extensionDirectory;
        _manager = manager;
        _connection = new DuckDBConnection(connectionString);
        TableName = tableName;
        FtsSchema = $"fts_main_{TableName}";
    }

    public async Task Init(CancellationToken cancellationToken)
    {
        if (Initialized)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Initialized)
            {
                return;
            }

            await _connection.OpenAsync(cancellationToken);
            await LoadFtsExtensionAsync(cancellationToken);
            await EnsureTableExistsAsync(cancellationToken);

            Initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task LoadFtsExtensionAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_extensionDirectory))
        {
            // Load a pre-installed extension from a known directory (no network access required).
            await ExecAsync($"SET extension_directory='{_extensionDirectory.Replace("'", "''")}';", ct);
            await ExecAsync("LOAD fts;", ct);
            return;
        }

        // Default path: install (downloads on first use) then load.
        await ExecAsync("INSTALL fts;", ct);
        await ExecAsync("LOAD fts;", ct);
    }

    private async Task ExecAsync(string sql, CancellationToken ct)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<T?> GetAsync(string id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"SELECT document FROM {TableName} WHERE id = $id";
            cmd.Parameters.Add(new DuckDBParameter("id", id));
            var result = await cmd.ExecuteScalarAsync(ct);
            return ParseResult(result);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static T? ParseResult(object? result)
    {
        var json = result?.ToString();
        return json is null ? default : JsonSerializer.Deserialize<T>(json);
    }

    public Task IndexAsync(T document, CancellationToken ct = default) =>
        IndexManyAsync([document], ct);

    public async Task IndexManyAsync(IEnumerable<T> documents, CancellationToken ct = default)
    {
        var rows = documents
            .Select(d => (d.Id, Document: JsonSerializer.Serialize(d), Text: d.GetSearchText()))
            .ToList();
        if (rows.Count == 0)
        {
            return;
        }

        await _gate.WaitAsync(ct);
        try
        {
            // Bulk path: the DuckDB Appender loads all rows in one native operation — thousands of
            // per-row INSERT round-trips are far too slow (DuckDB is columnar). The appender can't do
            // ON CONFLICT, so stage the rows and upsert from the staging table.
            var staging = $"{TableName}_stg";
            await ExecAsync($"CREATE OR REPLACE TABLE {staging} (id VARCHAR, document VARCHAR, search_text VARCHAR);", ct);

            using (var appender = _connection.CreateAppender(staging))
            {
                foreach (var (id, document, text) in rows)
                {
                    appender.CreateRow()
                        .AppendValue(id)
                        .AppendValue(document)
                        .AppendValue(text)
                        .EndRow();
                }
            }

            await ExecAsync($"""
                             INSERT INTO {TableName} (id, document, search_text, last_updated)
                             SELECT id, document, search_text, now() FROM {staging}
                             ON CONFLICT (id) DO UPDATE SET
                                 document = excluded.document,
                                 search_text = excluded.search_text,
                                 last_updated = now();
                             DROP TABLE {staging};
                             """, ct);

            _ftsDirty = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SearchResponse<T>> SearchAsync(SearchRequest<T> request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        await _gate.WaitAsync(ct);
        try
        {
            if (string.IsNullOrEmpty(request.Query))
            {
                return await FilterAsync(request, sw, ct);
            }

            await EnsureFtsIndexAsync(ct);

            var clauses = WhereClauseBuilder<T>.BuildClauses(request.Filters);
            var matchPredicate = BuildMatchPredicate(request, out var matchParameters);
            // The outer SELECT reads from the ranked_docs CTE, whose columns are unqualified.
            var orderClause = BuildOrderByClause(request, documentColumn: "document") ?? "ORDER BY score DESC, id";

            var filterSql = clauses.Count == 0
                ? string.Empty
                : "AND " + string.Join(" AND ", clauses.Select(c => c.Sql));

            var sql = $"""
                       WITH ranked_docs AS (
                           SELECT m.id AS id,
                                  m.document AS document,
                                  m.last_updated AS last_updated,
                                  coalesce({FtsSchema}.match_bm25(m.id, $ftsQuery), 0.0) AS score
                           FROM {TableName} m
                           WHERE {matchPredicate}
                           {filterSql}
                       )
                       SELECT id, document, last_updated, score, COUNT(*) OVER() AS total
                       FROM ranked_docs
                       WHERE score >= $minScore
                       {orderClause}
                       {GetLimitClause(request)}
                       """;

            var results = new List<SearchResult<T>>();
            long totalCount = 0;
            float maxScore = 0;

            await using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.Parameters.Add(new DuckDBParameter("ftsQuery", request.Query));
                cmd.Parameters.Add(new DuckDBParameter("minScore", (double)request.Options.MinScore));
                foreach (var p in matchParameters) cmd.Parameters.Add(p);
                cmd.AddParameters(clauses);

                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var score = Convert.ToSingle(reader.GetDouble(3));
                    maxScore = Math.Max(maxScore, score);
                    totalCount = reader.GetInt64(4);

                    results.Add(new SearchResult<T>
                    {
                        Id = reader.GetString(0),
                        LastUpdated = new DateTimeOffset(reader.GetDateTime(2), TimeSpan.Zero),
                        Score = score,
                        Document = request.Options.IncludeRawDocument
                            ? JsonSerializer.Deserialize<T>(reader.GetString(1))
                            : default
                    });
                }
            }

            if (results.Count == 0)
            {
                var countSql = $"""
                                WITH ranked_docs AS (
                                    SELECT m.id AS id,
                                           coalesce({FtsSchema}.match_bm25(m.id, $ftsQuery), 0.0) AS score
                                    FROM {TableName} m
                                    WHERE {matchPredicate}
                                    {filterSql}
                                )
                                SELECT COUNT(*) AS total
                                FROM ranked_docs
                                WHERE score >= $minScore
                                """;

                await using var countCmd = _connection.CreateCommand();
                countCmd.CommandText = countSql;
                countCmd.Parameters.Add(new DuckDBParameter("ftsQuery", request.Query));
                countCmd.Parameters.Add(new DuckDBParameter("minScore", (double)request.Options.MinScore));
                foreach (var p in BuildMatchParameters(request)) countCmd.Parameters.Add(p);
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
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// If the request does not include a text query, we can use a simpler filter query.
    /// </summary>
    private async Task<SearchResponse<T>> FilterAsync(SearchRequest<T> request, Stopwatch sw, CancellationToken ct)
    {
        var clauses = WhereClauseBuilder<T>.BuildClauses(request.Filters);
        var orderClause = BuildOrderByClause(request, documentColumn: "m.document") ?? "ORDER BY m.id";

        var selectColumns = request.Options.IncludeRawDocument
            ? "m.id, m.last_updated, m.document, COUNT(*) OVER() AS total"
            : "m.id, m.last_updated, COUNT(*) OVER() AS total";

        var sql = $"""
                   SELECT {selectColumns}
                   FROM {TableName} m
                   {clauses.ToWhereClause()}
                   {orderClause}
                   {GetLimitClause(request)}
                   """;

        var results = new List<SearchResult<T>>();
        long totalCount = 0;
        const float score = 1.0f;

        await using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = sql;
            cmd.AddParameters(clauses);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                totalCount = reader.GetInt64(request.Options.IncludeRawDocument ? 3 : 2);

                results.Add(new SearchResult<T>
                {
                    Id = reader.GetString(0),
                    LastUpdated = new DateTimeOffset(reader.GetDateTime(1), TimeSpan.Zero),
                    Score = score,
                    Document = request.Options.IncludeRawDocument
                        ? JsonSerializer.Deserialize<T>(reader.GetString(2))
                        : default
                });
            }
        }

        if (results.Count == 0)
        {
            var countSql = $"""
                            SELECT COUNT(*) AS total
                            FROM {TableName} m
                            {clauses.ToWhereClause()}
                            """;

            await using var countCmd = _connection.CreateCommand();
            countCmd.CommandText = countSql;
            countCmd.AddParameters(clauses);

            var countResult = await countCmd.ExecuteScalarAsync(ct);
            totalCount = Convert.ToInt64(countResult);
        }

        return new SearchResponse<T>
        {
            Results = results,
            TotalCount = totalCount,
            MaxScore = score,
            SearchTime = sw.Elapsed
        };
    }

    private static string GetLimitClause(SearchRequest<T> request)
    {
        if (request.Options.Skip > 0)
        {
            return $"LIMIT {request.Options.Take} OFFSET {request.Options.Skip}";
        }

        return $"LIMIT {request.Options.Take}";
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default) =>
        await DeleteManyAsync([id], ct);

    public async Task<int> DeleteManyAsync(IEnumerable<string> ids, CancellationToken ct = default)
    {
        var idsList = ids.ToList();
        if (idsList.Count == 0) return 0;

        await _gate.WaitAsync(ct);
        try
        {
            var parameters = idsList.Select((_, index) => $"$id{index}").ToArray();
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"DELETE FROM {TableName} WHERE id IN ({string.Join(",", parameters)})";
            for (var i = 0; i < idsList.Count; i++)
            {
                cmd.Parameters.Add(new DuckDBParameter($"id{i}", idsList[i]));
            }

            var deleted = await cmd.ExecuteNonQueryAsync(ct);
            _ftsDirty = true;
            return deleted;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> DeleteWhereAsync(SearchRequest<T> request, CancellationToken ct = default)
    {
        if (request.Filters.Count == 0)
        {
            throw new InvalidOperationException("DeleteWhereAsync requires at least one filter. Use ClearAsync() to delete all documents.");
        }

        await _gate.WaitAsync(ct);
        try
        {
            var clauses = WhereClauseBuilder<T>.BuildClauses(request.Filters);
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"DELETE FROM {TableName} {clauses.ToWhereClause()}";
            cmd.AddParameters(clauses);

            var deleted = await cmd.ExecuteNonQueryAsync(ct);
            _ftsDirty = true;
            return deleted;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> ClearAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"DELETE FROM {TableName}";
            var deleted = await cmd.ExecuteNonQueryAsync(ct);
            _ftsDirty = true;
            return deleted;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<long> CountAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {TableName}";
            var result = await cmd.ExecuteScalarAsync(ct);
            return Convert.ToInt64(result);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<long> CountAsync(SearchRequest<T> request, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var clauses = WhereClauseBuilder<T>.BuildClauses(request.Filters);

            string sql;
            List<DuckDBParameter> matchParameters = [];
            if (string.IsNullOrEmpty(request.Query))
            {
                sql = $"""
                       SELECT COUNT(*)
                       FROM {TableName} m
                       {clauses.ToWhereClause()}
                       """;
            }
            else
            {
                await EnsureFtsIndexAsync(ct);
                var matchPredicate = BuildMatchPredicate(request, out matchParameters);
                var filterSql = clauses.Count == 0 ? string.Empty : "AND " + string.Join(" AND ", clauses.Select(c => c.Sql));
                sql = $"""
                       SELECT COUNT(*)
                       FROM {TableName} m
                       WHERE {matchPredicate}
                       {filterSql}
                       """;
            }

            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            foreach (var p in matchParameters) cmd.Parameters.Add(p);
            cmd.AddParameters(clauses);

            var result = await cmd.ExecuteScalarAsync(ct);
            return Convert.ToInt64(result);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DropIndexAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_ftsCreated)
            {
                await ExecAsync($"PRAGMA drop_fts_index('{TableName}');", ct);
                _ftsCreated = false;
            }

            await ExecAsync($"DROP TABLE IF EXISTS {TableName};", ct);
            Initialized = false;
        }
        finally
        {
            _gate.Release();
        }

        _manager.Remove(this);
        await _connection.DisposeAsync();
    }

    private async Task EnsureTableExistsAsync(CancellationToken cancellationToken)
    {
        await ExecAsync($"""
                         CREATE TABLE IF NOT EXISTS {TableName} (
                             id VARCHAR PRIMARY KEY NOT NULL,
                             document VARCHAR NOT NULL,
                             search_text VARCHAR NOT NULL,
                             last_updated TIMESTAMP NOT NULL DEFAULT now()
                         );
                         """, cancellationToken);
        _ftsDirty = true;
    }

    /// <summary>
    /// Rebuilds the DuckDB fts index over the current table snapshot when the table has changed since
    /// the last rebuild. DuckDB's fts index does not auto-update on writes, so this guarantees a search
    /// sees current data. The caller must hold <see cref="_gate"/>.
    /// </summary>
    private async Task EnsureFtsIndexAsync(CancellationToken ct)
    {
        if (!_ftsDirty)
        {
            return;
        }

        // create_fts_index over a snapshot; overwrite=1 makes the call idempotent and refreshes content.
        // Disable stemming, stop words and accent stripping so tokens match the documents verbatim
        // (case-insensitive). lower=1 keeps searches case-insensitive while preserving accents.
        await ExecAsync(
            $"PRAGMA create_fts_index('{TableName}', 'id', 'search_text', stemmer='none', stopwords='none', strip_accents=0, lower=1, overwrite=1);",
            ct);

        _ftsCreated = true;
        _ftsDirty = false;
    }

    /// <summary>
    /// Builds the candidate-selection predicate for a full text query. DuckDB's BM25 is a bag-of-words
    /// score and cannot express phrase or OR semantics on its own, so candidate selection is done by
    /// matching against a normalized (lower-cased, token-delimited) projection of search_text:
    /// <list type="bullet">
    /// <item>partial matches enabled: a document matches if it contains ANY query token,</item>
    /// <item>partial matches disabled: a document matches only if it contains the whole query as a
    /// contiguous, token-aligned phrase.</item>
    /// </list>
    /// BM25 is still used to score/rank the selected candidates.
    /// </summary>
    private static string BuildMatchPredicate(SearchRequest<T> request, out List<DuckDBParameter> parameters)
    {
        parameters = BuildMatchParameters(request);
        var normalizedField = NormalizeSqlExpression("m.search_text");

        if (request.Options.IncludePartialMatches)
        {
            var tokens = Tokenize(request.Query ?? string.Empty);
            if (tokens.Count == 0)
            {
                return "TRUE";
            }

            var ors = tokens.Select((_, i) => $"{normalizedField} LIKE '%' || $token{i} || '%'");
            return "(" + string.Join(" OR ", ors) + ")";
        }

        return $"{normalizedField} LIKE '%' || $phrase || '%'";
    }

    private static List<DuckDBParameter> BuildMatchParameters(SearchRequest<T> request)
    {
        var parameters = new List<DuckDBParameter>();
        if (request.Options.IncludePartialMatches)
        {
            var tokens = Tokenize(request.Query ?? string.Empty);
            for (var i = 0; i < tokens.Count; i++)
            {
                parameters.Add(new DuckDBParameter($"token{i}", $" {tokens[i]} "));
            }
        }
        else
        {
            parameters.Add(new DuckDBParameter("phrase", $" {NormalizeText(request.Query ?? string.Empty)} "));
        }

        return parameters;
    }

    /// <summary>SQL projection that normalizes a text column the same way <see cref="NormalizeText"/> does in C#.</summary>
    private static string NormalizeSqlExpression(string column) =>
        $"(' ' || trim(regexp_replace(lower({column}), '[^a-z0-9]+', ' ', 'g')) || ' ')";

    private static List<string> Tokenize(string query) =>
        NormalizeText(query).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>Lower-cases, replaces every run of non-alphanumeric characters with a single space and trims.</summary>
    private static string NormalizeText(string value)
    {
        var lowered = value.ToLowerInvariant();
        var collapsed = NonAlphaNumericRegex().Replace(lowered, " ");
        return collapsed.Trim();
    }

    private static string? BuildOrderByClause(SearchRequest<T> request, string documentColumn)
    {
        if (request.OrderBys.Count == 0)
        {
            return null;
        }

        var orderClauses = request.OrderBys.Select(order =>
        {
            var direction = order.Direction == SortDirection.Ascending ? "ASC" : "DESC";
            // Order so that NULLs sort as the smallest value, matching LINQ's IComparable ordering
            // (DuckDB defaults to NULLS LAST for ASC / NULLS FIRST for DESC otherwise).
            var nulls = order.Direction == SortDirection.Ascending ? "NULLS FIRST" : "NULLS LAST";
            // json_extract returns an untyped JSON value which sorts lexically; cast numeric fields so
            // they sort numerically, and read text fields as strings so quotes are stripped.
            var expression = IsNumericOrderField(order.PropertyName)
                ? $"CAST(json_extract({documentColumn}, '$.{order.PropertyName}') AS DOUBLE)"
                : $"json_extract_string({documentColumn}, '$.{order.PropertyName}')";
            return $"{expression} {direction} {nulls}";
        });

        return $"ORDER BY {string.Join(", ", orderClauses)}";
    }

    private static bool IsNumericOrderField(string propertyName)
    {
        var type = ResolvePropertyType(propertyName);
        if (type is null)
        {
            return false;
        }

        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying.IsEnum)
        {
            return false;
        }

        return underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(short)
               || underlying == typeof(byte) || underlying == typeof(double) || underlying == typeof(decimal)
               || underlying == typeof(float);
    }

    private static Type? ResolvePropertyType(string propertyName)
    {
        var current = typeof(T);
        foreach (var segment in propertyName.Split('.'))
        {
            var prop = current.GetProperty(segment, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase);
            if (prop is null)
            {
                return null;
            }

            current = prop.PropertyType;
        }

        return current;
    }

    public static string GetTableName(string collectionName)
    {
        var sanitizedTypeName = IdentifierRegex().Replace(typeof(T).Name, "").ToLowerInvariant();
        collectionName = IdentifierRegex().Replace(collectionName, "").TrimEnd('_').ToLowerInvariant();

        var budget = 128 - collectionName.Length - 11;

        if (budget > 0 && sanitizedTypeName.Length > budget)
        {
            sanitizedTypeName = sanitizedTypeName[..budget];
        }

        var sanitized = $"searchlite_{sanitizedTypeName}_{collectionName}";
        if (sanitized.Length > 128)
        {
            sanitized = sanitized[..128];
        }

        return sanitized;
    }

    [GeneratedRegex(@"[^a-zA-Z0-9_]")]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphaNumericRegex();
}
