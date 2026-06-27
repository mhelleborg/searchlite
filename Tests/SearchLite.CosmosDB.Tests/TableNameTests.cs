using FluentAssertions;
using SearchLite.CosmosDB;

namespace SearchLite.CosmosDB.Tests;

/// <summary>
/// Cosmos container-naming (collection-naming) tests, mirroring the Postgres table-name tests.
/// </summary>
public class TableNameTests
{
    [Fact]
    public void GetContainerName_ShouldReturnCorrectName()
    {
        var result = SearchIndex<SearchLite.Tests.IndexTests.TestDocument>.GetContainerName("test");

        result.Should().Be("searchlite_testdocument_test");
    }

    [Fact]
    public void GetContainerName_ShouldSanitizeIllegalCharacters()
    {
        var result = SearchIndex<SearchLite.Tests.IndexTests.TestDocument>.GetContainerName("my/collection#name");

        result.Should().Be("searchlite_testdocument_mycollectionname");
    }

    [Fact]
    public void GetContainerName_ShouldReturnCorrectNameGeneric()
    {
        var result = SearchIndex<GenericAndLongTestDocument<Foo>>.GetContainerName("test");

        result.Should().Be("searchlite_genericandlongtestdocument1_test");
    }

    [Fact]
    public void GetContainerName_ShouldTruncateWhenTooLong()
    {
        var result = SearchIndex<GenericAndLongTestDocument<Foo>>.GetContainerName("a".PadRight(300, 'a'));

        result.Length.Should().BeLessThanOrEqualTo(255);
        result.Should().StartWith("searchlite_genericandlongtestdocument1_");
    }

    public class Foo;

    public class GenericAndLongTestDocument<T> : ISearchableDocument
    {
        public required string Id { get; init; }
        public required string Title { get; init; }
        public string Content { get; init; } = "";
        public int Views { get; init; }
        public DateTime CreatedAt { get; set; }

        public string GetSearchText() => $"{Title} {Content}";
    }
}
