using FluentAssertions;
using SearchLite.Postgres;

namespace SearchLite.Tests.Postgres;

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

        result.Should().Be("searchlite_genericandlongtestdo_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
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