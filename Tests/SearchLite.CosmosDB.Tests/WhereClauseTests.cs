using System.Linq.Expressions;
using System.Text.Json.Serialization;
using FluentAssertions;
using SearchLite.CosmosDB;

namespace SearchLite.CosmosDB.Tests;

/// <summary>
/// Unit tests for the Cosmos NoSQL <c>WHERE</c> translation, mirroring the Postgres WhereClauseTests
/// but asserting the Cosmos accessor form (<c>c.doc["Field"]</c>) and functions
/// (<c>ARRAY_CONTAINS</c>, <c>CONTAINS</c>, <c>IS_DEFINED</c>, ...).
/// </summary>
public class WhereClauseTests
{
    public enum TestEnum
    {
        Value1,
        Value2
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TestStringEnum
    {
        String1,
        String2
    }

    public class Address
    {
        public required string City { get; set; }
    }

    public class Author
    {
        public required string Name { get; set; }
        public int Rank { get; set; }
        public required Address Address { get; set; }
        public required List<string> Roles { get; set; }
    }

    public class TestModel
    {
        public int Age { get; set; }
        public required string Name { get; set; }
        public bool IsActive { get; set; }
        public double Score { get; set; }
        public decimal Price { get; set; }
        public DateTime CreatedAt { get; set; }
        public TestEnum EnumValue { get; set; }
        public TestStringEnum StringEnumValue { get; set; }
        public required Author Author { get; set; }
        public required List<string> Labels { get; set; }
    }

    [Fact]
    public void Should_Handle_Simple_Integer_Comparison()
    {
        var clause = BuildClause(x => x.Age > 18);

        clause.Sql.Should().Be("c.doc[\"Age\"] > @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be(18);
    }

    [Fact]
    public void Should_Handle_Enum_Comparison_AsInteger()
    {
        var clause = BuildClause(x => x.EnumValue == TestEnum.Value2);

        clause.Sql.Should().Be("c.doc[\"EnumValue\"] = @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be((int)TestEnum.Value2);
    }

    [Fact]
    public void Should_Handle_String_Enum_Comparison_AsString()
    {
        var clause = BuildClause(x => x.StringEnumValue == TestStringEnum.String2);

        clause.Sql.Should().Be("c.doc[\"StringEnumValue\"] = @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be(nameof(TestStringEnum.String2));
    }

    [Fact]
    public void Should_Handle_String_Equality()
    {
        var clause = BuildClause(x => x.Name == "John");

        clause.Sql.Should().Be("c.doc[\"Name\"] = @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be("John");
    }

    [Fact]
    public void Should_Handle_Boolean_Comparison()
    {
        var clause = BuildClause(x => x.IsActive == true);

        clause.Sql.Should().Be("c.doc[\"IsActive\"] = @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be(true);
    }

    [Fact]
    public void Should_Handle_Double_Comparison()
    {
        var clause = BuildClause(x => x.Score >= 95.5);

        clause.Sql.Should().Be("c.doc[\"Score\"] >= @p0");
        clause.Parameters[0].Value.Should().Be(95.5);
    }

    [Fact]
    public void Should_Handle_DateTime_Comparison_AsRoundTripString()
    {
        var date = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var clause = BuildClause(x => x.CreatedAt > date);

        clause.Sql.Should().Be("c.doc[\"CreatedAt\"] > @p0");
        clause.Parameters[0].Value.Should().Be(date.ToString("O"));
    }

    [Fact]
    public void Should_Handle_Multiple_Conditions_With_And()
    {
        var clause = BuildClause(x => x.Age > 18 && x.IsActive == true);

        clause.Sql.Should().Be("(c.doc[\"Age\"] > @p0 AND c.doc[\"IsActive\"] = @p1)");
        clause.Parameters.Should().HaveCount(2);
        clause.Parameters[0].Value.Should().Be(18);
        clause.Parameters[1].Value.Should().Be(true);
    }

    [Fact]
    public void Should_Handle_Multiple_Conditions_With_Or()
    {
        var clause = BuildClause(x => x.Name == "John" || x.Name == "Jane");

        clause.Sql.Should().Be("(c.doc[\"Name\"] = @p0 OR c.doc[\"Name\"] = @p1)");
        clause.Parameters.Should().HaveCount(2);
        clause.Parameters[0].Value.Should().Be("John");
        clause.Parameters[1].Value.Should().Be("Jane");
    }

    [Fact]
    public void Should_Handle_Complex_Nested_Conditions()
    {
        var clause = BuildClause(x =>
            (x.Age > 18 && x.IsActive == true) || (x.Score >= 95.5 && x.Name == "John"));

        clause.Sql.Should().Be(
            "((c.doc[\"Age\"] > @p0 AND c.doc[\"IsActive\"] = @p1) OR " +
            "(c.doc[\"Score\"] >= @p2 AND c.doc[\"Name\"] = @p3))");
        clause.Parameters.Should().HaveCount(4);
    }

    [Fact]
    public void Should_Handle_All_Comparison_Operators()
    {
        (Expression<Func<TestModel, bool>> expression, string expected)[] cases =
        [
            (x => x.Age == 18, "c.doc[\"Age\"] = @p0"),
            (x => x.Age != 18, "c.doc[\"Age\"] != @p0"),
            (x => x.Age > 18, "c.doc[\"Age\"] > @p0"),
            (x => x.Age >= 18, "c.doc[\"Age\"] >= @p0"),
            (x => x.Age < 18, "c.doc[\"Age\"] < @p0"),
            (x => x.Age <= 18, "c.doc[\"Age\"] <= @p0")
        ];

        foreach (var testCase in cases)
        {
            var clause = BuildClause(testCase.expression);
            clause.Sql.Should().Be(testCase.expected);
            clause.Parameters[0].Value.Should().Be(18);
        }
    }

    [Fact]
    public void Should_Handle_Unique_Parameter_Names_Across_Where_Calls()
    {
        var request = new SearchRequest<TestModel>()
            .Where(x => x.Name.Contains("Test"))
            .Where(x => x.Age >= 18);

        var clauses = WhereClauseBuilder<TestModel>.BuildClauses(request.Filters).ToList();

        clauses.Should().HaveCount(2);
        var paramNames = clauses.SelectMany(c => c.Parameters).Select(p => p.Name).ToList();
        paramNames.Should().OnlyHaveUniqueItems();
        paramNames.Should().Contain("@p0");
        paramNames.Should().Contain("@p1");
    }

    [Fact]
    public void Should_Handle_Nested_String_Equality()
    {
        var clause = BuildClause(x => x.Author.Name == "Alice");

        clause.Sql.Should().Be("c.doc[\"Author\"][\"Name\"] = @p0");
        clause.Parameters[0].Value.Should().Be("Alice");
    }

    [Fact]
    public void Should_Handle_Deeply_Nested_String_Equality()
    {
        var clause = BuildClause(x => x.Author.Address.City == "Oslo");

        clause.Sql.Should().Be("c.doc[\"Author\"][\"Address\"][\"City\"] = @p0");
        clause.Parameters[0].Value.Should().Be("Oslo");
    }

    [Fact]
    public void Should_Handle_Nested_Comparison()
    {
        var clause = BuildClause(x => x.Author.Rank > 3);

        clause.Sql.Should().Be("c.doc[\"Author\"][\"Rank\"] > @p0");
        clause.Parameters[0].Value.Should().Be(3);
    }

    [Fact]
    public void Should_Handle_Nested_Null_Check()
    {
        var clause = BuildClause(x => x.Author.Name == null);

        clause.Sql.Should().Be(
            "(NOT IS_DEFINED(c.doc[\"Author\"][\"Name\"]) OR IS_NULL(c.doc[\"Author\"][\"Name\"]))");
        clause.Parameters.Should().HaveCount(0);
    }

    [Fact]
    public void Should_Handle_Nested_String_Contains()
    {
        var clause = BuildClause(x => x.Author.Name.Contains("li"));

        clause.Sql.Should().Be("CONTAINS(c.doc[\"Author\"][\"Name\"], @p0)");
        clause.Parameters[0].Value.Should().Be("li");
    }

    [Fact]
    public void Should_Handle_StartsWith_IgnoreCase()
    {
        var clause = BuildClause(x => x.Name.StartsWith("jo", StringComparison.OrdinalIgnoreCase));

        clause.Sql.Should().Be("STARTSWITH(c.doc[\"Name\"], @p0, true)");
        clause.Parameters[0].Value.Should().Be("jo");
    }

    [Fact]
    public void Should_Handle_CollectionContains_TopLevel()
    {
        var clause = BuildClause(x => x.Labels.Contains("urgent"));

        clause.Sql.Should().Be("ARRAY_CONTAINS(c.doc[\"Labels\"], @p0)");
        clause.Parameters[0].Value.Should().Be("urgent");
    }

    [Fact]
    public void Should_Handle_CollectionNotContains_TopLevel()
    {
        var clause = BuildClause(x => !x.Labels.Contains("urgent"));

        clause.Sql.Should().Be("NOT (ARRAY_CONTAINS(c.doc[\"Labels\"], @p0))");
        clause.Parameters[0].Value.Should().Be("urgent");
    }

    [Fact]
    public void Should_Handle_CollectionContains_Nested()
    {
        var clause = BuildClause(x => x.Author.Roles.Contains("admin"));

        clause.Sql.Should().Be("ARRAY_CONTAINS(c.doc[\"Author\"][\"Roles\"], @p0)");
        clause.Parameters[0].Value.Should().Be("admin");
    }

    [Fact]
    public void Should_Handle_In_Operator()
    {
        var names = new[] { "John", "Jane" };
        var clause = BuildClause(x => names.Contains(x.Name));

        clause.Sql.Should().Be("c.doc[\"Name\"] IN (@p0, @p1)");
        clause.Parameters.Should().HaveCount(2);
        clause.Parameters[0].Value.Should().Be("John");
        clause.Parameters[1].Value.Should().Be("Jane");
    }

    private static Clause BuildClause(Expression<Func<TestModel, bool>> predicate) =>
        WhereClauseBuilder<TestModel>.BuildClauses([FilterMapper.Map(predicate)]).Single();
}
