using FluentAssertions;
using SearchLite.MariaDb;
using SearchLite.Tests.MariaDb.Fixtures;

namespace SearchLite.Tests.MariaDb;

public class IndexTests(MariaDbFixture fixture)
    : Tests.IndexTests(new SearchManager(fixture.ConnectionString)), IClassFixture<MariaDbFixture>
{
    [Theory]
    // MariaDB boolean-mode relevance scores are not comparable to other providers, so we only
    // assert the deterministic threshold-of-zero case: every full-text match is returned.
    // Query "Exact match testing" (partial matches on) matches docs 1 ("exact"/"match") and
    // 2 ("match"); doc 3 ("Unrelated document") shares no term.
    [InlineData(2, 0.0)]
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
            Options = new SearchOptions
            {
                MinScore = minScore,
                IncludePartialMatches = true
            }
        };

        var result = await Index.SearchAsync(request);

        result.Results.Should().HaveCount(expectedCount);
    }
}
