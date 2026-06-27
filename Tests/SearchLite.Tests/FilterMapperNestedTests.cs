using System.Linq.Expressions;
using FluentAssertions;

namespace SearchLite.Tests;

public class FilterMapperNestedTests
{
    private class Doc
    {
        public string Name { get; set; } = "";
        public Inner? Author { get; set; }
        public List<string> Labels { get; set; } = [];
        public int[] Scores { get; set; } = [];
    }

    private class Inner
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public Deeper? Address { get; set; }
    }

    private class Deeper
    {
        public string City { get; set; } = "";
    }

    [Fact]
    public void Map_NestedEquality_ProducesDottedPath()
    {
        Expression<Func<Doc, bool>> predicate = d => d.Author!.Name == "Alice";
        var result = FilterMapper.Map(predicate);

        result.Should().BeEquivalentTo(new FilterNode<Doc>.Condition
        {
            PropertyName = "Author.Name",
            PropertyType = typeof(string),
            Operator = Operator.Equal,
            Value = "Alice"
        });
    }

    [Fact]
    public void Map_MultiLevelNesting_ProducesDottedPath()
    {
        Expression<Func<Doc, bool>> predicate = d => d.Author!.Address!.City == "Oslo";
        var result = FilterMapper.Map(predicate);

        result.Should().BeEquivalentTo(new FilterNode<Doc>.Condition
        {
            PropertyName = "Author.Address.City",
            PropertyType = typeof(string),
            Operator = Operator.Equal,
            Value = "Oslo"
        });
    }

    [Fact]
    public void Map_NestedComparison_ProducesDottedPath()
    {
        Expression<Func<Doc, bool>> predicate = d => d.Author!.Age > 18;
        var result = FilterMapper.Map(predicate);

        result.Should().BeEquivalentTo(new FilterNode<Doc>.Condition
        {
            PropertyName = "Author.Age",
            PropertyType = typeof(int),
            Operator = Operator.GreaterThan,
            Value = 18
        });
    }

    [Fact]
    public void Map_CollectionContains_ProducesCollectionContainsOperator()
    {
        Expression<Func<Doc, bool>> predicate = d => d.Labels.Contains("urgent");
        var result = FilterMapper.Map(predicate);

        result.Should().BeEquivalentTo(new FilterNode<Doc>.Condition
        {
            PropertyName = "Labels",
            PropertyType = typeof(List<string>),
            Operator = Operator.CollectionContains,
            Value = "urgent"
        });
    }

    [Fact]
    public void Map_NegatedCollectionContains_ProducesCollectionNotContains()
    {
        Expression<Func<Doc, bool>> predicate = d => !d.Labels.Contains("urgent");
        var result = FilterMapper.Map(predicate);

        ((FilterNode<Doc>.Condition)result).Operator.Should().Be(Operator.CollectionNotContains);
    }

    [Fact]
    public void Map_EnumerableContainsOnDocumentArray_ProducesCollectionContains()
    {
        Expression<Func<Doc, bool>> predicate = d => Enumerable.Contains(d.Scores, 5);
        var result = FilterMapper.Map(predicate);

        result.Should().BeEquivalentTo(new FilterNode<Doc>.Condition
        {
            PropertyName = "Scores",
            PropertyType = typeof(int[]),
            Operator = Operator.CollectionContains,
            Value = 5
        });
    }

    [Fact]
    public void Map_ConstantCollectionContainsDocumentField_StillProducesIn()
    {
        var allowed = new[] { "a", "b" };
        Expression<Func<Doc, bool>> predicate = d => allowed.Contains(d.Name);
        var result = FilterMapper.Map(predicate);

        var condition = (FilterNode<Doc>.Condition)result;
        condition.PropertyName.Should().Be("Name");
        condition.Operator.Should().Be(Operator.In);
    }

    [Fact]
    public void OrderByMapper_NestedField_ProducesDottedPath()
    {
        Expression<Func<Doc, string>> selector = d => d.Author!.Name;
        var node = OrderByMapper.Map(selector, SortDirection.Ascending);

        node.PropertyName.Should().Be("Author.Name");
        node.Direction.Should().Be(SortDirection.Ascending);
    }
}
