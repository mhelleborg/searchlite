using FluentAssertions;

namespace SearchLite.Tests;

public abstract partial class IndexTests
{
    [Fact]
    public async Task SearchAsync_WithStringContains_ShouldFilterCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Hello World", Description = "Test content", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "2", Title = "Goodbye World", Description = "Different content", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "3", Title = "Hello Universe", Description = "Another test", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "4", Title = "Farewell", Description = "Final content", CreatedAt = DateTime.UtcNow }
        };
        await Index.IndexManyAsync(docs);

        // Act & Assert - Find documents where Title contains "Hello"
        await ShouldReturnSameResultsAsLinq(docs, d => d.Title.Contains("Hello"), 2);
    }

    [Fact]
    public async Task SearchAsync_WithStringNotContains_ShouldFilterCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Hello World", Description = "Test content", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "2", Title = "Goodbye World", Description = "Different content", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "3", Title = "Hello Universe", Description = "Another test", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "4", Title = "Farewell", Description = "Final content", CreatedAt = DateTime.UtcNow }
        };
        await Index.IndexManyAsync(docs);

        // Act & Assert - Find documents where Title does NOT contain "Hello"
        await ShouldReturnSameResultsAsLinq(docs, d => !d.Title.Contains("Hello"), 2);
    }

    [Fact]
    public async Task SearchAsync_WithStringContainsIgnoreCase_ShouldFilterCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Hello World", Description = "Test content", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "2", Title = "HELLO UNIVERSE", Description = "Different content", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "3", Title = "hello galaxy", Description = "Another test", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "4", Title = "Farewell", Description = "Final content", CreatedAt = DateTime.UtcNow }
        };
        await Index.IndexManyAsync(docs);

        // Act & Assert - Find documents where Title contains "hello" (case insensitive)
        await ShouldReturnSameResultsAsLinq(docs, d => d.Title.Contains("hello", StringComparison.OrdinalIgnoreCase), 3);
    }

    [Fact]
    public async Task SearchAsync_WithStringStartsWith_ShouldFilterCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Alpha Test", Description = "First", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "2", Title = "Beta Test", Description = "Second", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "3", Title = "Alpha Version", Description = "Third", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "4", Title = "Gamma Test", Description = "Fourth", CreatedAt = DateTime.UtcNow }
        };
        await Index.IndexManyAsync(docs);

        // Act & Assert - Find documents where Title starts with "Alpha"
        await ShouldReturnSameResultsAsLinq(docs, d => d.Title.StartsWith("Alpha"), 2);
    }

    [Fact]
    public async Task SearchAsync_WithStringNotStartsWith_ShouldFilterCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Alpha Test", Description = "First", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "2", Title = "Beta Test", Description = "Second", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "3", Title = "Alpha Version", Description = "Third", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "4", Title = "Gamma Test", Description = "Fourth", CreatedAt = DateTime.UtcNow }
        };
        await Index.IndexManyAsync(docs);

        // Act & Assert - Find documents where Title does NOT start with "Alpha"
        await ShouldReturnSameResultsAsLinq(docs, d => !d.Title.StartsWith("Alpha"), 2);
    }

    [Fact]
    public async Task SearchAsync_WithStringStartsWithIgnoreCase_ShouldFilterCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Alpha Test", Description = "First", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "2", Title = "ALPHA VERSION", Description = "Second", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "3", Title = "alpha development", Description = "Third", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "4", Title = "Beta Test", Description = "Fourth", CreatedAt = DateTime.UtcNow }
        };
        await Index.IndexManyAsync(docs);

        // Act & Assert - Find documents where Title starts with "alpha" (case insensitive)
        await ShouldReturnSameResultsAsLinq(docs, d => d.Title.StartsWith("alpha", StringComparison.OrdinalIgnoreCase), 3);
    }

    [Fact]
    public async Task SearchAsync_WithStringEndsWith_ShouldFilterCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Document.txt", Description = "First", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "2", Title = "Image.jpg", Description = "Second", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "3", Title = "Backup.txt", Description = "Third", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "4", Title = "Config.xml", Description = "Fourth", CreatedAt = DateTime.UtcNow }
        };
        await Index.IndexManyAsync(docs);

        // Act & Assert - Find documents where Title ends with ".txt"
        await ShouldReturnSameResultsAsLinq(docs, d => d.Title.EndsWith(".txt"), 2);
    }

    [Fact]
    public async Task SearchAsync_WithStringNotEndsWith_ShouldFilterCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Document.txt", Description = "First", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "2", Title = "Image.jpg", Description = "Second", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "3", Title = "Backup.txt", Description = "Third", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "4", Title = "Config.xml", Description = "Fourth", CreatedAt = DateTime.UtcNow }
        };
        await Index.IndexManyAsync(docs);

        // Act & Assert - Find documents where Title does NOT end with ".txt"
        await ShouldReturnSameResultsAsLinq(docs, d => !d.Title.EndsWith(".txt"), 2);
    }

    [Fact]
    public async Task SearchAsync_WithStringEndsWithIgnoreCase_ShouldFilterCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Document.TXT", Description = "First", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "2", Title = "Image.JPG", Description = "Second", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "3", Title = "Backup.txt", Description = "Third", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "4", Title = "Config.XML", Description = "Fourth", CreatedAt = DateTime.UtcNow }
        };
        await Index.IndexManyAsync(docs);

        // Act & Assert - Find documents where Title ends with ".txt" (case insensitive)
        await ShouldReturnSameResultsAsLinq(docs, d => d.Title.EndsWith(".txt", StringComparison.OrdinalIgnoreCase), 2);
    }

    [Fact]
    public async Task SearchAsync_WithComplexStringOperatorExpression_ShouldFilterCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "MyFile.txt", Description = "Important document", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "2", Title = "MyImage.jpg", Description = "Important image", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "3", Title = "OtherFile.txt", Description = "Regular document", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "4", Title = "MyData.xml", Description = "Important data", CreatedAt = DateTime.UtcNow }
        };
        await Index.IndexManyAsync(docs);

        // Act & Assert - Find documents where (Title starts with "My" AND Description contains "Important") OR Title ends with ".txt"
        await ShouldReturnSameResultsAsLinq(docs, d => 
            (d.Title.StartsWith("My") && d.Description!.Contains("Important")) || d.Title.EndsWith(".txt"), 4);
    }

    [Fact]
    public async Task SearchAsync_WithStringOperatorEdgeCases_ShouldHandleCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "", Description = "Empty title", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "2", Title = "A", Description = "Single char", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "3", Title = "   ", Description = "Whitespace title", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "4", Title = "Normal Title", Description = "Regular", CreatedAt = DateTime.UtcNow }
        };
        await Index.IndexManyAsync(docs);

        // Test Contains with empty string (should match all)
        await ShouldReturnSameResultsAsLinq(docs, d => d.Title.Contains(""), 4);

        // Test StartsWith with empty string (should match all)
        await ShouldReturnSameResultsAsLinq(docs, d => d.Title.StartsWith(""), 4);

        // Test EndsWith with empty string (should match all)  
        await ShouldReturnSameResultsAsLinq(docs, d => d.Title.EndsWith(""), 4);

        // Test Contains with single character
        await ShouldReturnSameResultsAsLinq(docs, d => d.Title.Contains("A"), 1);
    }

    [Fact]
    public async Task SearchAsync_WithMixedCaseStringOperators_ShouldRespectCaseSensitivity()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "TestCase", Description = "Mixed case", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "2", Title = "TESTCASE", Description = "Upper case", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "3", Title = "testcase", Description = "Lower case", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "4", Title = "TestCASE", Description = "Mixed different", CreatedAt = DateTime.UtcNow }
        };
        await Index.IndexManyAsync(docs);

        // Test case-sensitive Contains
        await ShouldReturnSameResultsAsLinq(docs, d => d.Title.Contains("Test"), 2);

        // Test case-insensitive Contains
        await ShouldReturnSameResultsAsLinq(docs, d => d.Title.Contains("test", StringComparison.OrdinalIgnoreCase), 4);
    }
}