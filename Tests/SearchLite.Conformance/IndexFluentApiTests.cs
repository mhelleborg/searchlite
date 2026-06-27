using FluentAssertions;

namespace SearchLite.Tests;

public abstract partial class IndexTests
{
    [Fact]
    public async Task FluentApi_PropertyReference_ShouldExtractValue()
    {
        // Arrange - Example showing the exact pattern requested by user
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Doc 1", Views = 100, CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "2", Title = "Doc 2", Views = 200, CreatedAt = DateTime.UtcNow.AddDays(-1) },
            new TestDocument { Id = "3", Title = "Doc 3", Views = 300, CreatedAt = DateTime.UtcNow.AddDays(-2) }
        };

        var doc2 = docs[1];
        
        await Index.IndexManyAsync(docs);

        // Act - This demonstrates the exact syntax pattern from the user's example
        var request = new SearchRequest<TestDocument>
        {
            Options = new SearchOptions
            {
                Take = 1,
                IncludeRawDocument = true,
            }
        }.Where(it => it.Views > 150 && it.Id.Equals(doc2.Id));

        var result = await Index.SearchAsync(request);

        // Assert
        result.Results.Should().HaveCount(1);
        result.Results[0].Document!.Id.Should().Be("2");
        result.Results[0].Document!.Views.Should().Be(200);
    }

    [Fact]
    public async Task FluentApi_ChainedWhereWithOptions_ShouldWork()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument
            {
                Id = "1",
                Title = "Alpha Document",
                Content = "Content 1",
                Description = "First document",
                Views = 100,
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            },
            new TestDocument
            {
                Id = "2",
                Title = "Beta Document",
                Content = "Content 2",
                Description = null,
                Views = 200,
                CreatedAt = DateTime.UtcNow.AddDays(-2)
            },
            new TestDocument
            {
                Id = "3",
                Title = "Gamma Document",
                Content = "Content 3",
                Description = "Third document",
                Views = 300,
                CreatedAt = DateTime.UtcNow
            }
        };
        
        await Index.IndexManyAsync(docs);

        // Act - Fluent API pattern with Options initialization and chained Where
        var request = new SearchRequest<TestDocument>
        {
            Options = new SearchOptions
            {
                Take = 1,
                IncludeRawDocument = true,
            }
        }.Where(it => it.Views > 150);

        var result = await Index.SearchAsync(request);

        // Assert
        result.Results.Should().HaveCount(1);
        result.Results[0].Document.Should().NotBeNull();
        result.Results[0].Document!.Views.Should().BeGreaterThan(150); // Should match either doc 2 or 3
    }

    [Fact]
    public async Task FluentApi_ComplexNestedExpressions_ShouldWork()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument
            {
                Id = "1",
                Title = "Important Document",
                Content = "Critical content",
                Description = "High priority item",
                Views = 500,
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            },
            new TestDocument
            {
                Id = "2",
                Title = "Regular Document",
                Content = "Normal content",
                Description = null,
                Views = 200,
                CreatedAt = DateTime.UtcNow.AddDays(-2)
            },
            new TestDocument
            {
                Id = "3",
                Title = "Archive Document",
                Content = "Old content",
                Description = "   ",
                Views = 800,
                CreatedAt = DateTime.UtcNow.AddDays(-10)
            }
        };
        
        await Index.IndexManyAsync(docs);

        // Act - Complex nested expressions with multiple conditions
        var request = new SearchRequest<TestDocument>
        {
            Options = new SearchOptions
            {
                Take = 10,
                IncludeRawDocument = true,
                Skip = 0
            }
        }
        .Where(doc => 
            doc.Title.StartsWith("Important") &&
            doc.Views > 300 &&
            !string.IsNullOrWhiteSpace(doc.Description))
        .OrderByDescending(doc => doc.Views)
        .OrderByAscending(doc => doc.CreatedAt);

        var result = await Index.SearchAsync(request);

        // Assert
        result.Results.Should().HaveCount(1);
        result.Results[0].Document!.Id.Should().Be("1");
        result.Results[0].Document!.Description.Should().Be("High priority item");
    }

    [Fact]
    public async Task FluentApi_MultipleWhereClausesChained_ShouldWork()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument
            {
                Id = "1",
                Title = "Test Document Alpha",
                Content = "Main content",
                Description = "Valid analysis",
                Views = 300,
                CreatedAt = DateTime.UtcNow
            },
            new TestDocument
            {
                Id = "2",
                Title = "Test Document Beta",
                Content = "Secondary content",
                Description = "Another analysis",
                Views = 150,
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            },
            new TestDocument
            {
                Id = "3",
                Title = "Other Document",
                Content = "Different content",
                Description = "Different analysis",
                Views = 400,
                CreatedAt = DateTime.UtcNow.AddDays(-2)
            }
        };
        
        await Index.IndexManyAsync(docs);

        // Act - Multiple chained Where clauses combined into single expression
        var request = new SearchRequest<TestDocument>
        {
            Options = new SearchOptions { Take = 5, IncludeRawDocument = true }
        }
        .Where(x => x.Title.Contains("Test") && x.Views >= 200 && x.Content.StartsWith("Main"));

        var result = await Index.SearchAsync(request);

        // Assert
        result.Results.Should().HaveCount(1);
        result.Results[0].Document!.Id.Should().Be("1");
    }

    [Fact]
    public async Task FluentApi_WithOrderingAndFiltering_ShouldMaintainChainability()
    {
        // Arrange
        var docs = Enumerable.Range(1, 10).Select(i => new TestDocument
        {
            Id = i.ToString(),
            Title = $"Document {i}",
            Content = $"Content {i}",
            Description = i % 2 == 0 ? $"Even doc {i}" : null,
            Views = 50 + (i * 20),
            CreatedAt = DateTime.UtcNow.AddDays(-i)
        }).ToArray();
        
        await Index.IndexManyAsync(docs);

        // Act - Complex chaining with multiple operations
        var result = await Index.SearchAsync(
            new SearchRequest<TestDocument>
            {
                Options = new SearchOptions 
                { 
                    Skip = 1, 
                    Take = 3, 
                    IncludeRawDocument = true 
                }
            }
            .Where(x => x.Views > 150 && x.Title.Contains("Document"))
            .OrderByDescending(x => x.Views)
            .OrderByAscending(x => x.Id)
        );

        // Assert
        result.Results.Should().HaveCount(3);
        result.TotalCount.Should().BeGreaterThan(3);
        
        // Verify ordering - should be ordered by Views desc, then Id asc
        var views = result.Results.Select(r => r.Document!.Views).ToArray();
        views.Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task FluentApi_CompositeExpressionsWithNullChecks_ShouldWork()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument
            {
                Id = "1",
                Title = "First",
                Description = "Valid description",
                Views = 100,
                CreatedAt = DateTime.UtcNow
            },
            new TestDocument
            {
                Id = "2",
                Title = "Second",
                Description = null,
                Views = 200,
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            },
            new TestDocument
            {
                Id = "3",
                Title = "Third",
                Description = "",
                Views = 300,
                CreatedAt = DateTime.UtcNow.AddDays(-2)
            }
        };
        
        await Index.IndexManyAsync(docs);

        // Act - Composite expressions with null checks and fluent chaining
        var request = new SearchRequest<TestDocument>
        {
            Options = new SearchOptions { IncludeRawDocument = true }
        }
        .Where(d => d.Views > 150 && (d.Description == null || string.IsNullOrEmpty(d.Description)))
        .OrderByAscending(d => d.Views);

        var result = await Index.SearchAsync(request);

        // Assert
        result.Results.Should().HaveCount(2); // docs 2 and 3 match
        result.Results[0].Document!.Id.Should().Be("2"); // Views = 200, lowest first
        result.Results[1].Document!.Id.Should().Be("3"); // Views = 300
    }

    [Fact]
    public async Task FluentApi_WithEqualsMethod_ShouldWork()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "John", Views = 100, CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "2", Title = "Jane", Views = 200, CreatedAt = DateTime.UtcNow.AddDays(-1) },
            new TestDocument { Id = "3", Title = "Bob", Views = 300, CreatedAt = DateTime.UtcNow.AddDays(-2) }
        };
        
        await Index.IndexManyAsync(docs);

        // Act - Using .Equals() method in fluent API
        var request = new SearchRequest<TestDocument>
        {
            Options = new SearchOptions { IncludeRawDocument = true }
        }.Where(doc => doc.Title.Equals("Jane") && doc.Views.Equals(200));

        var result = await Index.SearchAsync(request);

        // Assert
        result.Results.Should().HaveCount(1);
        result.Results[0].Document!.Id.Should().Be("2");
        result.Results[0].Document!.Title.Should().Be("Jane");
    }

    [Fact]
    public async Task FluentApi_WithNegatedEqualsMethod_ShouldWork()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "John", Views = 100, CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "2", Title = "Jane", Views = 200, CreatedAt = DateTime.UtcNow.AddDays(-1) },
            new TestDocument { Id = "3", Title = "Bob", Views = 300, CreatedAt = DateTime.UtcNow.AddDays(-2) }
        };
        
        await Index.IndexManyAsync(docs);

        // Act - Using negated .Equals() method
        var request = new SearchRequest<TestDocument>
        {
            Options = new SearchOptions { IncludeRawDocument = true }
        }.Where(doc => !doc.Title.Equals("John") && doc.Views > 150);

        var result = await Index.SearchAsync(request);

        // Assert
        result.Results.Should().HaveCount(2);
        result.Results.Should().NotContain(r => r.Document!.Title == "John");
        result.Results.All(r => r.Document!.Views > 150).Should().BeTrue();
    }

    [Fact]
    public async Task FluentApi_WithCompareToMethod_ShouldWork()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Apple", Views = 100, CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "2", Title = "Banana", Views = 200, CreatedAt = DateTime.UtcNow.AddDays(-1) },
            new TestDocument { Id = "3", Title = "Cherry", Views = 300, CreatedAt = DateTime.UtcNow.AddDays(-2) }
        };
        
        await Index.IndexManyAsync(docs);

        // Act - Using CompareTo method for string comparison
        var request = new SearchRequest<TestDocument>
        {
            Options = new SearchOptions { IncludeRawDocument = true }
        }.Where(doc => doc.Title.CompareTo("Banana") > 0); // Titles > "Banana" alphabetically

        var result = await Index.SearchAsync(request);

        // Assert
        result.Results.Should().HaveCount(1);
        result.Results[0].Document!.Title.Should().Be("Cherry");
    }

    [Fact]
    public async Task FluentApi_WithCompareToEqualZero_ShouldWork()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Test", Views = 100, CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "2", Title = "Demo", Views = 200, CreatedAt = DateTime.UtcNow.AddDays(-1) },
            new TestDocument { Id = "3", Title = "Sample", Views = 300, CreatedAt = DateTime.UtcNow.AddDays(-2) }
        };
        
        await Index.IndexManyAsync(docs);

        // Act - Using CompareTo with equality check
        var request = new SearchRequest<TestDocument>
        {
            Options = new SearchOptions { IncludeRawDocument = true }
        }.Where(doc => doc.Title.CompareTo("Demo") == 0);

        var result = await Index.SearchAsync(request);

        // Assert
        result.Results.Should().HaveCount(1);
        result.Results[0].Document!.Title.Should().Be("Demo");
    }

    [Fact]
    public async Task FluentApi_WithMixedMethodsAndOperators_ShouldWork()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Alpha", Views = 100, Description = "First", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "2", Title = "Beta", Views = 200, Description = "Second", CreatedAt = DateTime.UtcNow.AddDays(-1) },
            new TestDocument { Id = "3", Title = "Gamma", Views = 300, Description = "Third", CreatedAt = DateTime.UtcNow.AddDays(-2) }
        };
        
        await Index.IndexManyAsync(docs);

        // Act - Mix of methods, operators, and string operations
        var request = new SearchRequest<TestDocument>
        {
            Options = new SearchOptions { IncludeRawDocument = true }
        }
        .Where(doc => 
            doc.Title.CompareTo("Beta") >= 0 && 
            doc.Views.Equals(200) && 
            doc.Description!.StartsWith("Se"));

        var result = await Index.SearchAsync(request);

        // Assert
        result.Results.Should().HaveCount(1);
        result.Results[0].Document!.Id.Should().Be("2");
        result.Results[0].Document!.Title.Should().Be("Beta");
    }
}