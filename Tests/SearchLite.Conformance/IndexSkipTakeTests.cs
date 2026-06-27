using FluentAssertions;

namespace SearchLite.Tests;

public abstract partial class IndexTests
{
    [Fact]
    public async Task SearchAsync_WithSkipZero_ShouldReturnFromBeginning()
    {
        // Arrange
        var docs = Enumerable.Range(1, 5).Select(i => new TestDocument { Id = i.ToString(), Title = $"Doc {i}" });
        await Index.IndexManyAsync(docs);

        // Act
        var request = new SearchRequest<TestDocument>
        {
            Options = new SearchOptions { Skip = 0, Take = 3 }
        };
        var result = await Index.SearchAsync(request);

        // Assert
        result.Results.Should().HaveCount(3);
        result.TotalCount.Should().Be(5);
    }

    [Fact]
    public async Task SearchAsync_WithTakeZero_ShouldReturnNoResults()
    {
        // Arrange
        var docs = Enumerable.Range(1, 5).Select(i => new TestDocument { Id = i.ToString(), Title = $"Doc {i}" });
        await Index.IndexManyAsync(docs);

        // Act
        var request = new SearchRequest<TestDocument>
        {
            Options = new SearchOptions { Skip = 2, Take = 0 }
        };
        var result = await Index.SearchAsync(request);

        // Assert
        result.Results.Should().BeEmpty();
        result.TotalCount.Should().Be(5);
    }

    [Fact]
    public async Task SearchAsync_WithSkipGreaterThanTotal_ShouldReturnEmpty()
    {
        // Arrange
        var docs = Enumerable.Range(1, 5).Select(i => new TestDocument { Id = i.ToString(), Title = $"Doc {i}" });
        await Index.IndexManyAsync(docs);

        // Act
        var request = new SearchRequest<TestDocument>
        {
            Options = new SearchOptions { Skip = 10, Take = 5 }
        };
        var result = await Index.SearchAsync(request);

        // Assert
        result.Results.Should().BeEmpty();
        result.TotalCount.Should().Be(5);
    }

    [Fact]
    public async Task SearchAsync_WithTakeGreaterThanRemaining_ShouldReturnAvailable()
    {
        // Arrange
        var docs = Enumerable.Range(1, 5).Select(i => new TestDocument { Id = i.ToString(), Title = $"Doc {i}" });
        await Index.IndexManyAsync(docs);

        // Act
        var request = new SearchRequest<TestDocument>
        {
            Options = new SearchOptions { Skip = 3, Take = 10 }
        };
        var result = await Index.SearchAsync(request);

        // Assert
        result.Results.Should().HaveCount(2);
        result.TotalCount.Should().Be(5);
    }

    [Fact]
    public async Task SearchAsync_WithSkipEqualsTotal_ShouldReturnEmpty()
    {
        // Arrange
        var docs = Enumerable.Range(1, 5).Select(i => new TestDocument { Id = i.ToString(), Title = $"Doc {i}" });
        await Index.IndexManyAsync(docs);

        // Act
        var request = new SearchRequest<TestDocument>
        {
            Options = new SearchOptions { Skip = 5, Take = 3 }
        };
        var result = await Index.SearchAsync(request);

        // Assert
        result.Results.Should().BeEmpty();
        result.TotalCount.Should().Be(5);
    }
}