using FluentAssertions;
using SearchLite.Postgres;
using SearchLite.Tests.Postgres.Fixtures;

namespace SearchLite.Tests.Postgres;

public class IndexTests(PostgresFixture fixture)
    : Tests.IndexTests(new SearchManager(fixture.ConnectionString)), IClassFixture<PostgresFixture>
{
    [Theory]
    [InlineData(0, 0.10)]
    [InlineData(1, 0.05)]
    [InlineData(2, 0.01)]
    public async Task SearchAsync_WithMinScore_ShouldFilterLowScores(int expectedCount, float minScore)
    {
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Exact match test" },
            new TestDocument { Id = "2", Title = "Somewhat related match" },
            new TestDocument { Id = "3", Title = "Unrelated document" }
        };

        await Index.IndexManyAsync(docs);

        var request = new SearchRequest<TestDocument>
        {
            Query = "Exact match testing",
            IncludePartialMatches = true,
            Options = new SearchOptions { MinScore = minScore }
        };

        var result = await Index.SearchAsync(request);


        result.Results.Should().HaveCount(expectedCount);
    }
}