using FluentAssertions;
using SearchLite.Sqlite;

namespace SearchLite.Tests.Sqlite;

public class IndexTests() : Tests.IndexTests(new SearchManager($"Data Source={Path.GetTempFileName()};"))
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
        result.Results[0].Document.Should().NotBeNull();
        result.Results[0].Id.Should().Be("1");
        result.Results[1].Document.Should().NotBeNull();
        result.Results[1].Id.Should().Be("2");
    }
}