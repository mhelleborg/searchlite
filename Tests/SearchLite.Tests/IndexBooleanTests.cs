using FluentAssertions;

namespace SearchLite.Tests;

public abstract partial class IndexTests
{
    [Fact]
    public async Task SearchAsync_WithBooleanProperties_ShouldFilterCorrectly()
    {
        // Arrange
        var docs = new[] 
        {
            new TestDocument { Id = "1", Title = "Registered User 1", IsRegistered = true, CreatedAt = DateTime.UtcNow , Valid = true},
            new TestDocument { Id = "2", Title = "Unregistered User 1", IsRegistered = false, CreatedAt = DateTime.UtcNow , Valid = true},
            new TestDocument { Id = "3", Title = "Registered User 2", IsRegistered = true, CreatedAt = DateTime.UtcNow , Valid = true},
            new TestDocument { Id = "4", Title = "Unregistered User 2", IsRegistered = false, CreatedAt = DateTime.UtcNow, Valid = true },
            new TestDocument { Id = "5", Title = "Registered User 3", IsRegistered = true, CreatedAt = DateTime.UtcNow , Valid = null},
            new TestDocument { Id = "6", Title = "Unregistered User 3", IsRegistered = false, CreatedAt = DateTime.UtcNow, Valid = null }
        };
        
        await Index.IndexManyAsync(docs);

        // Act & Assert - Find documents where IsRegistered is true
        await ShouldReturnSameResultsAsLinq(docs, d => d.IsRegistered);
        await ShouldReturnSameResultsAsLinq(docs, d => !d.IsRegistered);
        await ShouldReturnSameResultsAsLinq(docs, d => d.IsRegistered == true);
        await ShouldReturnSameResultsAsLinq(docs, d => d.IsRegistered != true);
        await ShouldReturnSameResultsAsLinq(docs, d => d.IsRegistered == false);
        await ShouldReturnSameResultsAsLinq(docs, d => d.IsRegistered != false);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Valid == null);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Valid != null);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Valid == true);
    }

    [Fact]
    public async Task SearchAsync_WithBooleanComplexNesting_ShouldFilterCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Premium Active", IsRegistered = true, Views = 1000, Status = DocumentStatus.Published, Description = "Premium user", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "2", Title = "Basic Active", IsRegistered = false, Views = 500, Status = DocumentStatus.Published, Description = "Basic user", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "3", Title = "Premium Draft", IsRegistered = true, Views = 100, Status = DocumentStatus.Draft, Description = "Premium draft", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "4", Title = "Basic Draft", IsRegistered = false, Views = 50, Status = DocumentStatus.Draft, Description = null, CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "5", Title = "Premium Archived", IsRegistered = true, Views = 2000, Status = DocumentStatus.Archived, Description = "Archived premium", CreatedAt = DateTime.UtcNow }
        };
        await Index.IndexManyAsync(docs);

        // Act & Assert - Complex nested boolean expression:
        // (IsRegistered AND Status == Published AND Views > 800) OR 
        // (!IsRegistered AND Description is null)
        await ShouldReturnSameResultsAsLinq(docs, d => 
            (d.IsRegistered && d.Status == DocumentStatus.Published && d.Views > 800) || 
            (!d.IsRegistered && d.Description == null), 2);
    }
}