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
            return field ??= _manager
                .Get<TestDocument>(_collectionName, CancellationToken.None).GetAwaiter().GetResult();
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
        public int Views { get; init; }
        public DateTime CreatedAt { get; set; }
        public string GetSearchText() => $"{Title} {Content}";
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
        var result = await Index.SearchAsync(new SearchRequest<TestDocument>
        {
            Query = "Test"
        });

        var retrievedDoc = await Index.GetAsync(doc.Id);

        // Assert
        result.Results.Should().ContainSingle()
            .Which.Id.Should().Be(doc.Id);

        var searchedDoc = result.Results.Single().Document;

        searchedDoc.Should().NotBeNull();
        searchedDoc.Should().BeEquivalentTo(doc);

        retrievedDoc.Should().NotBeNull();
        retrievedDoc.Should().BeEquivalentTo(doc);
    }

    [Fact]
    public async Task SearchAsync_WithFilters_ShouldReturnMatchingDocuments()
    {
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
        var request = new SearchRequest<TestDocument>()
            .Where(d => d.Views > 500);
        var result = await Index.SearchAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Results.Should().ContainSingle()
            .Which.Document.Should().NotBeNull();
        result.Results.Single().Id.Should().Be("1");
    }

    [Fact]
    public async Task SearchAsync_WithOptions_ShouldRespectMaxResults()
    {
        // Arrange
        var docs = Enumerable.Range(1, 10)
            .Select(i => new TestDocument
            {
                Id = i.ToString(),
                Title = $"Doc {i}"
            });

        await Index.IndexManyAsync(docs);

        // Act
        var request = new SearchRequest<TestDocument>
        {
            Options = new SearchOptions { MaxResults = 5 }
        };
        var result = await Index.SearchAsync(request);

        // Assert
        result.Results.Should().HaveCount(5);
        result.TotalCount.Should().Be(10);
    }

    [Fact]
    public async Task SearchAsync_WithMinScore_ShouldFilterLowScores2()
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Exact match test" },
            new TestDocument { Id = "2", Title = "match" },
            new TestDocument { Id = "3", Title = "Unrelated document" }
        };

        await Index.IndexManyAsync(docs);

        // Act
        var request = new SearchRequest<TestDocument>
        {
            Query = "exact match",
            IncludePartialMatches = true
        };
        var result = await Index.SearchAsync(request);

        // Assert
        result.Results.Should().HaveCount(2);
        result.Results[0].Document.Should().NotBeNull();
        result.Results[0].Id.Should().Be("1");
        result.Results[1].Document.Should().NotBeNull();
        result.Results[1].Id.Should().Be("2");
    }

    [Theory]
    [InlineData("")]
    public async Task SearchAsync_WithEmptyQuery_ShouldReturnAllDocuments(string query)
    {
        // Arrange
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Doc 1" },
            new TestDocument { Id = "2", Title = "Doc 2" }
        };

        await Index.IndexManyAsync(docs);

        // Act
        var request = new SearchRequest<TestDocument> { Query = query };
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
            new TestDocument { Id = "1", Title = "Doc 1" },
            new TestDocument { Id = "2", Title = "Doc 2" }
        };

        await Index.IndexManyAsync(docs);

        // Act
        var request = new SearchRequest<TestDocument>
        {
            Query = "",
            Options = new SearchOptions { MaxResults = 1 }
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
            new TestDocument { Id = "1", Title = "Doc 1" },
            new TestDocument { Id = "2", Title = "Doc 2" }
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
        var doc = new TestDocument { Id = "1", Title = "Test" };
        await Index.IndexAsync(doc);

        // Act
        await Index.DeleteAsync(doc.Id);
        var result = await Index.SearchAsync(new SearchRequest<TestDocument>
        {
            Query = "Test"
        });

        // Assert
        result.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task ClearAsync_ShouldRemoveDocuments()
    {
        // Arrange
        var doc = new TestDocument { Id = "1", Title = "Test" };
        await Index.IndexAsync(doc);

        // Act
        await Index.ClearAsync();
        var result = await Index.SearchAsync(new SearchRequest<TestDocument>());

        // Assert
        result.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task Performance_ShouldHandleBulkOperations()
    {
        // Arrange
        var docs = Enumerable.Range(1, 1000)
            .Select(i => new TestDocument
            {
                Id = i.ToString(),
                Title = $"Bulk Test {i}",
                Content = $"Content {i}"
            });

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
        var collectionNames = Enumerable.Range(1, collectionCount)
            .Select(it => Guid.NewGuid().ToString("N")[..6]);

        // Act & Assert
        await Task.WhenAll(collectionNames.Select(async i =>
        {
            await Task.Yield();
            var index = await _manager.Get<TestDocument>(i.ToString(), CancellationToken.None);

            await Task.WhenAll(Enumerable.Range(1, docCountPerCollection)
                .Select(async j =>
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
}