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

        // Act & Assert - Find documents with Views in [200, 400]
        var validViews = new[] { 200, 400 };
        await ShouldReturnSameResultsAsLinq(docs, d => validViews.Contains(d.Views), 2);
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

        // Act & Assert - Find documents with Views NOT in [200, 400]
        var excludedViews = new[] { 200, 400 };
        await ShouldReturnSameResultsAsLinq(docs, d => !excludedViews.Contains(d.Views), 3);
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

        // Act & Assert - Find documents with Title in ["Alpha", "Gamma", "Epsilon"]
        var validTitles = new List<string> { "Alpha", "Gamma", "Epsilon" };
        await ShouldReturnSameResultsAsLinq(docs, d => validTitles.Contains(d.Title), 3);
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

        // Act & Assert - Find documents using Enumerable.Contains
        var targetViews = new[] { 20, 40 };
        await ShouldReturnSameResultsAsLinq(docs, d => Enumerable.Contains(targetViews, d.Views), 2);
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

        // Act & Assert - Find documents using Enumerable.Contains
        var targetViews = new[] { 20 };
        var targetViews2 = new[] { 40, 50 };
        await ShouldReturnSameResultsAsLinq(docs, d => targetViews.Contains(d.Views) || targetViews2.Contains(d.Views), 3);
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

        // Act & Assert - Find documents with (Views in [200, 400]) AND (Description is null)
        var targetViews = new[] { 200, 400 };
        await ShouldReturnSameResultsAsLinq(docs, d => 
            targetViews.Contains(d.Views) && d.Description == null, 2);
    }
}