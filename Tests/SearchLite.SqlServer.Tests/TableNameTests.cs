using FluentAssertions;

namespace SearchLite.SqlServer.Tests;

public class TableNameTests
{
    [Fact]
    public void GetTableName_ShouldReturnCorrectTableName()
    {
        var result = SearchIndex<SearchLite.Tests.IndexTests.TestDocument>.GetTableName("test");

        result.Should().Be("searchlite_testdocument_test");
    }

    [Fact]
    public void GetTableName_ShouldReturnCorrectTableNameGeneric()
    {
        var result = SearchIndex<GenericAndLongTestDocument<Foo>>.GetTableName("test");

        result.Should().Be("searchlite_genericandlongtestdocument1_test");
    }

    [Fact]
    public void GetTableName_ShouldReturnCorrectTableNameWhenTooLong()
    {
        // SQL Server identifiers are capped at 128 characters. Pad the collection name out so the
        // composed name exceeds that and must be truncated.
        var collection = "a".PadRight(140, 'a');
        var result = SearchIndex<GenericAndLongTestDocument<Foo>>.GetTableName(collection);

        result.Length.Should().Be(128);
        result.Should().StartWith("searchlite_");
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
