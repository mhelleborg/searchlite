using FluentAssertions;

namespace SearchLite.Tests;

public abstract partial class IndexTests
{
    [Fact]
    public async Task DeleteManyAsync_WithValidIds_ShouldRemoveDocuments()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Document 1" },
            new TestDocument { Id = "2", Title = "Document 2" },
            new TestDocument { Id = "3", Title = "Document 3" },
            new TestDocument { Id = "4", Title = "Document 4" },
            new TestDocument { Id = "5", Title = "Document 5" }
        };

        await Index.IndexManyAsync(docs);

        // Act
        var idsToDelete = new[] { "2", "4" };
        await Index.DeleteManyAsync(idsToDelete);

        // Assert
        var remainingCount = await Index.CountAsync();
        remainingCount.Should().Be(3);

        // Verify specific documents are removed
        (await Index.GetAsync("2")).Should().BeNull();
        (await Index.GetAsync("4")).Should().BeNull();

        // Verify remaining documents exist
        (await Index.GetAsync("1")).Should().NotBeNull();
        (await Index.GetAsync("3")).Should().NotBeNull();
        (await Index.GetAsync("5")).Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteManyAsync_WithEmptyCollection_ShouldNotThrow()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Document 1" },
            new TestDocument { Id = "2", Title = "Document 2" }
        };

        await Index.IndexManyAsync(docs);

        // Act & Assert
        await Index.DeleteManyAsync(Array.Empty<string>());
        var count = await Index.CountAsync();
        count.Should().Be(2); // No documents should be deleted
    }

    [Fact]
    public async Task DeleteManyAsync_WithNonExistentIds_ShouldNotThrow()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Document 1" },
            new TestDocument { Id = "2", Title = "Document 2" }
        };

        await Index.IndexManyAsync(docs);

        // Act & Assert
        var nonExistentIds = new[] { "99", "100" };
        await Index.DeleteManyAsync(nonExistentIds);
        var count = await Index.CountAsync();
        count.Should().Be(2); // Original documents should remain
    }

    [Fact]
    public async Task DeleteManyAsync_WithMixOfValidAndInvalidIds_ShouldDeleteOnlyValidOnes()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Document 1" },
            new TestDocument { Id = "2", Title = "Document 2" },
            new TestDocument { Id = "3", Title = "Document 3" }
        };

        await Index.IndexManyAsync(docs);

        // Act
        var mixedIds = new[] { "1", "99", "3", "100" };
        await Index.DeleteManyAsync(mixedIds);

        // Assert
        var remainingCount = await Index.CountAsync();
        remainingCount.Should().Be(1);

        (await Index.GetAsync("1")).Should().BeNull();
        (await Index.GetAsync("2")).Should().NotBeNull(); // Should remain
        (await Index.GetAsync("3")).Should().BeNull();
    }

    [Fact]
    public async Task DeleteWhereAsync_WithTitleFilter_ShouldRemoveMatchingDocuments()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Important Document" },
            new TestDocument { Id = "2", Title = "Important Report" },
            new TestDocument { Id = "3", Title = "Regular Document" },
            new TestDocument { Id = "4", Title = "Important File" },
            new TestDocument { Id = "5", Title = "Normal File" }
        };

        await Index.IndexManyAsync(docs);

        // Act
        var request = new SearchRequest<TestDocument>().Where(d => d.Title.Contains("Important"));
        await Index.DeleteWhereAsync(request);

        // Assert
        var remainingCount = await Index.CountAsync();
        remainingCount.Should().Be(2);

        // Verify documents with "Important" in title are removed
        (await Index.GetAsync("1")).Should().BeNull();
        (await Index.GetAsync("2")).Should().BeNull();
        (await Index.GetAsync("4")).Should().BeNull();

        // Verify other documents remain
        (await Index.GetAsync("3")).Should().NotBeNull();
        (await Index.GetAsync("5")).Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteWhereAsync_WithViewsFilter_ShouldRemoveMatchingDocuments()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Doc 1", Views = 100 },
            new TestDocument { Id = "2", Title = "Doc 2", Views = 250 },
            new TestDocument { Id = "3", Title = "Doc 3", Views = 50 },
            new TestDocument { Id = "4", Title = "Doc 4", Views = 300 },
            new TestDocument { Id = "5", Title = "Doc 5", Views = 150 }
        };

        await Index.IndexManyAsync(docs);

        // Act - Delete documents with views >= 200
        var request = new SearchRequest<TestDocument>().Where(d => d.Views >= 200);
        await Index.DeleteWhereAsync(request);

        // Assert
        var remainingCount = await Index.CountAsync();
        remainingCount.Should().Be(3);

        // Verify high-view documents are removed
        (await Index.GetAsync("2")).Should().BeNull(); // 250 views
        (await Index.GetAsync("4")).Should().BeNull(); // 300 views

        // Verify low-view documents remain
        (await Index.GetAsync("1")).Should().NotBeNull(); // 100 views
        (await Index.GetAsync("3")).Should().NotBeNull(); // 50 views
        (await Index.GetAsync("5")).Should().NotBeNull(); // 150 views
    }

    [Fact]
    public async Task DeleteWhereAsync_WithBooleanFilter_ShouldRemoveMatchingDocuments()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Doc 1", IsRegistered = true },
            new TestDocument { Id = "2", Title = "Doc 2", IsRegistered = false },
            new TestDocument { Id = "3", Title = "Doc 3", IsRegistered = true },
            new TestDocument { Id = "4", Title = "Doc 4", IsRegistered = false }
        };

        await Index.IndexManyAsync(docs);

        // Act - Delete registered documents
        var request = new SearchRequest<TestDocument>().Where(d => d.IsRegistered == true);
        await Index.DeleteWhereAsync(request);

        // Assert
        var remainingCount = await Index.CountAsync();
        remainingCount.Should().Be(2);

        // Verify registered documents are removed
        (await Index.GetAsync("1")).Should().BeNull();
        (await Index.GetAsync("3")).Should().BeNull();

        // Verify unregistered documents remain
        (await Index.GetAsync("2")).Should().NotBeNull();
        (await Index.GetAsync("4")).Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteWhereAsync_WithComplexFilter_ShouldRemoveMatchingDocuments()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Important Doc", Views = 100, IsRegistered = true },
            new TestDocument { Id = "2", Title = "Important Report", Views = 300, IsRegistered = false },
            new TestDocument { Id = "3", Title = "Regular Doc", Views = 150, IsRegistered = true },
            new TestDocument { Id = "4", Title = "Important File", Views = 50, IsRegistered = true },
            new TestDocument { Id = "5", Title = "Normal File", Views = 200, IsRegistered = false }
        };

        await Index.IndexManyAsync(docs);

        // Act - Delete documents that are important AND have high views (>= 100)
        var request = new SearchRequest<TestDocument>()
            .Where(d => d.Title.Contains("Important"))
            .Where(d => d.Views >= 100);
        await Index.DeleteWhereAsync(request);

        // Assert
        var remainingCount = await Index.CountAsync();
        remainingCount.Should().Be(3);

        // Verify matching documents are removed
        (await Index.GetAsync("1")).Should().BeNull(); // Important + 100 views
        (await Index.GetAsync("2")).Should().BeNull(); // Important + 300 views

        // Verify non-matching documents remain
        (await Index.GetAsync("3")).Should().NotBeNull(); // Regular (not Important)
        (await Index.GetAsync("4")).Should().NotBeNull(); // Important but 50 views (< 100)
        (await Index.GetAsync("5")).Should().NotBeNull(); // Normal (not Important)
    }

    [Fact]
    public async Task DeleteWhereAsync_WithNoMatches_ShouldNotDeleteAnyDocuments()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Document 1", Views = 50 },
            new TestDocument { Id = "2", Title = "Document 2", Views = 75 },
            new TestDocument { Id = "3", Title = "Document 3", Views = 25 }
        };

        await Index.IndexManyAsync(docs);

        // Act - Try to delete documents with views > 1000 (none should match)
        var request = new SearchRequest<TestDocument>().Where(d => d.Views > 1000);
        await Index.DeleteWhereAsync(request);

        // Assert
        var remainingCount = await Index.CountAsync();
        remainingCount.Should().Be(3); // All documents should remain

        (await Index.GetAsync("1")).Should().NotBeNull();
        (await Index.GetAsync("2")).Should().NotBeNull();
        (await Index.GetAsync("3")).Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteWhereAsync_WithEmptyFilters_ShouldThrowException()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Document 1" },
            new TestDocument { Id = "2", Title = "Document 2" }
        };

        await Index.IndexManyAsync(docs);

        // Act & Assert - Delete with no filters should throw exception
        var request = new SearchRequest<TestDocument>();
        var act = async () => await Index.DeleteWhereAsync(request);
        
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*DeleteWhereAsync requires at least one filter*Use ClearAsync*");

        // Verify no documents were deleted
        var remainingCount = await Index.CountAsync();
        remainingCount.Should().Be(2);

        (await Index.GetAsync("1")).Should().NotBeNull();
        (await Index.GetAsync("2")).Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteWhereAsync_WithNullableFilter_ShouldRemoveMatchingDocuments()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Doc 1", Description = "Has description" },
            new TestDocument { Id = "2", Title = "Doc 2", Description = null },
            new TestDocument { Id = "3", Title = "Doc 3", Description = "Another description" },
            new TestDocument { Id = "4", Title = "Doc 4", Description = null }
        };

        await Index.IndexManyAsync(docs);

        // Act - Delete documents with null description
        var request = new SearchRequest<TestDocument>().Where(d => d.Description == null);
        await Index.DeleteWhereAsync(request);

        // Assert
        var remainingCount = await Index.CountAsync();
        remainingCount.Should().Be(2);

        // Verify documents with null description are removed
        (await Index.GetAsync("2")).Should().BeNull();
        (await Index.GetAsync("4")).Should().BeNull();

        // Verify documents with description remain
        (await Index.GetAsync("1")).Should().NotBeNull();
        (await Index.GetAsync("3")).Should().NotBeNull();
    }
}