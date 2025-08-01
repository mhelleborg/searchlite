using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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

        // Act - Find documents where Title contains "Hello"
        var request = new SearchRequest<TestDocument>().Where(d => d.Title.Contains("Hello"));
        var result = await Index.SearchAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Results.Should().HaveCount(2);
        result.Results.Select(r => r.Document!.Id).Should().BeEquivalentTo(new[] { "1", "3" });
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

        // Act - Find documents where Title does NOT contain "Hello"
        var request = new SearchRequest<TestDocument>().Where(d => !d.Title.Contains("Hello"));
        var result = await Index.SearchAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Results.Should().HaveCount(2);
        result.Results.Select(r => r.Document!.Id).Should().BeEquivalentTo(new[] { "2", "4" });
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

        // Act - Find documents where Title contains "hello" (case insensitive)
        var request = new SearchRequest<TestDocument>().Where(d => d.Title.Contains("hello", StringComparison.OrdinalIgnoreCase));
        var result = await Index.SearchAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Results.Should().HaveCount(3);
        result.Results.Select(r => r.Document!.Id).Should().BeEquivalentTo(new[] { "1", "2", "3" });
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

        // Act - Find documents where Title starts with "Alpha"
        var request = new SearchRequest<TestDocument>().Where(d => d.Title.StartsWith("Alpha"));
        var result = await Index.SearchAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Results.Should().HaveCount(2);
        result.Results.Select(r => r.Document!.Id).Should().BeEquivalentTo(new[] { "1", "3" });
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

        // Act - Find documents where Title does NOT start with "Alpha"
        var request = new SearchRequest<TestDocument>().Where(d => !d.Title.StartsWith("Alpha"));
        var result = await Index.SearchAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Results.Should().HaveCount(2);
        result.Results.Select(r => r.Document!.Id).Should().BeEquivalentTo(new[] { "2", "4" });
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

        // Act - Find documents where Title starts with "alpha" (case insensitive)
        var request = new SearchRequest<TestDocument>().Where(d => d.Title.StartsWith("alpha", StringComparison.OrdinalIgnoreCase));
        var result = await Index.SearchAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Results.Should().HaveCount(3);
        result.Results.Select(r => r.Document!.Id).Should().BeEquivalentTo(new[] { "1", "2", "3" });
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

        // Act - Find documents where Title ends with ".txt"
        var request = new SearchRequest<TestDocument>().Where(d => d.Title.EndsWith(".txt"));
        var result = await Index.SearchAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Results.Should().HaveCount(2);
        result.Results.Select(r => r.Document!.Id).Should().BeEquivalentTo(new[] { "1", "3" });
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

        // Act - Find documents where Title does NOT end with ".txt"
        var request = new SearchRequest<TestDocument>().Where(d => !d.Title.EndsWith(".txt"));
        var result = await Index.SearchAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Results.Should().HaveCount(2);
        result.Results.Select(r => r.Document!.Id).Should().BeEquivalentTo(new[] { "2", "4" });
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

        // Act - Find documents where Title ends with ".txt" (case insensitive)
        var request = new SearchRequest<TestDocument>().Where(d => d.Title.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
        var result = await Index.SearchAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Results.Should().HaveCount(2);
        result.Results.Select(r => r.Document!.Id).Should().BeEquivalentTo(new[] { "1", "3" });
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

        // Act - Find documents where (Title starts with "My" AND Description contains "Important") OR Title ends with ".txt"
        var request = new SearchRequest<TestDocument>().Where(d => 
            (d.Title.StartsWith("My") && d.Description!.Contains("Important")) || d.Title.EndsWith(".txt"));
        var result = await Index.SearchAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Results.Should().HaveCount(4); // All documents match: 1,2,4 match first condition, 1,3 match second condition
        result.Results.Select(r => r.Document!.Id).Should().BeEquivalentTo(new[] { "1", "2", "3", "4" });
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
        var emptyContainsRequest = new SearchRequest<TestDocument>().Where(d => d.Title.Contains(""));
        var emptyContainsResult = await Index.SearchAsync(emptyContainsRequest);
        emptyContainsResult.Results.Should().HaveCount(4);

        // Test StartsWith with empty string (should match all)
        var emptyStartsWithRequest = new SearchRequest<TestDocument>().Where(d => d.Title.StartsWith(""));
        var emptyStartsWithResult = await Index.SearchAsync(emptyStartsWithRequest);
        emptyStartsWithResult.Results.Should().HaveCount(4);

        // Test EndsWith with empty string (should match all)  
        var emptyEndsWithRequest = new SearchRequest<TestDocument>().Where(d => d.Title.EndsWith(""));
        var emptyEndsWithResult = await Index.SearchAsync(emptyEndsWithRequest);
        emptyEndsWithResult.Results.Should().HaveCount(4);

        // Test Contains with single character
        var singleCharRequest = new SearchRequest<TestDocument>().Where(d => d.Title.Contains("A"));
        var singleCharResult = await Index.SearchAsync(singleCharRequest);
        singleCharResult.Results.Should().HaveCount(1);
        singleCharResult.Results[0].Document!.Id.Should().Be("2");
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
        var caseSensitiveRequest = new SearchRequest<TestDocument>().Where(d => d.Title.Contains("Test"));
        var caseSensitiveResult = await Index.SearchAsync(caseSensitiveRequest);
        caseSensitiveResult.Results.Should().HaveCount(2);
        caseSensitiveResult.Results.Select(r => r.Document!.Id).Should().BeEquivalentTo(new[] { "1", "4" });

        // Test case-insensitive Contains
        var caseInsensitiveRequest = new SearchRequest<TestDocument>().Where(d => d.Title.Contains("test", StringComparison.OrdinalIgnoreCase));
        var caseInsensitiveResult = await Index.SearchAsync(caseInsensitiveRequest);
        caseInsensitiveResult.Results.Should().HaveCount(4);
        caseInsensitiveResult.Results.Select(r => r.Document!.Id).Should().BeEquivalentTo(new[] { "1", "2", "3", "4" });
    }
}