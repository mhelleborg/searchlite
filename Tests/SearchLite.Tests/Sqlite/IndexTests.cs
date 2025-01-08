using FluentAssertions;
using SearchLite.Sqlite;

namespace SearchLite.Tests.Sqlite;

public class IndexTests() : Tests.IndexTests(new SearchManager("Data Source=sharedmemdb;Mode=Memory;Cache=Shared"))
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0.4)]
    [InlineData(2, 0.000000001)]
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