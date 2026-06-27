using SearchLite.SqlServer.Tests.Fixtures;

namespace SearchLite.SqlServer.Tests;

public class IndexTests(SqlServerFixture fixture)
    : SearchLite.Tests.IndexTests(new SearchManager(fixture.ConnectionString)), IClassFixture<SqlServerFixture>;
