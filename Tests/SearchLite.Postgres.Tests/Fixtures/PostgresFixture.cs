using Testcontainers.PostgreSql;

namespace SearchLite.Tests.Postgres.Fixtures;

[Collection("postgres")]
public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer? _container;

    public string ConnectionString { get; private set; }

    public PostgresFixture()
    {
        // Allow pointing the suite at an already-running PostgreSQL via an env var, so it can be
        // run without Docker. When unset, a throwaway Testcontainers PostgreSQL is used (the CI path).
        var external = Environment.GetEnvironmentVariable("SEARCHLITE_POSTGRES_CONNSTR");
        if (!string.IsNullOrEmpty(external))
        {
            ConnectionString = external;
            return;
        }

        ConnectionString = null!;
        var userName = Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        var dbName = Guid.NewGuid().ToString("N");

        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithUsername(userName)
            .WithPassword(password)
            .WithDatabase(dbName)
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
