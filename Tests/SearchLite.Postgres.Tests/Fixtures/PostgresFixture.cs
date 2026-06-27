using System.Diagnostics.CodeAnalysis;
using Testcontainers.PostgreSql;

namespace SearchLite.Tests.Postgres.Fixtures;

[Collection("postgres")]
public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container;

    [field: AllowNull, MaybeNull] public string ConnectionString => field ??= _container!.GetConnectionString();

    public PostgresFixture()
    {
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


    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.StopAsync();
}