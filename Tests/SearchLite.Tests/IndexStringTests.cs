using FluentAssertions;

namespace SearchLite.Tests;

public abstract partial class IndexTests
{
    [Fact]
    public async Task SearchAsync_WithNegatedStringIsNullOrEmpty_ShouldFilterCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument
            {
                Id = "1",
                Title = "Document with null description",
                Description = null
            },
            new TestDocument
            {
                Id = "2",
                Title = "Document with empty description",
                Description = ""
            },
            new TestDocument
            {
                Id = "3",
                Title = "Document with content",
                Description = "Some description"
            },
            new TestDocument
            {
                Id = "4",
                Title = "Document with whitespace description",
                Description = "   "
            }
        };
        await Index.IndexManyAsync(docs);

        // Act & Assert
        await ShouldReturnSameResultsAsLinq(docs, d => !string.IsNullOrEmpty(d.Description), 2);
    }

    [Fact]
    public async Task SearchAsync_WithStringIsNullOrWhiteSpace_ShouldFilterCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument
            {
                Id = "1",
                Title = "Document with null tags",
                Tags = null
            },
            new TestDocument
            {
                Id = "2",
                Title = "Document with empty tags",
                Tags = ""
            },
            new TestDocument
            {
                Id = "3",
                Title = "Document with whitespace tags",
                Tags = "   "
            },
            new TestDocument
            {
                Id = "4",
                Title = "Document with tabs and newlines",
                Tags = "\t\n  \r\n"
            },
            new TestDocument
            {
                Id = "5",
                Title = "Document with real tags",
                Tags = "important,urgent"
            }
        };
        await Index.IndexManyAsync(docs);

        // Act & Assert
        await ShouldReturnSameResultsAsLinq(docs, d => string.IsNullOrWhiteSpace(d.Tags), 4);
    }

    [Fact]
    public async Task SearchAsync_WithNegatedStringIsNullOrWhiteSpace_ShouldFilterCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument
            {
                Id = "1",
                Title = "Document with null tags",
                Tags = null
            },
            new TestDocument
            {
                Id = "2",
                Title = "Document with empty tags",
                Tags = ""
            },
            new TestDocument
            {
                Id = "3",
                Title = "Document with whitespace tags",
                Tags = "   "
            },
            new TestDocument
            {
                Id = "4",
                Title = "Document with real tags",
                Tags = "important,urgent"
            },
            new TestDocument
            {
                Id = "5",
                Title = "Document with single character",
                Tags = "a"
            }
        };
        await Index.IndexManyAsync(docs);

        // Act & Assert
        await ShouldReturnSameResultsAsLinq(docs, d => !string.IsNullOrWhiteSpace(d.Tags), 2);
    }

    [Fact]
    public async Task SearchAsync_WithComplexStringExpression_ShouldFilterCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument
            {
                Id = "1",
                Title = "High views with content",
                Description = "Great content",
                Views = 1000
            },
            new TestDocument
            {
                Id = "2",
                Title = "High views no description",
                Description = null,
                Views = 1500
            },
            new TestDocument
            {
                Id = "3",
                Title = "Low views with content",
                Description = "Some content",
                Views = 50
            },
            new TestDocument
            {
                Id = "4",
                Title = "Low views no description",
                Description = "",
                Views = 25
            }
        };
        await Index.IndexManyAsync(docs);

        // Act & Assert - Find documents with high views OR documents where description is null/empty
        await ShouldReturnSameResultsAsLinq(docs, d => d.Views > 500 || string.IsNullOrEmpty(d.Description), 3);
    }
    
    [Fact]
    public async Task SearchAsync_WithNestedStringExpressions_ShouldFilterCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument
            {
                Id = "1",
                Title = "Complete document",
                Description = "Full description",
                Tags = "complete,full"
            },
            new TestDocument
            {
                Id = "2",
                Title = "Missing description",
                Description = null,
                Tags = "incomplete"
            },
            new TestDocument
            {
                Id = "3",
                Title = "Missing tags",
                Description = "Has description",
                Tags = ""
            },
            new TestDocument
            {
                Id = "4",
                Title = "Missing both",
                Description = null,
                Tags = null
            },
            new TestDocument
            {
                Id = "5",
                Title = "Whitespace only",
                Description = "  ",
                Tags = "\t\n"
            }
        };
        await Index.IndexManyAsync(docs);

        // Act & Assert - Find documents where both Description AND Tags have content (not null/empty/whitespace)
        await ShouldReturnSameResultsAsLinq(docs, d => !string.IsNullOrWhiteSpace(d.Description) && !string.IsNullOrEmpty(d.Tags), 1);
    }

}