using System.Diagnostics.CodeAnalysis;
using Testcontainers.MariaDb;

namespace SearchLite.Tests.MariaDb.Fixtures;

[Collection("mariadb")]
public class MariaDbFixture : IAsyncLifetime
{
    private readonly MariaDbContainer _container;

    [field: AllowNull, MaybeNull] public string ConnectionString => field ??= _container!.GetConnectionString();

    public MariaDbFixture()
    {
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
            // Both the InnoDB (innodb_ft_min_token_size) and MyISAM (ft_min_word_len) knobs are set
            // for completeness.
            .WithCommand(
                "--innodb-ft-min-token-size=1",
                "--ft-min-word-len=1")
            .Build();
    }

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.StopAsync();
}
