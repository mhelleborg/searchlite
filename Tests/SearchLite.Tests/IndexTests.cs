using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using FluentAssertions;

namespace SearchLite.Tests;

public abstract class IndexTests
{
    private readonly ISearchEngineManager _manager;
    private readonly string _collectionName = Guid.NewGuid().ToString("N")[..6];

    [field: AllowNull, MaybeNull]
    protected ISearchIndex<TestDocument> Index
    {
        get
        {
            return field ??= _manager.Get<TestDocument>(_collectionName, CancellationToken.None).GetAwaiter()
                .GetResult();
        }
    }

    protected IndexTests(ISearchEngineManager manager)
    {
        _manager = manager;
    }

    public class TestDocument : ISearchableDocument
    {
        public required string Id { get; init; }
        public required string Title { get; init; }
        public string Content { get; init; } = "";
        public string? Description { get; init; }
        public string? Tags { get; init; }
        public int Views { get; init; }
        public DateTime CreatedAt { get; set; }

        public string GetSearchText() => $"{Title} {Content} {Description} {Tags}";
    }

    [Fact]
    public async Task IndexAsync_SingleDocument_ShouldSucceed()
    {
        // Arrange
        var doc = new TestDocument
        {
            Id = Guid.NewGuid().ToString(),
            Title = "Test Title",
            Content = "Test Content",
            CreatedAt = DateTime.UtcNow
        };
        // Act
        await Index.IndexAsync(doc);
        await Index.IndexAsync(doc);
        await Index.IndexAsync(doc);
        var result = await Index.SearchAsync(new SearchRequest<TestDocument> { Query = "Test" });
        var retrievedDoc = await Index.GetAsync(doc.Id);
        // Assert
        result.Results.Should().ContainSingle().Which.Id.Should().Be(doc.Id);
        var searchedDoc = result.Results.Single().Document;
        searchedDoc.Should().NotBeNull();
        searchedDoc.Should().BeEquivalentTo(doc);
        retrievedDoc.Should().NotBeNull();
        retrievedDoc.Should().BeEquivalentTo(doc);

        result.TotalCount.Should().Be(result.Results.Count);
    }

    [Fact]
    public async Task SearchAsync_WithFilters_ShouldReturnMatchingDocuments()
    {
        var before = DateTimeOffset.Now - TimeSpan.FromSeconds(1);
        // Arrange
        var docs = new[]
        {
            new TestDocument
            {
                Id = "1",
                Title = "High Views",
                Views = 1000,
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            },
            new TestDocument
            {
                Id = "2",
                Title = "Low Views",
                Views = 10,
                CreatedAt = DateTime.UtcNow
            }
        };
        await Index.IndexManyAsync(docs);
        // Act
        var request = new SearchRequest<TestDocument>().Where(d => d.Views > 500);
        var result = await Index.SearchAsync(request);
        // Assert
        result.Should().NotBeNull();
        result.Results.Should().ContainSingle().Which.Document.Should().NotBeNull();
        result.Results.Single().Id.Should().Be("1");
        result.Results.Single().LastUpdated.Should().BeAfter(before);
        result.TotalCount.Should().Be(result.Results.Count);
    }

    [Fact]
    public async Task SearchAsync_WithOptions_ShouldRespectMaxResultsWithFullText()
    {
        // Arrange
        var docs = Enumerable.Range(1, 10).Select(i => new TestDocument { Id = i.ToString(), Title = $"Doc {i}" });
        await Index.IndexManyAsync(docs);
        // Act
        var request = new SearchRequest<TestDocument>
        {
            Query = "Doc",
            Options = new SearchOptions
            {
                Take = 5
            }
        };
        var result = await Index.SearchAsync(request);
        // Assert
        result.Results.Should().HaveCount(5);
        result.TotalCount.Should().Be(10);
    }

    [Fact]
    public async Task SearchAsync_WithOptions_ShouldRespectMaxResults()
    {
        // Arrange
        var docs = Enumerable.Range(1, 10).Select(i => new TestDocument { Id = i.ToString(), Title = $"Doc {i}" });
        await Index.IndexManyAsync(docs);
        // Act
        var request = new SearchRequest<TestDocument>
        {
            Options = new SearchOptions
            {
                Take = 5
            }
        };
        var result = await Index.SearchAsync(request);
        // Assert
        result.Results.Should().HaveCount(5);
        result.TotalCount.Should().Be(10);
    }

    [Fact]
    public async Task SearchAsync_WithOptions_ShouldRespectMaxAndSkipWithFullText()
    {
        // Arrange
        var docs = Enumerable.Range(1, 10).Select(i => new TestDocument { Id = i.ToString(), Title = $"Doc {i}" });
        await Index.IndexManyAsync(docs);
        // Act
        var request = new SearchRequest<TestDocument>
        {
            Query = "Doc",
            Options = new SearchOptions
            {
                Skip = 7,
                Take = 5
            }
        };
        var result = await Index.SearchAsync(request);
        // Assert
        result.Results.Should().HaveCount(3);
        result.TotalCount.Should().Be(10);
    }

    [Fact]
    public async Task SearchAsync_WithOptions_ShouldRespectMaxAndSkip()
    {
        // Arrange
        var docs = Enumerable.Range(1, 10).Select(i => new TestDocument { Id = i.ToString(), Title = $"Doc {i}" });
        await Index.IndexManyAsync(docs);
        // Act
        var request = new SearchRequest<TestDocument>
        {
            Options = new SearchOptions
            {
                Skip = 7,
                Take = 5
            }
        };
        var result = await Index.SearchAsync(request);
        // Assert
        result.Results.Should().HaveCount(3);
        result.TotalCount.Should().Be(10);
    }

    [Theory]
    [InlineData("")]
    public async Task SearchAsync_WithEmptyQuery_ShouldReturnAllDocuments(string query)
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument
            {
                Id = "1",
                Title = "Doc 1"
            },
            new TestDocument
            {
                Id = "2",
                Title = "Doc 2"
            }
        };
        await Index.IndexManyAsync(docs);
        // Act
        var request = new SearchRequest<TestDocument>
        {
            Query = query
        };
        var result = await Index.SearchAsync(request);
        // Assert
        result.Results.Should().HaveCount(2);
    }

    [Fact]
    public async Task TotalResults_ShouldReturnTotal()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument
            {
                Id = "1",
                Title = "Doc 1"
            },
            new TestDocument
            {
                Id = "2",
                Title = "Doc 2"
            }
        };
        await Index.IndexManyAsync(docs);
        // Act
        var request = new SearchRequest<TestDocument>
        {
            Query = "",
            Options = new SearchOptions { Take = 1 }
        };
        var result = await Index.SearchAsync(request);
        // Assert
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task CountAsync_ShouldReturnTotalDocuments()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument
            {
                Id = "1",
                Title = "Doc 1"
            },
            new TestDocument
            {
                Id = "2",
                Title = "Doc 2"
            }
        };
        await Index.IndexManyAsync(docs);
        // Act
        var count = await Index.CountAsync();
        // Assert
        count.Should().Be(2);
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveDocument()
    {
        // Arrange
        var doc = new TestDocument
        {
            Id = "1",
            Title = "Test"
        };
        await Index.IndexAsync(doc);
        // Act
        await Index.DeleteAsync(doc.Id);
        var result = await Index.SearchAsync(new SearchRequest<TestDocument> { Query = "Test" });
        // Assert
        result.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task ClearAsync_ShouldRemoveDocuments()
    {
        // Arrange
        var doc = new TestDocument
        {
            Id = "1",
            Title = "Test"
        };
        await Index.IndexAsync(doc);
        // Act
        await Index.ClearAsync();
        var result = await Index.SearchAsync(new SearchRequest<TestDocument>());
        // Assert
        result.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task DropIndexAsync_ShouldRemoveIndex()
    {
        // Arrange
        var doc = new TestDocument
        {
            Id = "1",
            Title = "Test"
        };
        await Index.IndexAsync(doc);
        // Act
        await Index.DropIndexAsync();

        Index.Invoking(it => it.SearchAsync(new SearchRequest<TestDocument>())
                .GetAwaiter().GetResult())
            .Should().Throw<Exception>();
    }

    [Fact]
    public async Task DropIndexAsync_ShouldRemoveIndex_AndMakeItAvailableForReuse()
    {
        // Arrange
        var doc = new TestDocument
        {
            Id = "1",
            Title = "Test"
        };
        await Index.IndexAsync(doc);
        // Act
        await Index.DropIndexAsync();

        var newIndex = await _manager.Get<TestDocument>(_collectionName, CancellationToken.None);

        var result = await newIndex.SearchAsync(new SearchRequest<TestDocument>());
        // Assert
        result.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task Performance_ShouldHandleBulkOperations()
    {
        // Arrange
        var docs = Enumerable.Range(1, 1000).Select(i => new TestDocument
            { Id = i.ToString(), Title = $"Bulk Test {i}", Content = $"Content {i}" });
        // Act
        var sw = Stopwatch.StartNew();
        await Index.IndexManyAsync(docs);
        sw.Stop();
        // Assert
        sw.ElapsedMilliseconds.Should().BeLessThan(5000); // Should complete in 5s
        var result = await Index.SearchAsync(new SearchRequest<TestDocument>());
        result.TotalCount.Should().Be(1000);
    }

    [Theory]
    [InlineData(5, 50)]
    public async Task CanHandleConcurrentWriteOperations(int collectionCount, int docCountPerCollection)
    {
        // Arrange
        var collectionNames = Enumerable.Range(1, collectionCount).Select(_ => Guid.NewGuid().ToString("N")[..6]);
        // Act & Assert
        await Task.WhenAll(collectionNames.Select(async i =>
        {
            await Task.Yield();
            var index = await _manager.Get<TestDocument>(i.ToString(), CancellationToken.None);
            await Task.WhenAll(Enumerable.Range(1, docCountPerCollection).Select(async j =>
            {
                await Task.Yield();
                var id = j.ToString();
                var testDocument = new TestDocument
                {
                    Id = id,
                    Title = $"Doc {j}"
                };
                await index.IndexAsync(testDocument);
            }));
            var count = await index.CountAsync();
            count.Should().Be(docCountPerCollection);
        }));
    }

    [Fact]
    public async Task SearchAsync_OrderByViews_ShouldReturnOrderedResults()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument
            {
                Id = "1",
                Title = "Doc 1",
                Views = 100
            },
            new TestDocument
            {
                Id = "2",
                Title = "Doc 2",
                Views = 300
            },
            new TestDocument
            {
                Id = "3",
                Title = "Doc 3",
                Views = 200
            }
        };
        await Index.IndexManyAsync(docs);
        // Act
        var request = new SearchRequest<TestDocument>().OrderByDescending(it => it.Views);

        var result = await Index.SearchAsync(request);
        // Assert
        result.Results.Should().HaveCount(3);
        result.Results.Select(r => r.Document!.Views).Should().BeInDescendingOrder();
        result.Results[0].Document!.Views.Should().Be(300);
        result.Results[1].Document!.Views.Should().Be(200);
        result.Results[2].Document!.Views.Should().Be(100);
    }

    [Fact]
    public async Task SearchAsync_OrderByCreatedAt_Ascending_ShouldReturnOrderedResults()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var docs = new[]
        {
            new TestDocument
            {
                Id = "1",
                Title = "Doc 1",
                CreatedAt = now.AddDays(-1)
            },
            new TestDocument
            {
                Id = "2",
                Title = "Doc 2",
                CreatedAt = now.AddDays(-3)
            },
            new TestDocument
            {
                Id = "3",
                Title = "Doc 3",
                CreatedAt = now.AddDays(-2)
            }
        };
        await Index.IndexManyAsync(docs);
        // Act
        var request = new SearchRequest<TestDocument>();
        request.OrderBys.Add(new OrderByNode<TestDocument>
            { PropertyName = "CreatedAt", Direction = SortDirection.Ascending });
        var result = await Index.SearchAsync(request);
        // Assert
        result.Results.Should().HaveCount(3);
        result.Results.Select(r => r.Document!.CreatedAt).Should().BeInAscendingOrder();
        result.Results[0].Document!.Id.Should().Be("2");
        result.Results[1].Document!.Id.Should().Be("3");
        result.Results[2].Document!.Id.Should().Be("1");
    }

    [Fact]
    public async Task SearchAsync_MultipleOrderBy_ShouldRespectOrdering()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument
            {
                Id = "1",
                Title = "Doc 1",
                Views = 100,
                Content = "AAA"
            },
            new TestDocument
            {
                Id = "2",
                Title = "Doc 2",
                Views = 100,
                Content = "BBB"
            },
            new TestDocument
            {
                Id = "3",
                Title = "Doc 3",
                Views = 200,
                Content = "CCC"
            }
        };
        await Index.IndexManyAsync(docs);
        // Act
        var request = new SearchRequest<TestDocument>().OrderByDescending(it => it.Views)
            .OrderByAscending(it => it.Content);
        var result = await Index.SearchAsync(request);
        // Assert
        result.Results.Should().HaveCount(3);
        result.Results[0].Document!.Views.Should().Be(200);
        result.Results[1].Document!.Content.Should().Be("AAA");
        result.Results[2].Document!.Content.Should().Be("BBB");
    }

    [Fact]
    public async Task SearchAsync_WithSpecialCharacters_ShouldHandleCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument
            {
                Id = "1",
                Title = "C# Programming",
                Content = "Learn C# & .NET"
            },
            new TestDocument
            {
                Id = "2",
                Title = "SQL*Plus Guide",
                Content = "Oracle & SQL"
            },
            new TestDocument
            {
                Id = "3",
                Title = "Regular Doc",
                Content = "No special chars"
            }
        };
        await Index.IndexManyAsync(docs);
        // Act & Assert
        // Test exact special character search
        var result1 = await Index.SearchAsync(new SearchRequest<TestDocument> { Query = "C#" });
        result1.Results.Should().ContainSingle().Which.Document!.Title.Should().Be("C# Programming");
        // Test mixed special characters
        var result2 = await Index.SearchAsync(new SearchRequest<TestDocument> { Query = "SQL*" });
        result2.Results.Should().ContainSingle().Which.Document!.Title.Should().Be("SQL*Plus Guide");
        // Non-full match
        var result3 = await Index.SearchAsync(new SearchRequest<TestDocument> { Query = "C# .NET" });
        result3.Results.Should().ContainSingle().Which.Document!.Title.Should().Be("C# Programming");
    }

    [Fact]
    public async Task SearchAsync_WithUnicodeCharacters_ShouldHandleCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument
            {
                Id = "1",
                Title = "café",
                Content = "Coffee & café au lait"
            },
            new TestDocument
            {
                Id = "2",
                Title = "résumé",
                Content = "Professional résumé writing"
            },
            new TestDocument
            {
                Id = "3",
                Title = "über facts",
                Content = "Facts about Über services"
            }
        };
        await Index.IndexManyAsync(docs);
        // Act & Assert
        // Test accent-sensitive search
        var result1 = await Index.SearchAsync(new SearchRequest<TestDocument> { Query = "café" });
        result1.Results.Should().ContainSingle().Which.Document!.Title.Should().Be("café");
        // Test mixed case with accents
        var result2 = await Index.SearchAsync(new SearchRequest<TestDocument> { Query = "Über" });
        result2.Results.Should().ContainSingle().Which.Document!.Title.Should().Be("über facts");
    }

    [Fact]
    public async Task SearchAsync_WithExtremelyLongContent_ShouldHandleCorrectly()
    {
        // Arrange
        var longContent = new string('x', 100000); // 100KB content
        var docs = new[]
        {
            new TestDocument
            {
                Id = "1",
                Title = "Long Document",
                Content = longContent + " findme " + longContent
            },
            new TestDocument
            {
                Id = "2",
                Title = "Normal Document",
                Content = "findme"
            }
        };
        await Index.IndexManyAsync(docs);
        // Act
        var result = await Index.SearchAsync(new SearchRequest<TestDocument> { Query = "findme" });
        // Assert
        result.Results.Should().HaveCount(2);
        result.Results.Select(r => r.Document!.Id).Should().Contain(new[] { "1", "2" });
    }

    [Fact]
    public async Task SearchAsync_WithNestedQuotes_ShouldHandleCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument
            {
                Id = "1",
                Title = "Quote Test",
                Content = "He said \"Hello World\" in the program"
            },
            new TestDocument
            {
                Id = "2",
                Title = "Another Quote",
                Content = "The 'quick' brown fox"
            },
            new TestDocument
            {
                Id = "3",
                Title = "Mixed",
                Content = "Its a \"quote\" string's test"
            }
        };
        await Index.IndexManyAsync(docs);
        // Act & Assert
        // Test exact phrase with quotes
        var result1 = await Index.SearchAsync(new SearchRequest<TestDocument> { Query = "\"Hello World\"" });
        result1.Results.Should().ContainSingle().Which.Document!.Id.Should().Be("1");
        // Test mixed quote types
        var result2 = await Index.SearchAsync(new SearchRequest<TestDocument> { Query = "quote" });
        result2.Results.Should().HaveCount(3);
    }

    [Fact]
    public async Task IncludePartialMatchesFalse_shouldNotReturnPartialMatches()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument
            {
                Id = "1",
                Title = "Test Document",
                Content = "test test test test" // Multiple occurrences
            },
            new TestDocument
            {
                Id = "2",
                Title = "Test",
                Content = "Test" // Single occurrence
            },
            new TestDocument
            {
                Id = "3",
                Title = "Test Document",
                Content = "test test" // Double occurrence
            }
        };
        await Index.IndexManyAsync(docs);
        // Act
        var result = await Index.SearchAsync(new SearchRequest<TestDocument>
            { Query = "Test Document", Options = new SearchOptions { IncludePartialMatches = false } });
        // Assert
        result.Results.Should().HaveCount(2);
        result.Results.Select(it => it.Id).Should().BeEquivalentTo("1", "3");
    }

    [Fact]
    public async Task SearchAsync_WithWhitespaceAndPunctuation_ShouldHandleCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument
            {
                Id = "1",
                Title = "   Padded    Spaces   ",
                Content = "Multiple     spaces    between    words"
            },
            new TestDocument
            {
                Id = "2",
                Title = "Punctuation!!!Test???",
                Content = "Hello...World...Test"
            },
            new TestDocument
            {
                Id = "3",
                Title = "Normal Document",
                Content = "Regular content here"
            }
        };
        await Index.IndexManyAsync(docs);
        // Act & Assert
        // Test multiple spaces
        var result1 = await Index.SearchAsync(new SearchRequest<TestDocument> { Query = "Padded     Spaces" });
        result1.Results.Should().ContainSingle().Which.Document!.Id.Should().Be("1");
        // Test excessive punctuation
        var result2 = await Index.SearchAsync(new SearchRequest<TestDocument> { Query = "Punctuation!!!" });
        result2.Results.Should().ContainSingle().Which.Document!.Id.Should().Be("2");
        // Test ellipsis
        var result3 = await Index.SearchAsync(new SearchRequest<TestDocument> { Query = "Hello...World" });
        result3.Results.Should().ContainSingle().Which.Document!.Id.Should().Be("2");
    }

    [Fact]
    public async Task SearchAsync_WithStringIsNullOrEmpty_ShouldFilterCorrectly()
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

        // Act - Find documents where Description is null or empty
        var request = new SearchRequest<TestDocument>().Where(d => string.IsNullOrEmpty(d.Description));
        var result = await Index.SearchAsync(request);

        // Assert
        result.Results.Should().HaveCount(2);
        result.Results.Select(r => r.Id).Should().BeEquivalentTo(new[] { "1", "2" });
    }

    [Fact]
    public async Task SearchAsync_WithCompositeExpression_ShouldFilterCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument
            {
                Id = "1",
                Title = "Not it",
                Description = null,
                Views = 100
            },
            new TestDocument
            {
                Id = "2",
                Title = "it",
                Description = "",
                Views = 200
            },
            new TestDocument
            {
                Id = "3",
                Title = "it",
                Description = "But has a description",
                Views = 50
            },
            new TestDocument
            {
                Id = "4",
                Title = "it",
                Description = null,
                Views = 300
            }
        };
        await Index.IndexManyAsync(docs);

        // Act - Find documents where Description is null/empty OR Views > 250
        var request = new SearchRequest<TestDocument>().Where(d =>
            string.IsNullOrEmpty(d.Description) && d.Title == "it");
        var result = await Index.SearchAsync(request);

        // Assert - Should get docs 1, 2, and 4 (null/empty descriptions + high views)
        result.Results.Should().HaveCount(2);
        result.Results.Select(r => r.Id).Should().BeEquivalentTo("2", "4");
    }

    [Fact]
    public async Task SearchAsync_WithComplexCompositeExpression_ShouldFilterCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument
            {
                Id = "1",
                Title = "High Views Complete",
                Description = "Full description",
                Tags = "complete",
                Views = 1000
            },
            new TestDocument
            {
                Id = "2",
                Title = "High Views Missing Description",
                Description = null,
                Tags = "incomplete",
                Views = 1500
            },
            new TestDocument
            {
                Id = "3",
                Title = "Low Views Complete",
                Description = "Some description",
                Tags = "complete",
                Views = 50
            },
            new TestDocument
            {
                Id = "4",
                Title = "Low Views Missing Both",
                Description = "",
                Tags = null,
                Views = 25
            },
            new TestDocument
            {
                Id = "5",
                Title = "Medium Views Whitespace",
                Description = "   ",
                Tags = "\t\n",
                Views = 500
            }
        };
        await Index.IndexManyAsync(docs);

        // Act - Complex expression: (High views AND has description) OR (Missing both description and tags)
        var request = new SearchRequest<TestDocument>().Where(d =>
            (d.Views > 800 && !string.IsNullOrWhiteSpace(d.Description)) ||
            (string.IsNullOrEmpty(d.Description) && string.IsNullOrEmpty(d.Tags)));
        var result = await Index.SearchAsync(request);

        // Assert - Should get docs 1 (high views + description) and 4 (missing both)
        result.Results.Should().HaveCount(2);
        result.Results.Select(r => r.Id).Should().BeEquivalentTo(new[] { "1", "4" });
    }


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

        // Act - Find documents where Description is NOT null or empty
        var request = new SearchRequest<TestDocument>().Where(d => !string.IsNullOrEmpty(d.Description));
        var result = await Index.SearchAsync(request);

        // Assert
        result.Results.Should().HaveCount(2);
        result.Results.Select(r => r.Id).Should().BeEquivalentTo(new[] { "3", "4" });
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

        // Act - Find documents where Tags is null, empty or whitespace
        var request = new SearchRequest<TestDocument>().Where(d => string.IsNullOrWhiteSpace(d.Tags));
        var result = await Index.SearchAsync(request);

        // Assert
        result.Results.Should().HaveCount(4);
        result.Results.Select(r => r.Id).Should().BeEquivalentTo(new[] { "1", "2", "3", "4" });
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

        // Act - Find documents where Tags is NOT null, empty or whitespace
        var request = new SearchRequest<TestDocument>().Where(d => !string.IsNullOrWhiteSpace(d.Tags));
        var result = await Index.SearchAsync(request);

        // Assert
        result.Results.Should().HaveCount(2);
        result.Results.Select(r => r.Id).Should().BeEquivalentTo(new[] { "4", "5" });
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

        // Act - Find documents with high views OR documents where description is null/empty
        var request = new SearchRequest<TestDocument>()
            .Where(d => d.Views > 500 || string.IsNullOrEmpty(d.Description));
        var result = await Index.SearchAsync(request);

        // Assert - Should get docs 1, 2, and 4
        result.Results.Should().HaveCount(3);
        result.Results.Select(r => r.Id).Should().BeEquivalentTo(new[] { "1", "2", "4" });
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

        // Act - Find documents where both Description AND Tags have content (not null/empty/whitespace)
        var request = new SearchRequest<TestDocument>()
            .Where(d => !string.IsNullOrWhiteSpace(d.Description) && !string.IsNullOrEmpty(d.Tags));
        var result = await Index.SearchAsync(request);

        // Assert - Should only get document 1
        result.Results.Should().ContainSingle();
        result.Results.Single().Id.Should().Be("1");
    }
}