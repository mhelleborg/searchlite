using FluentAssertions;
using SearchLite.DuckDB;

namespace SearchLite.Tests.DuckDB;

public class IndexTests() : Tests.IndexTests(CreateManager())
{
    private static SearchManager CreateManager()
    {
        var extensionDirectory = DuckDbFtsExtension.EnsureAvailable();
        var dbPath = Path.Combine(Path.GetTempPath(), $"searchlite_duckdb_{Guid.NewGuid():N}.duckdb");
        return new SearchManager($"DataSource={dbPath}", extensionDirectory);
    }

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

            Options = new SearchOptions
            {
                MinScore = minScore,
                IncludePartialMatches = true
            }
        };

        var result = await Index.SearchAsync(request);

        result.Results.Should().HaveCount(expectedCount);
    }

    [Fact]
    public async Task SearchAsync_WitIncludePartialMatches_Works()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Exact match test" },
            new TestDocument { Id = "2", Title = "match" },
            new TestDocument { Id = "3", Title = "Unrelated document" }
        };

        await Index.IndexManyAsync(docs);

        // Act
        var request = new SearchRequest<TestDocument>
        {
            Query = "exact match",
            Options = new SearchOptions
            {
                IncludePartialMatches = true
            }
        };
        var result = await Index.SearchAsync(request);

        // Assert
        result.Results.Should().HaveCount(2);
        result.Results.Should().Contain(r => r.Id == "1" && r.Document != null);
        result.Results.Should().Contain(r => r.Id == "2" && r.Document != null);
    }
}
