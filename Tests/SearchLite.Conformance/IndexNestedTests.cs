using FluentAssertions;

namespace SearchLite.Tests;

/// <summary>
/// Behavioral capability contract for complex queries (nested fields, nested OrderBy and
/// collection membership). Every case here runs against both backends via the inheriting
/// Postgres/Sqlite test classes, and is checked against in-memory LINQ as the oracle.
/// </summary>
public abstract partial class IndexTests
{
    private static TestDocument[] NestedDocs() =>
    [
        new TestDocument
        {
            Id = "1", Title = "Doc 1", CreatedAt = DateTime.UtcNow,
            Author = new AuthorInfo { Name = "Alice", Age = 30, Address = new AddressInfo { City = "Oslo", Country = "NO" } },
            Labels = ["urgent", "red"], Scores = [1, 2]
        },
        new TestDocument
        {
            Id = "2", Title = "Doc 2", CreatedAt = DateTime.UtcNow,
            Author = new AuthorInfo { Name = "Bob", Age = 40, Address = new AddressInfo { City = "Bergen", Country = "NO" } },
            Labels = ["red"], Scores = [2, 3]
        },
        new TestDocument
        {
            Id = "3", Title = "Doc 3", CreatedAt = DateTime.UtcNow,
            Author = new AuthorInfo { Name = "Charlie", Age = 50, Address = null },
            Labels = ["green", "urgent"], Scores = [3]
        },
        new TestDocument
        {
            Id = "4", Title = "Doc 4", CreatedAt = DateTime.UtcNow,
            Author = new AuthorInfo { Name = "Alice", Age = 25, Address = new AddressInfo { City = "Oslo", Country = "NO" } },
            Labels = [], Scores = []
        },
        new TestDocument
        {
            Id = "5", Title = "Doc 5", CreatedAt = DateTime.UtcNow,
            Author = new AuthorInfo { Name = "Dave", Age = 40, Address = new AddressInfo { City = "Tromso", Country = "NO" } },
            Labels = ["blue"], Scores = [5, 2]
        }
    ];

    [Fact]
    public async Task NestedField_Equality_ShouldFilter()
    {
        var docs = NestedDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Author!.Name == "Alice", 2);
    }

    [Fact]
    public async Task NestedField_Comparison_ShouldFilter()
    {
        var docs = NestedDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Author!.Age >= 40, 3);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Author!.Age > 30 && d.Author.Age < 50, 2);
    }

    [Fact]
    public async Task NestedField_MultiLevel_Equality_ShouldFilter()
    {
        var docs = NestedDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs,
            d => d.Author!.Address != null && d.Author.Address.City == "Oslo", 2);
    }

    [Fact]
    public async Task NestedField_NullChecks_ShouldFilter()
    {
        var docs = NestedDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Author!.Address == null, 1);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Author!.Address != null, 4);
    }

    [Fact]
    public async Task NestedField_StringContains_ShouldFilter()
    {
        var docs = NestedDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Author!.Name.Contains("li"), 3);
    }

    [Fact]
    public async Task NestedField_CombinedWithTopLevelAndArray_ShouldFilter()
    {
        var docs = NestedDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs,
            d => d.Labels.Contains("urgent") || d.Author!.Name == "Bob", 3);
    }

    [Fact]
    public async Task CollectionContains_String_ShouldFilter()
    {
        var docs = NestedDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Labels.Contains("urgent"), 2);
    }

    [Fact]
    public async Task CollectionNotContains_String_ShouldFilter()
    {
        var docs = NestedDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs, d => !d.Labels.Contains("urgent"), 3);
    }

    [Fact]
    public async Task CollectionContains_EnumerableForm_ShouldFilter()
    {
        var docs = NestedDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs, d => Enumerable.Contains(d.Labels, "red"), 2);
    }

    [Fact]
    public async Task CollectionContains_IntElement_ShouldFilter()
    {
        var docs = NestedDocs();
        await Index.IndexManyAsync(docs);
        await ShouldReturnSameResultsAsLinq(docs, d => d.Scores.Contains(2), 3);
    }

    [Fact]
    public async Task CollectionContains_EmptyArray_IsExcluded()
    {
        var docs = NestedDocs();
        await Index.IndexManyAsync(docs);
        // doc 4 has empty Labels and must never match a Contains filter.
        await ShouldReturnSameResultsAsLinq(docs, d => d.Labels.Contains("red"), 2);
    }

    [Fact]
    public async Task NestedField_OrderBy_ShouldOrder()
    {
        var docs = NestedDocs();
        await Index.IndexManyAsync(docs);

        var request = new SearchRequest<TestDocument>()
            .OrderByAscending(d => d.Author!.Age)
            .OrderByAscending(d => d.Author!.Name);

        var expected = docs
            .OrderBy(d => d.Author!.Age)
            .ThenBy(d => d.Author!.Name)
            .Select(d => d.Id)
            .ToList();

        var result = await Index.SearchAsync(request);
        result.Results.Select(r => r.Document!.Id).Should().Equal(expected);
    }
}
