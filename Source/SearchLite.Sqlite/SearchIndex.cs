using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace SearchLite.Sqlite;

public sealed class SearchIndex<T> : ISearchIndex<T> where T : ISearchableDocument
{
    private readonly string _connectionString;
    private readonly SearchManager _manager;
    private static readonly SemaphoreSlim UpdateSemaphore = new(1, 1);
    public string TableName { get; }
    public string FtsTableName { get; }
    public bool Initialized { get; private set; }
    private readonly SqliteConnection _writeConnection;
    private readonly AsyncLocal<SqliteConnection?> _threadLocalConnection = new();

    private SqliteConnection Connection
    {
        get
        {
            if (_threadLocalConnection.Value == null)
            {
                var connection = new SqliteConnection(_connectionString);
                connection.Open();
                _threadLocalConnection.Value = connection;
            }

            return _threadLocalConnection.Value;
        }
    }

    public SearchIndex(string connectionString, string tableName, SearchManager manager)
    {
        _connectionString = connectionString;
        _manager = manager;
        _writeConnection = new SqliteConnection(connectionString);
        TableName = tableName;
        FtsTableName = $"{TableName}_fts";
    }

    public async Task Init(CancellationToken cancellationToken)
    {
        if (Initialized)
        {
            return;
        }

        await _writeConnection.OpenAsync(cancellationToken);
        await EnsureTableExistsAsync(cancellationToken);

        Initialized = true;
    }

    public async Task<T?> GetAsync(string id, CancellationToken ct = default)
    {
        var sql = $"SELECT document FROM {TableName} WHERE id = @id";


        await using var cmd = new SqliteCommand(sql, Connection);
        cmd.Parameters.AddWithValue("@id", id);

        var result = await cmd.ExecuteScalarAsync(ct);
        return ParseResult(result);
    }

    private static T? ParseResult(object? result)
    {
        var json = result?.ToString();

        return json is null ? default : JsonSerializer.Deserialize<T>(json);
    }


    public Task IndexAsync(T document, CancellationToken ct = default) =>
        SerializedWrite(async transaction =>
        {
            var docSql = $"""
                          INSERT INTO {TableName} (id, document, search_text)
                          VALUES (@id, @doc, @text)
                          ON CONFLICT(id) DO UPDATE SET
                              document = @doc,
                              search_text = @text;
                          """;

            await using var docCmd = new SqliteCommand(docSql, transaction.Connection, transaction);
            docCmd.Parameters.AddWithValue("@id", document.Id);
            docCmd.Parameters.AddWithValue("@doc", JsonSerializer.Serialize(document));
            docCmd.Parameters.AddWithValue("@text", document.GetSearchText());
            await docCmd.ExecuteNonQueryAsync(ct);
        }, ct);

    public Task IndexManyAsync(IEnumerable<T> documents, CancellationToken ct = default) =>
        SerializedWrite(async transaction =>
        {
            foreach (var document in documents)
            {
                var docSql = $"""
                              INSERT INTO {TableName} (id, document, search_text)
                              VALUES (@id, @doc, @text)
                              ON CONFLICT(id) DO UPDATE SET
                                  document = @doc,
                                  search_text = @text;
                              """;

                await using var docCmd = new SqliteCommand(docSql, transaction.Connection, transaction);
                docCmd.Parameters.AddWithValue("@id", document.Id);
                docCmd.Parameters.AddWithValue("@doc", JsonSerializer.Serialize(document));
                docCmd.Parameters.AddWithValue("@text", document.GetSearchText());
                await docCmd.ExecuteNonQueryAsync(ct);
            }
        }, ct);

    public async Task<SearchResponse<T>> SearchAsync(SearchRequest<T> request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var clauses = WhereClauseBuilder<T>.BuildClauses(request.Filters);
        var limitClause = $"LIMIT {request.Options.MaxResults}";

        if (string.IsNullOrEmpty(request.Query))
        {
            return await FilterAsync(request, ct);
        }


        var query = request.Query ?? "";
        if (request.IncludePartialMatches && !string.IsNullOrEmpty(query))
        {
            // Convert space-separated terms into OR query
            query = string.Join(" OR ", query.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        var sql = $"""
                   WITH ranked_docs AS (
                       SELECT m.id as id,
                              m.document,
                              fts.rank * -1 as rank
                       FROM {TableName} m
                       RIGHT JOIN (
                           SELECT id, rank
                           FROM {FtsTableName}
                           WHERE {FtsTableName} MATCH @Query
                       ) fts ON m.id = fts.id
                       {clauses.ToWhereClause()}
                   )
                   SELECT id, document, rank, COUNT(*) OVER() as total
                   FROM ranked_docs
                   WHERE rank >= @minScore
                   ORDER BY rank desc
                   {limitClause}
                   """;

        var results = new List<SearchResult<T>>();
        var totalCount = 0;
        float maxScore = 0;


        await using var cmd = new SqliteCommand(sql, Connection);
        cmd.Parameters.AddWithValue("@Query", query);
        cmd.Parameters.AddWithValue("@minScore", request.Options.MinScore);
        cmd.AddParameters(clauses);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var score = Convert.ToSingle(reader.GetDouble(2));
            maxScore = Math.Max(maxScore, score);
            totalCount = reader.GetInt32(3);

            results.Add(new SearchResult<T>
            {
                Id = reader.GetString(0),
                Score = score,
                Document = request.Options.IncludeRawDocument
                    ? JsonSerializer.Deserialize<T>(reader.GetString(1))
                    : default
            });
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
    /// If the request does not include a text query, we can use a simpler filter query
    /// </summary>
    /// <param name="request"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    private async Task<SearchResponse<T>> FilterAsync(SearchRequest<T> request, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var clauses = WhereClauseBuilder<T>.BuildClauses(request.Filters);
        var limitClause = $"LIMIT {request.Options.MaxResults}";

        var selectColumns = request.Options.IncludeRawDocument
            ? "m.id, m.document, COUNT(*) OVER() as total"
            : "m.id, COUNT(*) OVER() as total";

        var sql = $"""
                   SELECT {selectColumns}
                   FROM {TableName} m
                   {clauses.ToWhereClause()}
                   ORDER BY m.id
                   {limitClause}
                   """;

        var results = new List<SearchResult<T>>();
        var totalCount = 0;
        const float score = 1.0f;


        await using var cmd = new SqliteCommand(sql, Connection);

        cmd.AddParameters(clauses);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            totalCount = reader.GetInt32(request.Options.IncludeRawDocument ? 2 : 1);

            results.Add(new SearchResult<T>
            {
                Id = reader.GetString(0),
                Score = score,
                Document = request.Options.IncludeRawDocument
                    ? JsonSerializer.Deserialize<T>(reader.GetString(1))
                    : default
            });
        }

        return new SearchResponse<T>
        {
            Results = results,
            TotalCount = totalCount,
            MaxScore = score,
            SearchTime = sw.Elapsed
        };
    }

    public Task DeleteAsync(string id, CancellationToken ct = default) =>
        SerializedWrite(async transaction =>
        {
            // Delete from main table (FTS will be automatically updated due to triggers)
            var sql = $"DELETE FROM {TableName} WHERE id = @id";

            await using var cmd = new SqliteCommand(sql, transaction.Connection, transaction);
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync(ct);
        }, ct);

    public Task ClearAsync(CancellationToken ct = default) =>
        SerializedWrite(async transaction =>
        {
            var sql = $"""
                       DELETE FROM {TableName};
                       DELETE FROM {FtsTableName};
                       """;

            await using var cmd = new SqliteCommand(sql, transaction.Connection, transaction);
            await cmd.ExecuteNonQueryAsync(ct);
        }, ct);

    public async Task<long> CountAsync(CancellationToken ct = default)
    {
        var sql = $"SELECT COUNT(*) FROM {TableName}";


        await using var cmd = new SqliteCommand(sql, Connection);
        var result = await cmd.ExecuteScalarAsync(ct);

        return Convert.ToInt64(result);
    }

    public async Task DropIndexAsync(CancellationToken ct = default)
    {
        await SerializedWrite(async transaction =>
        {
            var sql = $"""
                       DROP TABLE IF EXISTS {FtsTableName};
                       DROP TABLE IF EXISTS {TableName};
                       """;

            await using var cmd = new SqliteCommand(sql, transaction.Connection, transaction);
            await cmd.ExecuteNonQueryAsync(ct);
        }, ct);

        _manager.Remove(this);
    }

    /// <summary>
    /// SQLite does not support concurrent writes, so we need to serialize write operations
    /// </summary>
    /// <param name="callback">The write operation</param>
    /// <param name="cancellationToken"></param>
    private async Task SerializedWrite(Func<SqliteTransaction, Task> callback,
        CancellationToken cancellationToken)
    {
        await UpdateSemaphore.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                await callback(transaction);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        finally
        {
            UpdateSemaphore.Release();
        }
    }

    private Task EnsureTableExistsAsync(CancellationToken cancellationToken)
    {
        return SerializedWrite(async transaction =>
        {
            var mainTableSql = $"""
                                CREATE TABLE IF NOT EXISTS {TableName} (
                                    id TEXT PRIMARY KEY NOT NULL,
                                    document TEXT NOT NULL,
                                    search_text TEXT NOT NULL
                                );
                                """;

            await using var mainTableCmd = new SqliteCommand(mainTableSql, transaction.Connection, transaction);
            await mainTableCmd.ExecuteNonQueryAsync(cancellationToken);

            // Create FTS table
            var ftsTableSql = $"""
                               CREATE VIRTUAL TABLE IF NOT EXISTS {FtsTableName} USING fts5(id, search_text);
                               """;

            await using var ftsTableCmd = new SqliteCommand(ftsTableSql, transaction.Connection, transaction);
            await ftsTableCmd.ExecuteNonQueryAsync(cancellationToken);

            // Create triggers to maintain FTS index
            var triggersSql = $"""
                               -- Trigger for INSERT
                               CREATE TRIGGER IF NOT EXISTS {TableName}_ai AFTER INSERT ON {TableName} BEGIN
                                   INSERT INTO {FtsTableName}(id, search_text) VALUES (new.id, new.search_text);
                               END;

                               -- Trigger for UPDATE
                               CREATE TRIGGER IF NOT EXISTS {TableName}_au AFTER UPDATE ON {TableName} BEGIN
                                   DELETE FROM {FtsTableName} WHERE rowid = old.id;
                                   INSERT INTO {FtsTableName}(id, search_text) VALUES (new.id, new.search_text);
                               END;

                               -- Trigger for DELETE
                               CREATE TRIGGER IF NOT EXISTS {TableName}_ad AFTER DELETE ON {TableName} BEGIN
                                   DELETE FROM {FtsTableName} WHERE id = old.id;
                               END;
                               """;

            await using var triggersCmd = new SqliteCommand(triggersSql, transaction.Connection, transaction);
            await triggersCmd.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    public static string GetTableName(string collectionName) => $"SearchLite_{typeof(T).Name}_{collectionName}";
}