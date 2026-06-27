using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace SearchLite.SqlServer;

public sealed partial class SearchIndex<T> : ISearchIndex<T> where T : ISearchableDocument
{
    private readonly string _connectionString;
    private readonly SearchManager _manager;
    public string TableName { get; }
    public string FullTextCatalogName { get; }
    public bool Initialized { get; private set; }

    public SearchIndex(string connectionString, string tableName, SearchManager manager)
    {
        _connectionString = connectionString;
        _manager = manager;
        TableName = tableName;
        FullTextCatalogName = $"{TableName}_ftcat";
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

    public async Task<T?> GetAsync(string id, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        var sql = $"""
                   SELECT document
                   FROM {TableName}
                   WHERE id = @id;
                   """;
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return default;
        }

        var json = reader.GetString(0);
        return string.IsNullOrEmpty(json) ? default : JsonSerializer.Deserialize<T>(json);
    }

    public async Task IndexAsync(T document, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        var sql = $"""
                   MERGE {TableName} AS target
                   USING (SELECT @id AS id) AS source
                   ON target.id = source.id
                   WHEN MATCHED THEN
                       UPDATE SET document = @doc, search_text = @text, last_updated = SYSUTCDATETIME()
                   WHEN NOT MATCHED THEN
                       INSERT (id, document, search_text, last_updated)
                       VALUES (@id, @doc, @text, SYSUTCDATETIME());
                   """;
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", document.Id);
        cmd.Parameters.AddWithValue("@doc", JsonSerializer.Serialize(document));
        cmd.Parameters.AddWithValue("@text", document.GetSearchText());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task IndexManyAsync(IEnumerable<T> documents, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        await using var transaction = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            foreach (var document in documents)
            {
                var sql = $"""
                           MERGE {TableName} AS target
                           USING (SELECT @id AS id) AS source
                           ON target.id = source.id
                           WHEN MATCHED THEN
                               UPDATE SET document = @doc, search_text = @text, last_updated = SYSUTCDATETIME()
                           WHEN NOT MATCHED THEN
                               INSERT (id, document, search_text, last_updated)
                               VALUES (@id, @doc, @text, SYSUTCDATETIME());
                           """;
                await using var cmd = new SqlCommand(sql, conn, transaction);
                cmd.Parameters.AddWithValue("@id", document.Id);
                cmd.Parameters.AddWithValue("@doc", JsonSerializer.Serialize(document));
                cmd.Parameters.AddWithValue("@text", document.GetSearchText());
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
        var sw = Stopwatch.StartNew();

        if (string.IsNullOrEmpty(request.Query))
        {
            return await FilterAsync(request, ct);
        }

        await using var conn = await CreateConnectionAsync(ct);

        var clauses = WhereClauseBuilder<T>.BuildClauses(request.Filters);
        var orderClause = BuildOrderByClause(request) ?? "ORDER BY rank DESC";
        var filterSql = clauses.ToWhereClause();

        // CONTAINSTABLE / FREETEXTTABLE return a virtual table with [KEY] (the matched row key)
        // and [RANK] columns. FREETEXTTABLE performs meaning-based, partial matching; CONTAINSTABLE
        // requires precise boolean syntax. We use FREETEXTTABLE for partial matches and the simpler
        // contract.
        var sql = $"""
                   WITH ranked_docs AS (
                       SELECT m.id AS id,
                              m.document AS document,
                              m.last_updated AS last_updated,
                              CAST(ft.[RANK] AS real) AS rank
                       FROM {TableName} AS m
                       INNER JOIN FREETEXTTABLE({TableName}, search_text, @Query) AS ft
                           ON ft.[KEY] = m.id
                       {filterSql}
                   )
                   SELECT id, document, rank, COUNT(*) OVER() AS total, last_updated
                   FROM ranked_docs
                   WHERE rank >= @minScore
                   {orderClause}
                   OFFSET {Math.Max(0, request.Options.Skip)} ROWS FETCH NEXT {request.Options.Take} ROWS ONLY
                   """;

        var results = new List<SearchResult<T>>();
        var totalCount = 0;
        float maxScore = 0;

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Query", request.Query);
        cmd.Parameters.AddWithValue("@minScore", request.Options.MinScore);
        cmd.AddParameters(clauses);

        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var score = reader.GetFloat(2);
                maxScore = Math.Max(maxScore, score);
                totalCount = reader.GetInt32(3);
                var json = reader.GetString(1);

                results.Add(new SearchResult<T>
                {
                    Id = reader.GetString(0),
                    LastUpdated = new DateTimeOffset(reader.GetDateTime(4), TimeSpan.Zero),
                    Score = score,
                    Document = request.Options.IncludeRawDocument && !string.IsNullOrEmpty(json)
                        ? JsonSerializer.Deserialize<T>(json)
                        : default
                });
            }
        }

        if (results.Count == 0)
        {
            var countSql = $"""
                            WITH ranked_docs AS (
                                SELECT m.id AS id,
                                       CAST(ft.[RANK] AS real) AS rank
                                FROM {TableName} AS m
                                INNER JOIN FREETEXTTABLE({TableName}, search_text, @Query) AS ft
                                    ON ft.[KEY] = m.id
                                {filterSql}
                            )
                            SELECT COUNT(*) AS total
                            FROM ranked_docs
                            WHERE rank >= @minScore
                            """;

            await using var countCmd = new SqlCommand(countSql, conn);
            countCmd.Parameters.AddWithValue("@Query", request.Query);
            countCmd.Parameters.AddWithValue("@minScore", request.Options.MinScore);
            countCmd.AddParameters(clauses);

            var countResult = await countCmd.ExecuteScalarAsync(ct);
            totalCount = Convert.ToInt32(countResult);
        }

        return new SearchResponse<T>
        {
            Results = results,
            TotalCount = totalCount,
            MaxScore = maxScore,
            SearchTime = sw.Elapsed
        };
    }

    /// <summary>
    /// If the request does not include a text query, we can use a simpler filter query.
    /// </summary>
    private async Task<SearchResponse<T>> FilterAsync(SearchRequest<T> request, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        await using var conn = await CreateConnectionAsync(ct);

        var clauses = WhereClauseBuilder<T>.BuildClauses(request.Filters);
        var orderClause = BuildOrderByClause(request) ?? "ORDER BY m.id";

        var selectColumns = request.Options.IncludeRawDocument
            ? "m.id, m.last_updated, m.document, COUNT(*) OVER() AS total"
            : "m.id, m.last_updated, COUNT(*) OVER() AS total";

        var sql = $"""
                   SELECT {selectColumns}
                   FROM {TableName} AS m
                   {clauses.ToWhereClause()}
                   {orderClause}
                   OFFSET {Math.Max(0, request.Options.Skip)} ROWS FETCH NEXT {request.Options.Take} ROWS ONLY
                   """;

        var results = new List<SearchResult<T>>();
        var totalCount = 0;
        const float score = 1.0f;

        await using var cmd = new SqlCommand(sql, conn);
        cmd.AddParameters(clauses);

        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                totalCount = reader.GetInt32(request.Options.IncludeRawDocument ? 3 : 2);

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
                            FROM {TableName} AS m
                            {clauses.ToWhereClause()}
                            """;

            await using var countCmd = new SqlCommand(countSql, conn);
            countCmd.AddParameters(clauses);

            var countResult = await countCmd.ExecuteScalarAsync(ct);
            totalCount = Convert.ToInt32(countResult);
        }

        return new SearchResponse<T>
        {
            Results = results,
            TotalCount = totalCount,
            MaxScore = score,
            SearchTime = sw.Elapsed
        };
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        var sql = $"DELETE FROM {TableName} WHERE id = @id";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> DeleteManyAsync(IEnumerable<string> ids, CancellationToken ct = default)
    {
        var idsList = ids.ToList();
        if (idsList.Count == 0) return 0;

        await using var conn = await CreateConnectionAsync(ct);
        var parameters = idsList.Select((_, index) => $"@id{index}").ToArray();
        var sql = $"DELETE FROM {TableName} WHERE id IN ({string.Join(", ", parameters)})";

        await using var cmd = new SqlCommand(sql, conn);
        for (var i = 0; i < idsList.Count; i++)
        {
            cmd.Parameters.AddWithValue($"@id{i}", idsList[i]);
        }

        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> DeleteWhereAsync(SearchRequest<T> request, CancellationToken ct = default)
    {
        if (request.Filters.Count == 0)
        {
            throw new InvalidOperationException("DeleteWhereAsync requires at least one filter. Use ClearAsync() to delete all documents.");
        }

        await using var conn = await CreateConnectionAsync(ct);
        var clauses = WhereClauseBuilder<T>.BuildClauses(request.Filters);
        var sql = $"DELETE FROM {TableName} {clauses.ToWhereClause()}";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.AddParameters(clauses);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> ClearAsync(CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        var sql = $"DELETE FROM {TableName}";
        await using var cmd = new SqlCommand(sql, conn);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<long> CountAsync(CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        var sql = $"SELECT COUNT_BIG(*) FROM {TableName}";
        await using var cmd = new SqlCommand(sql, conn);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }

    public async Task<long> CountAsync(SearchRequest<T> request, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        var clauses = WhereClauseBuilder<T>.BuildClauses(request.Filters);
        var sql = $"""
                   SELECT COUNT_BIG(*)
                   FROM {TableName} AS m
                   {clauses.ToWhereClause()}
                   """;
        await using var cmd = new SqlCommand(sql, conn);
        cmd.AddParameters(clauses);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }

    public async Task DropIndexAsync(CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        var sql = $"""
                   IF EXISTS (SELECT 1 FROM sys.fulltext_indexes fi
                              INNER JOIN sys.objects o ON fi.object_id = o.object_id
                              WHERE o.name = '{TableName}')
                       DROP FULLTEXT INDEX ON {TableName};

                   IF EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = '{FullTextCatalogName}')
                       DROP FULLTEXT CATALOG {FullTextCatalogName};

                   DROP TABLE IF EXISTS {TableName};
                   """;
        await using var cmd = new SqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
        _manager.Remove(this);
    }

    private async Task<SqlConnection> CreateConnectionAsync(CancellationToken ct)
    {
        var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    private async Task EnsureTableExistsAsync(CancellationToken cancellationToken)
    {
        await using var conn = await CreateConnectionAsync(cancellationToken);

        // 1. Table + key index (full-text needs a single-column unique key index).
        var tableSql = $"""
                        IF OBJECT_ID(N'{TableName}', N'U') IS NULL
                        BEGIN
                            CREATE TABLE {TableName} (
                                id NVARCHAR(450) NOT NULL,
                                document NVARCHAR(MAX) NOT NULL,
                                search_text NVARCHAR(MAX) NOT NULL,
                                last_updated DATETIME2 NOT NULL CONSTRAINT DF_{TableName}_lu DEFAULT (SYSUTCDATETIME()),
                                CONSTRAINT PK_{TableName} PRIMARY KEY (id)
                            );
                        END
                        """;
        await using (var cmd = new SqlCommand(tableSql, conn))
        {
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // 2. Full-text catalog + index. The standard SQL Server container image does NOT ship the
        //    full-text component, so these statements fail there. We attempt them and swallow the
        //    failure so basic (filter-only) usage still works on plain images; FTS queries will then
        //    error at query time. On a full-text-enabled image, this wires up search_text for
        //    FREETEXTTABLE / CONTAINSTABLE.
        try
        {
            var catalogSql = $"""
                              IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = '{FullTextCatalogName}')
                                  CREATE FULLTEXT CATALOG {FullTextCatalogName};
                              """;
            await using (var cmd = new SqlCommand(catalogSql, conn))
            {
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            var ftIndexSql = $"""
                              IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes fi
                                             INNER JOIN sys.objects o ON fi.object_id = o.object_id
                                             WHERE o.name = '{TableName}')
                                  CREATE FULLTEXT INDEX ON {TableName}(search_text)
                                      KEY INDEX PK_{TableName}
                                      ON {FullTextCatalogName}
                                      WITH CHANGE_TRACKING AUTO;
                              """;
            await using (var cmd = new SqlCommand(ftIndexSql, conn))
            {
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        catch (SqlException)
        {
            // Full-text not available on this server instance (e.g. the base container image).
            // Filter-only operations remain functional; full-text SearchAsync will surface the error.
        }
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
            var segments = FieldPath.Split(order.PropertyName);
            var path = "$." + string.Join(".", segments);
            return $"JSON_VALUE(m.document, '{path}') {direction}";
        });

        return $"ORDER BY {string.Join(", ", orderClauses)}";
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

        // SQL Server has a 128-character limit for identifiers.
        if (sanitized.Length > 128)
        {
            sanitized = sanitized[..128];
        }

        return sanitized;
    }

    [GeneratedRegex(@"[^a-zA-Z0-9_]")]
    private static partial Regex IdentifierRegex();
}
