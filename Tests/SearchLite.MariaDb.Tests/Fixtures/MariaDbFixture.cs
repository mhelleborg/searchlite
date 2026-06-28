using Testcontainers.MariaDb;

namespace SearchLite.Tests.MariaDb.Fixtures;

[Collection("mariadb")]
public sealed class MariaDbFixture : IAsyncLifetime
{
    private readonly MariaDbContainer? _container;

    public string ConnectionString { get; private set; }

    public MariaDbFixture()
    {
        // Allow pointing the suite at an already-running MariaDB (e.g. a local server started with
        // innodb_ft_min_token_size=1) via an env var, so it can be run without Docker. When unset,
        // a throwaway Testcontainers MariaDB is used (the CI path).
        var external = Environment.GetEnvironmentVariable("SEARCHLITE_MARIADB_CONNSTR");
        if (!string.IsNullOrEmpty(external))
        {
            ConnectionString = external;
            return;
        }

        ConnectionString = null!;
        var userName = Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        var dbName = Guid.NewGuid().ToString("N");

        _container = new MariaDbBuilder()
            .WithImage("mariadb:11")
            .WithUsername(userName)
            .WithPassword(password)
            .WithDatabase(dbName)
            // InnoDB's default minimum FULLTEXT token size is 3, which would drop the short tokens
            // ("c", "1", "doc"...) the conformance suite searches for. These settings must be in
            // place before any FULLTEXT index is created, so they are passed as server startup
            // arguments; the index is created afterwards by SearchIndex, so it picks up the change.
            .WithCommand(
                "--innodb-ft-min-token-size=1",
                "--ft-min-word-len=1")
            .Build();
    }

    public async Task InitializeAsync()
    {
        if (_container is null)
        {
            return;
        }

        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.StopAsync();
        }
    }
}
