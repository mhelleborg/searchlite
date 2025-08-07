using FluentAssertions;

namespace SearchLite.Tests;

public abstract partial class IndexTests
{

    [Fact]
    public async Task SearchAsync_WithInOperator_ShouldFilterCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Doc 1", Views = 100, CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "2", Title = "Doc 2", Views = 200, CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "3", Title = "Doc 3", Views = 300, CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "4", Title = "Doc 4", Views = 400, CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "5", Title = "Doc 5", Views = 500, CreatedAt = DateTime.UtcNow }
        };
        await Index.IndexManyAsync(docs);

        // Act - Find documents with Views in [200, 400]
        var validViews = new[] { 200, 400 };
        var request = new SearchRequest<TestDocument>().Where(d => validViews.Contains(d.Views));
        var result = await Index.SearchAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Results.Should().HaveCount(2);
        result.Results.Select(r => r.Document!.Id).Should().BeEquivalentTo(new[] { "2", "4" });
        result.Results.Select(r => r.Document!.Views).Should().BeEquivalentTo(new[] { 200, 400 });
    }

    [Fact]
    public async Task SearchAsync_WithNotInOperator_ShouldFilterCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Doc 1", Views = 100, CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "2", Title = "Doc 2", Views = 200, CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "3", Title = "Doc 3", Views = 300, CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "4", Title = "Doc 4", Views = 400, CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "5", Title = "Doc 5", Views = 500, CreatedAt = DateTime.UtcNow }
        };
        await Index.IndexManyAsync(docs);

        // Act - Find documents with Views NOT in [200, 400]
        var excludedViews = new[] { 200, 400 };
        var request = new SearchRequest<TestDocument>().Where(d => !excludedViews.Contains(d.Views));
        var result = await Index.SearchAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Results.Should().HaveCount(3);
        result.Results.Select(r => r.Document!.Id).Should().BeEquivalentTo(new[] { "1", "3", "5" });
        result.Results.Select(r => r.Document!.Views).Should().BeEquivalentTo(new[] { 100, 300, 500 });
    }

    [Fact]
    public async Task SearchAsync_WithStringInOperator_ShouldFilterCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Alpha", Views = 100, CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "2", Title = "Beta", Views = 200, CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "3", Title = "Gamma", Views = 300, CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "4", Title = "Delta", Views = 400, CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "5", Title = "Epsilon", Views = 500, CreatedAt = DateTime.UtcNow }
        };
        await Index.IndexManyAsync(docs);

        // Act - Find documents with Title in ["Alpha", "Gamma", "Epsilon"]
        var validTitles = new List<string> { "Alpha", "Gamma", "Epsilon" };
        var request = new SearchRequest<TestDocument>().Where(d => validTitles.Contains(d.Title));
        var result = await Index.SearchAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Results.Should().HaveCount(3);
        result.Results.Select(r => r.Document!.Id).Should().BeEquivalentTo(new[] { "1", "3", "5" });
        result.Results.Select(r => r.Document!.Title).Should().BeEquivalentTo(new[] { "Alpha", "Gamma", "Epsilon" });
    }

    [Fact]
    public async Task SearchAsync_WithEnumerableContains_ShouldFilterCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Doc 1", Views = 10, CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "2", Title = "Doc 2", Views = 20, CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "3", Title = "Doc 3", Views = 30, CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "4", Title = "Doc 4", Views = 40, CreatedAt = DateTime.UtcNow }
        };
        await Index.IndexManyAsync(docs);

        // Act - Find documents using Enumerable.Contains
        var targetViews = new[] { 20, 40 };
        var request = new SearchRequest<TestDocument>().Where(d => Enumerable.Contains(targetViews, d.Views));
        var result = await Index.SearchAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Results.Should().HaveCount(2);
        result.Results.Select(r => r.Document!.Id).Should().BeEquivalentTo(new[] { "2", "4" });
        result.Results.Select(r => r.Document!.Views).Should().BeEquivalentTo(new[] { 20, 40 });
    }
    
    [Fact]
    public async Task SearchAsync_WithMultipleContains_ShouldFilterCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Doc 1", Views = 10, CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "2", Title = "Doc 2", Views = 20, CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "3", Title = "Doc 3", Views = 30, CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "4", Title = "Doc 4", Views = 40, CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "5", Title = "Doc 5", Views = 50, CreatedAt = DateTime.UtcNow }
        };
        await Index.IndexManyAsync(docs);

        // Act - Find documents using Enumerable.Contains
        var targetViews = new[] { 20 };
        var targetViews2 = new[] { 40, 50 };
        var request = new SearchRequest<TestDocument>().Where(d => targetViews.Contains(d.Views) || targetViews2.Contains(d.Views));
        var result = await Index.SearchAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Results.Should().HaveCount(3);
        result.Results.Select(r => r.Document!.Id).Should().BeEquivalentTo("2", "4", "5");
        result.Results.Select(r => r.Document!.Views).Should().BeEquivalentTo([20, 40, 50]);
    }

    [Fact]
    public async Task SearchAsync_WithInOperatorInComplexExpression_ShouldFilterCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Alpha", Views = 100, Description = "Test", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "2", Title = "Beta", Views = 200, Description = null, CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "3", Title = "Gamma", Views = 300, Description = "Test", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "4", Title = "Delta", Views = 400, Description = null, CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "5", Title = "Epsilon", Views = 500, Description = "Test", CreatedAt = DateTime.UtcNow }
        };
        await Index.IndexManyAsync(docs);

        // Act - Find documents with (Views in [200, 400]) AND (Description is null)
        var targetViews = new[] { 200, 400 };
        var request = new SearchRequest<TestDocument>().Where(d => 
            targetViews.Contains(d.Views) && d.Description == null);
        var result = await Index.SearchAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Results.Should().HaveCount(2);
        result.Results.Select(r => r.Document!.Id).Should().BeEquivalentTo(new[] { "2", "4" });
    }
}