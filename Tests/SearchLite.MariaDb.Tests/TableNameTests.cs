using FluentAssertions;
using SearchLite.MariaDb;

namespace SearchLite.Tests.MariaDb;

public class TableNameTests
{
    [Fact]
    public void GetTableName_ShouldReturnCorrectTableName()
    {
        var result = SearchIndex<Tests.IndexTests.TestDocument>.GetTableName("test");

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
        var result = SearchIndex<GenericAndLongTestDocument<Foo>>.GetTableName("a".PadRight(32, 'a'));

        result.Should().Be("searchlite_genericandlongtestdoc_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        result.Length.Should().Be(64);
    }

    public class Foo{}

    public class GenericAndLongTestDocument<T>: ISearchableDocument
    {
        public required string Id { get; init; }
        public required string Title { get; init; }
        public string Content { get; init; } = "";
        public int Views { get; init; }
        public DateTime CreatedAt { get; set; }

        public string GetSearchText() => $"{Title} {Content}";
    }
}
