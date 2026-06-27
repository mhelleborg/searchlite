using System.Diagnostics.CodeAnalysis;
using Testcontainers.MsSql;

namespace SearchLite.SqlServer.Tests.Fixtures;

/// <summary>
/// Spins up a SQL Server container for integration testing.
///
/// IMPORTANT: the default <c>mcr.microsoft.com/mssql/server</c> image does NOT include the
/// SQL Server Full-Text Search component, which the provider relies on for
/// FREETEXTTABLE / CONTAINSTABLE relevance scoring. Filter-only operations work against the
/// stock image, but full-text <c>SearchAsync</c> calls will fail there. To run the full
/// conformance suite, point <see cref="MsSqlBuilder.WithImage"/> at a full-text-enabled image
/// (for example a custom image built FROM the base server image with the full-text feature
/// installed, or the Azure SQL Edge / developer images that bundle it).
/// </summary>
[Collection("sqlserver")]
public class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container;

    [field: AllowNull, MaybeNull] public string ConnectionString => field ??= _container!.GetConnectionString();

    public SqlServerFixture()
    {
        _container = new MsSqlBuilder()
            // Override with a full-text-enabled image to exercise full-text search.
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();
    }

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.StopAsync();
}
