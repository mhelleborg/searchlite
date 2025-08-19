using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Text.Json.Serialization;
using FluentAssertions;

namespace SearchLite.Tests;

public abstract partial class IndexTests
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
        public bool IsRegistered { get; init; } = false;
        public bool? Valid { get; init; }
        public string Content { get; init; } = "";
        public string? Description { get; init; }
        public string? Tags { get; init; }
        public int Views { get; init; }
        public DateTime CreatedAt { get; set; }
        public Guid UniqueId { get; init; } = Guid.NewGuid();
        public DocumentStatus Status { get; init; } = DocumentStatus.Draft;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public Priority? CurrentPriority { get; init; }

        public DangerZone DangerLevel { get; init; } = DangerZone.Low;

        public string GetSearchText() => $"{Title} {Content} {Description} {Tags}";
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DocumentStatus
    {
        Draft = 0,
        Published = 1,
        Archived = 2,
        Deleted = 3
    }

    public enum Priority
    {
        Low = 1,
        Medium = 2,
        High = 3,
        Critical = 4
    }

    public enum DangerZone
    {
        Low = 5,
        Medium = 20,
        High = 100,
        Archer = 1000
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
        // Act & Assert
        await ShouldReturnSameResultsAsLinq(docs, d => d.Views > 500, 1);

        var result = await Index.SearchAsync(new SearchRequest<TestDocument>().Where(d => d.Views > 500));
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

        // Act & Assert
        await ShouldReturnSameResultsAsLinq(docs, d => string.IsNullOrEmpty(d.Description), 2);
    }

    [Fact]
    public async Task SearchAsync_WithStringNotIsNullOrEmpty_ShouldFilterCorrectly()
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

        // Act & Assert
        await ShouldReturnSameResultsAsLinq(docs, d => string.IsNullOrEmpty(d.Description) && d.Title == "it", 2);
    }

    [Fact]
    public async Task SearchAsync_WithCompositeExpressionIncludingNull_ShouldFilterCorrectly()
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

        // Act & Assert
        await ShouldReturnSameResultsAsLinq(docs, d => d.Description == null && d.Title == "it", 1);
    }

    [Fact]
    public async Task SearchAsync_WithNotNullComparison_ShouldFilterCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument
            {
                Id = "1",
                Title = "Has description",
                Description = "Some content"
            },
            new TestDocument
            {
                Id = "2",
                Title = "Null description",
                Description = null
            },
            new TestDocument
            {
                Id = "3",
                Title = "Empty description",
                Description = ""
            }
        };
        await Index.IndexManyAsync(docs);

        // Act & Assert
        await ShouldReturnSameResultsAsLinq(docs, d => d.Description != null, 2);
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

        // Act & Assert
        await ShouldReturnSameResultsAsLinq(docs, d =>
            (d.Views > 800 && !string.IsNullOrWhiteSpace(d.Description)) ||
            (string.IsNullOrEmpty(d.Description) && string.IsNullOrEmpty(d.Tags)), 2);
    }


    [Fact]
    public async Task SearchAsync_OrderByNullValues_ShouldHandleNullsCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument
            {
                Id = "1",
                Title = "Has description",
                Description = "Z content",
                Views = 100
            },
            new TestDocument
            {
                Id = "2",
                Title = "Null description",
                Description = null,
                Views = 200
            },
            new TestDocument
            {
                Id = "3",
                Title = "Another description",
                Description = "A content",
                Views = 150
            }
        };
        await Index.IndexManyAsync(docs);

        // Act - Order by description (nulls should come first or last depending on implementation)
        var request = new SearchRequest<TestDocument>().OrderByAscending(d => d.Description);
        var result = await Index.SearchAsync(request);

        // Assert
        result.Results.Should().HaveCount(3);
        // Null values handling varies by database, but order should be consistent
        var descriptions = result.Results.Select(r => r.Document!.Description).ToList();
        descriptions.Should().Equal(descriptions.OrderBy(d => d));
    }

    [Fact]
    public async Task SearchAsync_OrderByWithIdenticalValues_ShouldMaintainStableSort()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument
            {
                Id = "1",
                Title = "First",
                Views = 100,
                CreatedAt = DateTime.UtcNow.AddMinutes(-10)
            },
            new TestDocument
            {
                Id = "2",
                Title = "Second",
                Views = 100,
                CreatedAt = DateTime.UtcNow.AddMinutes(-5)
            },
            new TestDocument
            {
                Id = "3",
                Title = "Third",
                Views = 100,
                CreatedAt = DateTime.UtcNow
            }
        };
        await Index.IndexManyAsync(docs);

        // Act - Order by Views (all identical), then by CreatedAt for tie-breaking
        var request = new SearchRequest<TestDocument>()
            .OrderByAscending(d => d.Views)
            .OrderByAscending(d => d.CreatedAt);
        var result = await Index.SearchAsync(request);

        // Assert
        result.Results.Should().HaveCount(3);
        result.Results.Select(r => r.Document!.CreatedAt).Should().BeInAscendingOrder();
        result.Results[0].Document!.Id.Should().Be("1");
        result.Results[1].Document!.Id.Should().Be("2");
        result.Results[2].Document!.Id.Should().Be("3");
    }

    [Fact]
    public async Task SearchAsync_OrderByMultipleFields_WithMixedNullValues_ShouldHandleCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument
            {
                Id = "1",
                Title = "High views, no description",
                Description = null,
                Views = 500
            },
            new TestDocument
            {
                Id = "2",
                Title = "High views, with description",
                Description = "Some content",
                Views = 500
            },
            new TestDocument
            {
                Id = "3",
                Title = "Low views, no description",
                Description = null,
                Views = 100
            },
            new TestDocument
            {
                Id = "4",
                Title = "Low views, with description",
                Description = "Other content",
                Views = 100
            }
        };
        await Index.IndexManyAsync(docs);

        // Act - Order by Views descending, then by Description ascending
        var request = new SearchRequest<TestDocument>()
            .OrderByDescending(d => d.Views)
            .OrderByAscending(d => d.Description);
        var result = await Index.SearchAsync(request);

        // Assert
        result.Results.Should().HaveCount(4);
        // First two should have Views = 500, last two should have Views = 100
        result.Results[0].Document!.Views.Should().Be(500);
        result.Results[1].Document!.Views.Should().Be(500);
        result.Results[2].Document!.Views.Should().Be(100);
        result.Results[3].Document!.Views.Should().Be(100);
    }

    [Fact]
    public async Task SearchAsync_OrderByString_WithCaseAndSpecialChars_ShouldOrderCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument
            {
                Id = "1",
                Title = "zebra",
                Views = 1
            },
            new TestDocument
            {
                Id = "2",
                Title = "Apple",
                Views = 2
            },
            new TestDocument
            {
                Id = "3",
                Title = "apple",
                Views = 3
            },
            new TestDocument
            {
                Id = "4",
                Title = "Zebra",
                Views = 4
            },
            new TestDocument
            {
                Id = "5",
                Title = "123 numeric",
                Views = 5
            }
        };
        await Index.IndexManyAsync(docs);

        // Act - Order by Title ascending
        var request = new SearchRequest<TestDocument>().OrderByAscending(d => d.Title);
        var result = await Index.SearchAsync(request);

        // Assert
        result.Results.Should().HaveCount(5);
        var titles = result.Results.Select(r => r.Document!.Title).ToList();

        // Verify that the database ordering is consistent and logical
        // Numbers should come first, then letters (the exact case order may vary by database)
        titles[0].Should().Be("123 numeric"); // Numbers first

        // The remaining items should be alphabetically ordered (case rules may vary)
        var letterTitles = titles.Skip(1).ToList();
        letterTitles.Should().Contain("Apple");
        letterTitles.Should().Contain("apple");
        letterTitles.Should().Contain("Zebra");
        letterTitles.Should().Contain("zebra");

        // Verify stable sort - results should be deterministic
        var secondResult = await Index.SearchAsync(request);
        var secondTitles = secondResult.Results.Select(r => r.Document!.Title).ToList();
        titles.Should().Equal(secondTitles);
    }

    [Fact]
    public async Task SearchAsync_CombinedFilterOrderPagination_ShouldWorkTogether()
    {
        // Arrange
        var docs = Enumerable.Range(1, 20).Select(i => new TestDocument
        {
            Id = i.ToString(),
            Title = $"Document {i:00}",
            Views = i * 10,
            Description = i % 3 == 0 ? null : $"Content {i}"
        });
        await Index.IndexManyAsync(docs);

        // Act - Filter (Views > 50), Order by Views desc, Skip 3, Take 5
        var request = new SearchRequest<TestDocument>
            {
                Options = new SearchOptions { Skip = 3, Take = 5 }
            }
            .Where(d => d.Views > 50)
            .OrderByDescending(d => d.Views)
            .OrderByAscending(d => d.Title);
        var result = await Index.SearchAsync(request);

        // Assert
        result.Results.Should().HaveCount(5);
        result.TotalCount.Should().Be(15); // Documents with Views > 50 (i=6 to 20, which is 15 documents)
        result.Results.Select(r => r.Document!.Views).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task SearchAsync_FilterWithStringOperatorsPlusPagination_ShouldWorkCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "A", Description = null, Views = 100 },
            new TestDocument { Id = "2", Title = "B", Description = "", Views = 200 },
            new TestDocument { Id = "3", Title = "C", Description = "Content", Views = 300 },
            new TestDocument { Id = "4", Title = "D", Description = null, Views = 400 },
            new TestDocument { Id = "5", Title = "E", Description = "   ", Views = 500 },
            new TestDocument { Id = "6", Title = "F", Description = "More content", Views = 600 },
            new TestDocument { Id = "7", Title = "G", Description = "", Views = 700 }
        };
        await Index.IndexManyAsync(docs);

        // Act & Assert - Filter by string.IsNullOrEmpty, order by Views, paginate
        await ShouldReturnSameResultsAsLinq(docs, d => string.IsNullOrEmpty(d.Description), 4);

        var request = new SearchRequest<TestDocument>
            {
                Options = new SearchOptions { Skip = 1, Take = 2 }
            }
            .Where(d => string.IsNullOrEmpty(d.Description))
            .OrderByAscending(d => d.Views);
        var result = await Index.SearchAsync(request);

        // Assert pagination specific behavior
        result.Results.Should().HaveCount(2);
        result.TotalCount.Should().Be(4);
        result.Results[0].Document!.Id.Should().Be("2"); // Views = 200
        result.Results[1].Document!.Id.Should().Be("4"); // Views = 400
    }

    [Fact]
    public async Task SearchAsync_StringOperatorEdgeCases_ShouldHandleAllScenarios()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Doc1", Description = null, Tags = null },
            new TestDocument { Id = "2", Title = "Doc2", Description = "", Tags = "" },
            new TestDocument { Id = "3", Title = "Doc3", Description = " ", Tags = "\t" },
            new TestDocument { Id = "4", Title = "Doc4", Description = "\n\r", Tags = "  \n  " },
            new TestDocument { Id = "5", Title = "Doc5", Description = "actual content", Tags = "real tags" },
            new TestDocument { Id = "6", Title = "Doc6", Description = "0", Tags = "0" } // Edge case: single character
        };
        await Index.IndexManyAsync(docs);

        // Test IsNullOrEmpty
        await ShouldReturnSameResultsAsLinq(docs, d => string.IsNullOrEmpty(d.Description), 2);

        // Test IsNullOrWhiteSpace
        await ShouldReturnSameResultsAsLinq(docs, d => string.IsNullOrWhiteSpace(d.Tags), 4);

        // Test negated IsNullOrEmpty
        await ShouldReturnSameResultsAsLinq(docs, d => !string.IsNullOrEmpty(d.Description), 4);
    }

    [Fact]
    public async Task SearchAsync_WithGuidFilter_ShouldFilterCorrectly()
    {
        // Arrange
        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();
        var guid3 = Guid.NewGuid();

        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Doc1", UniqueId = guid1, Status = DocumentStatus.Draft },
            new TestDocument { Id = "2", Title = "Doc2", UniqueId = guid2, Status = DocumentStatus.Published },
            new TestDocument { Id = "3", Title = "Doc3", UniqueId = guid3, Status = DocumentStatus.Archived }
        };
        await Index.IndexManyAsync(docs);

        // Test GUID equality
        await ShouldReturnSameResultsAsLinq(docs, d => d.UniqueId == guid1, 1);

        // Test GUID inequality
        await ShouldReturnSameResultsAsLinq(docs, d => d.UniqueId != guid1, 2);

        // Test GUID in collection
        var guidCollection = new[] { guid1, guid3 };
        await ShouldReturnSameResultsAsLinq(docs, d => guidCollection.Contains(d.UniqueId), 2);
    }

    [Fact]
    public async Task SearchAsync_WithEnumFilter_ShouldFilterCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument
                { Id = "1", Title = "Doc1", Status = DocumentStatus.Draft, CurrentPriority = Priority.Low },
            new TestDocument
                { Id = "2", Title = "Doc2", Status = DocumentStatus.Published, CurrentPriority = Priority.High },
            new TestDocument
                { Id = "3", Title = "Doc3", Status = DocumentStatus.Archived, CurrentPriority = Priority.Medium },
            new TestDocument { Id = "4", Title = "Doc4", Status = DocumentStatus.Published, CurrentPriority = null },
            new TestDocument
                { Id = "5", Title = "Doc5", Status = DocumentStatus.Deleted, CurrentPriority = Priority.Critical }
        };
        await Index.IndexManyAsync(docs);

        // Test enum equality
        await ShouldReturnSameResultsAsLinq(docs, d => d.Status == DocumentStatus.Published, 2);

        // Test enum inequality
        await ShouldReturnSameResultsAsLinq(docs, d => d.Status != DocumentStatus.Draft, 4);

        // Test enum in collection
        var statusCollection = new[] { DocumentStatus.Published, DocumentStatus.Archived };
        await ShouldReturnSameResultsAsLinq(docs, d => statusCollection.Contains(d.Status), 3);
    }

    [Fact]
    public async Task SearchAsync_WithNullableEnumFilter_ShouldFilterCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument
                { Id = "1", Title = "Doc1", Status = DocumentStatus.Draft, CurrentPriority = Priority.Low },
            new TestDocument
                { Id = "2", Title = "Doc2", Status = DocumentStatus.Published, CurrentPriority = Priority.High },
            new TestDocument { Id = "3", Title = "Doc3", Status = DocumentStatus.Archived, CurrentPriority = null },
            new TestDocument { Id = "4", Title = "Doc4", Status = DocumentStatus.Published, CurrentPriority = null }
        };
        await Index.IndexManyAsync(docs);

        // Test nullable enum equality
        await ShouldReturnSameResultsAsLinq(docs, d => d.CurrentPriority == Priority.High, 1);

        // Test nullable enum null comparison
        await ShouldReturnSameResultsAsLinq(docs, d => d.CurrentPriority == null, 2);

        // Test nullable enum not null comparison
        await ShouldReturnSameResultsAsLinq(docs, d => d.CurrentPriority != null, 2);
    }

    [Fact]
    public async Task SearchAsync_WithComplexGuidAndEnumFilter_ShouldFilterCorrectly()
    {
        // Arrange
        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();

        var docs = new[]
        {
            new TestDocument
            {
                Id = "1", Title = "Doc1", UniqueId = guid1, Status = DocumentStatus.Published,
                CurrentPriority = Priority.High
            },
            new TestDocument
            {
                Id = "2", Title = "Doc2", UniqueId = guid2, Status = DocumentStatus.Published,
                CurrentPriority = Priority.Low
            },
            new TestDocument
            {
                Id = "3", Title = "Doc3", UniqueId = Guid.NewGuid(), Status = DocumentStatus.Draft,
                CurrentPriority = Priority.High
            },
            new TestDocument
            {
                Id = "4", Title = "Doc4", UniqueId = Guid.NewGuid(), Status = DocumentStatus.Archived,
                CurrentPriority = null
            }
        };
        await Index.IndexManyAsync(docs);

        // Test complex filter: Published status AND (specific GUID OR high priority)
        await ShouldReturnSameResultsAsLinq(docs, d => d.Status == DocumentStatus.Published &&
                                                       (d.UniqueId == guid1 || d.CurrentPriority == Priority.High), 1);

        // Test complex filter with collections
        var guidCollection = new[] { guid1, guid2 };
        var statusCollection = new[] { DocumentStatus.Published, DocumentStatus.Draft };
        await ShouldReturnSameResultsAsLinq(docs, d => guidCollection.Contains(d.UniqueId) &&
                                                       statusCollection.Contains(d.Status), 2);
    }

    [Fact]
    public async Task SearchAsync_WithIntBasedEnumFilter_ShouldFilterCorrectly()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Doc1", DangerLevel = DangerZone.Low },
            new TestDocument { Id = "2", Title = "Doc2", DangerLevel = DangerZone.Medium },
            new TestDocument { Id = "3", Title = "Doc3", DangerLevel = DangerZone.High },
            new TestDocument { Id = "4", Title = "Doc4", DangerLevel = DangerZone.Archer }
        };
        await Index.IndexManyAsync(docs);

        // Test enum equality
        await ShouldReturnSameResultsAsLinq(docs, d => d.DangerLevel == DangerZone.High, 1);

        // Test enum greater than
        await ShouldReturnSameResultsAsLinq(docs, d => d.DangerLevel > DangerZone.Medium, 2);
        await ShouldReturnSameResultsAsLinq(docs, d => d.DangerLevel >= DangerZone.Medium, 3);

        // Test enum less than
        await ShouldReturnSameResultsAsLinq(docs, d => d.DangerLevel < DangerZone.Archer, 3);
        await ShouldReturnSameResultsAsLinq(docs, d => d.DangerLevel <= DangerZone.High, 3);
        await ShouldReturnSameResultsAsLinq(docs, d => d.DangerLevel <= DangerZone.High, d => d.DangerLevel < DangerZone.Archer);
    }

    private async Task ShouldReturnSameResultsAsLinq(TestDocument[] documents,
        params Expression<Func<TestDocument, bool>>[] expressions)
    {
        if (documents.Length == 0)
        {
            throw new ArgumentException("Documents cannot be empty");
        }

        IEnumerable<TestDocument> expected = documents; 
        var searchRequest = new SearchRequest<TestDocument>();
        foreach (var expression in expressions)
        {
            var compiledExpression = expression.Compile();
            expected = expected.Where(compiledExpression);
            searchRequest = searchRequest.Where(expression);
        }

        var linqFilteredDocs = expected.ToList();
        if (linqFilteredDocs.Count == 0)
        {
            throw new InvalidOperationException("No documents matched the filter");
        }


        var searchedDocs = await Index.SearchAsync(searchRequest);
        searchedDocs.Results.Should().HaveCount(linqFilteredDocs.Count);
        searchedDocs.Results.Select(r => r.Document!.Id).Should().BeEquivalentTo(linqFilteredDocs.Select(d => d.Id));
    }
    
    private async Task ShouldReturnSameResultsAsLinq(TestDocument[] documents,
        Expression<Func<TestDocument, bool>> expression,
        int? expectedCount = null)
    {
        if (documents.Length == 0)
        {
            throw new ArgumentException("Documents cannot be empty");
        }

        var compiledExpression = expression.Compile();
        var linqFilteredDocs = documents.Where(compiledExpression).ToList();
        if (expectedCount is not null)
        {
            if (linqFilteredDocs.Count != expectedCount)
            {
                throw new InvalidOperationException(
                    $"Expected {expectedCount} documents, but found {linqFilteredDocs.Count}");
            }
        }
        else if (linqFilteredDocs.Count == 0)
        {
            throw new InvalidOperationException("No documents matched the filter");
        }


        var searchedDocs = await Index.SearchAsync(new SearchRequest<TestDocument>().Where(expression));
        searchedDocs.Results.Should().HaveCount(linqFilteredDocs.Count);
        searchedDocs.Results.Select(r => r.Document!.Id).Should().BeEquivalentTo(linqFilteredDocs.Select(d => d.Id));
    }
}