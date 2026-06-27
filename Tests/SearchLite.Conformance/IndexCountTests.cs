using FluentAssertions;

namespace SearchLite.Tests;

public abstract partial class IndexTests
{
    [Fact]
    public async Task CountAsync_OnEmptyIndex_ShouldReturnZero()
    {
        var count = await Index.CountAsync();

        count.Should().Be(0);
    }

    [Fact]
    public async Task CountAsync_AfterIndexing_ShouldReturnTotal()
    {
        var docs = Enumerable.Range(1, 7)
            .Select(i => new TestDocument { Id = i.ToString(), Title = $"Doc {i}" });
        await Index.IndexManyAsync(docs);

        var count = await Index.CountAsync();

        count.Should().Be(7);
    }

    [Fact]
    public async Task CountAsync_ReflectsDeletions()
    {
        var docs = Enumerable.Range(1, 5)
            .Select(i => new TestDocument { Id = i.ToString(), Title = $"Doc {i}" });
        await Index.IndexManyAsync(docs);

        await Index.DeleteAsync("2");
        await Index.DeleteAsync("4");

        (await Index.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task CountAsync_WithFilter_ShouldCountMatchingDocuments()
    {
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Alpha", Views = 100 },
            new TestDocument { Id = "2", Title = "Beta", Views = 200 },
            new TestDocument { Id = "3", Title = "Gamma", Views = 300 },
            new TestDocument { Id = "4", Title = "Delta", Views = 400 }
        };
        await Index.IndexManyAsync(docs);

        var request = new SearchRequest<TestDocument>()
            .Where(d => d.Views >= 200);

        var count = await Index.CountAsync(request);

        count.Should().Be(3);
    }

    [Fact]
    public async Task CountAsync_WithFilterMatchingNone_ShouldReturnZero()
    {
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Alpha", Views = 100 },
            new TestDocument { Id = "2", Title = "Beta", Views = 200 }
        };
        await Index.IndexManyAsync(docs);

        var request = new SearchRequest<TestDocument>()
            .Where(d => d.Views > 1000);

        var count = await Index.CountAsync(request);

        count.Should().Be(0);
    }

    [Fact]
    public async Task CountAsync_WithEmptyRequest_ShouldCountAllDocuments()
    {
        var docs = Enumerable.Range(1, 4)
            .Select(i => new TestDocument { Id = i.ToString(), Title = $"Doc {i}" });
        await Index.IndexManyAsync(docs);

        var count = await Index.CountAsync(new SearchRequest<TestDocument>());

        count.Should().Be(4);
    }

    [Fact]
    public async Task CountAsync_WithCompositeFilter_ShouldCountMatchingDocuments()
    {
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "Apple", Views = 100 },
            new TestDocument { Id = "2", Title = "Apple", Views = 300 },
            new TestDocument { Id = "3", Title = "Banana", Views = 300 },
            new TestDocument { Id = "4", Title = "Apple", Views = 50 }
        };
        await Index.IndexManyAsync(docs);

        var request = new SearchRequest<TestDocument>()
            .Where(d => d.Title == "Apple" && d.Views >= 100);

        var count = await Index.CountAsync(request);

        count.Should().Be(2);
    }

    [Fact]
    public async Task CountAsync_WithFilter_ShouldIgnorePaging()
    {
        var docs = Enumerable.Range(1, 10)
            .Select(i => new TestDocument { Id = i.ToString(), Title = $"Doc {i}", Views = i * 10 });
        await Index.IndexManyAsync(docs);

        var request = new SearchRequest<TestDocument>
        {
            Options = new SearchOptions { Skip = 2, Take = 3 }
        }.Where(d => d.Views >= 30);

        // Count is over all matches, independent of Skip/Take paging.
        var count = await Index.CountAsync(request);

        count.Should().Be(8);
    }

    [Fact]
    public async Task CountAsync_WithTextQuery_ShouldCountFullTextMatches()
    {
        var docs = new[]
        {
            new TestDocument { Id = "1", Title = "raspberry pie recipe" },
            new TestDocument { Id = "2", Title = "raspberry jam" },
            new TestDocument { Id = "3", Title = "blueberry muffins" }
        };
        await Index.IndexManyAsync(docs);

        var request = new SearchRequest<TestDocument>
        {
            Query = "raspberry"
        };

        var count = await Index.CountAsync(request);

        count.Should().Be(2);
    }
}
