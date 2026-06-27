using System.Linq.Expressions;
using System.Text.Json.Serialization;
using FluentAssertions;
using SearchLite.Sqlite;

namespace SearchLite.Tests.Sqlite;

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

    public class TestAddress
    {
        public required string City { get; set; }
    }

    public class TestAuthor
    {
        public required string Name { get; set; }
        public int Age { get; set; }
        public required TestAddress Address { get; set; }
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
        public required List<string> Labels { get; set; }
        public required TestAuthor Author { get; set; }
    }

    [Fact]
    public void Should_Handle_Simple_Integer_Comparison()
    {
        var clause = BuildClause(x => x.Age > 18);

        clause.Sql.Should().Be("CAST(json_extract(document, '$.Age') AS INTEGER) > @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be(18);
    }

    [Fact]
    public void Should_Handle_Enum_Comparison()
    {
        var clause = BuildClause(x => x.EnumValue == TestEnum.Value2);

        clause.Sql.Should().Be("CAST(json_extract(document, '$.EnumValue') AS INTEGER) = @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be((int)TestEnum.Value2);
    }

    [Fact]
    public void Should_Handle_String_Enum_Comparison()
    {
        var clause = BuildClause(x => x.StringEnumValue == TestStringEnum.String2);

        clause.Sql.Should().Be("CAST(json_extract(document, '$.StringEnumValue') AS TEXT) = @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be(TestStringEnum.String2.ToString());
    }

    [Fact]
    public void Should_Handle_String_Equality()
    {
        var clause = BuildClause(x => x.Name == "John");

        clause.Sql.Should().Be("CAST(json_extract(document, '$.Name') AS TEXT) = @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be("John");
    }

    [Fact]
    public void Should_Handle_Boolean_Comparison()
    {
        var clause = BuildClause(x => x.IsActive == true);

        clause.Sql.Should().Be("CAST(json_extract(document, '$.IsActive') AS INTEGER) = @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be(true);
    }

    [Fact]
    public void Should_Handle_Double_Comparison()
    {
        var clause = BuildClause(x => x.Score >= 95.5);

        clause.Sql.Should().Be("CAST(json_extract(document, '$.Score') AS REAL) >= @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be(95.5);
    }

    [Fact]
    public void Should_Handle_Decimal_Comparison()
    {
        var clause = BuildClause(x => x.Price < 199.99m);

        clause.Sql.Should().Be("CAST(json_extract(document, '$.Price') AS REAL) < @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be(199.99m);
    }

    [Fact]
    public void Should_Handle_DateTime_Comparison()
    {
        var date = new DateTime(2024, 1, 1);
        var clause = BuildClause(x => x.CreatedAt > date);

        clause.Sql.Should().Be("CAST(json_extract(document, '$.CreatedAt') AS TEXT) > @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be(date);
    }

    [Fact]
    public void Should_Handle_Multiple_Conditions_With_And()
    {
        var clause = BuildClause(x => x.Age > 18 && x.IsActive == true);

        clause.Sql.Should()
            .Be(
                "(CAST(json_extract(document, '$.Age') AS INTEGER) > @p0 AND CAST(json_extract(document, '$.IsActive') AS INTEGER) = @p1)");
        clause.Parameters.Should().HaveCount(2);
        clause.Parameters[0].Value.Should().Be(18);
        clause.Parameters[1].Value.Should().Be(true);
    }

    [Fact]
    public void Should_Handle_Multiple_Conditions_With_Or()
    {
        var clause = BuildClause(x => x.Name == "John" || x.Name == "Jane");

        clause.Sql.Should()
            .Be(
                "(CAST(json_extract(document, '$.Name') AS TEXT) = @p0 OR CAST(json_extract(document, '$.Name') AS TEXT) = @p1)");
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
            "((CAST(json_extract(document, '$.Age') AS INTEGER) > @p0 AND CAST(json_extract(document, '$.IsActive') AS INTEGER) = @p1) OR " +
            "(CAST(json_extract(document, '$.Score') AS REAL) >= @p2 AND CAST(json_extract(document, '$.Name') AS TEXT) = @p3))");
        clause.Parameters.Should().HaveCount(4);
        clause.Parameters[0].Value.Should().Be(18);
        clause.Parameters[1].Value.Should().Be(true);
        clause.Parameters[2].Value.Should().Be(95.5);
        clause.Parameters[3].Value.Should().Be("John");
    }

    [Fact]
    public void Should_Handle_Multiple_Predicates()
    {
        var predicates = new List<Expression<Func<TestModel, bool>>>
        {
            x => x.Age > 18,
            x => x.IsActive == true
        };

        var clauses = BuildClauses(predicates);

        clauses.Should().HaveCount(2);
        clauses[0].Sql.Should().Be("CAST(json_extract(document, '$.Age') AS INTEGER) > @p0");
        clauses[1].Sql.Should().Be("CAST(json_extract(document, '$.IsActive') AS INTEGER) = @p0");
        clauses[0].Parameters.Should().HaveCount(1);
        clauses[1].Parameters.Should().HaveCount(1);
    }

    [Fact]
    public void Should_Handle_All_Comparison_Operators()
    {
        (Expression<Func<TestModel, bool>> expression, string expected)[] cases =
        [
            (x => x.Age == 18, "CAST(json_extract(document, '$.Age') AS INTEGER) = @p0"),
            (x => x.Age != 18, "CAST(json_extract(document, '$.Age') AS INTEGER) != @p0"),
            (x => x.Age > 18, "CAST(json_extract(document, '$.Age') AS INTEGER) > @p0"),
            (x => x.Age >= 18, "CAST(json_extract(document, '$.Age') AS INTEGER) >= @p0"),
            (x => x.Age < 18, "CAST(json_extract(document, '$.Age') AS INTEGER) < @p0"),
            (x => x.Age <= 18, "CAST(json_extract(document, '$.Age') AS INTEGER) <= @p0")
        ];

        foreach (var testCase in cases)
        {
            var clause = BuildClause(testCase.expression);
            clause.Sql.Should().Be(testCase.expected);
            clause.Parameters.Should().HaveCount(1);
            clause.Parameters[0].Value.Should().Be(18);
        }
    }

    [Fact]
    public void Should_Handle_Nested_String_Equality()
    {
        var clause = BuildClause(x => x.Author.Name == "John");

        clause.Sql.Should().Be("CAST(json_extract(document, '$.Author.Name') AS TEXT) = @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be("John");
    }

    [Fact]
    public void Should_Handle_Nested_Integer_Comparison()
    {
        var clause = BuildClause(x => x.Author.Age > 18);

        clause.Sql.Should().Be("CAST(json_extract(document, '$.Author.Age') AS INTEGER) > @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be(18);
    }

    [Fact]
    public void Should_Handle_Deeply_Nested_String_Equality()
    {
        var clause = BuildClause(x => x.Author.Address.City == "Oslo");

        clause.Sql.Should().Be("CAST(json_extract(document, '$.Author.Address.City') AS TEXT) = @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be("Oslo");
    }

    [Fact]
    public void Should_Handle_Nested_Null_Check()
    {
        var clause = BuildClause(x => x.Author.Name == null);

        clause.Sql.Should().Be("json_extract(document, '$.Author.Name') IS NULL");
        clause.Parameters.Should().BeEmpty();
    }

    [Fact]
    public void Should_Handle_Nested_String_Operator()
    {
        var clause = BuildClause(x => x.Author.Name.Contains("oh"));

        clause.Sql.Should().Be("CAST(json_extract(document, '$.Author.Name') AS TEXT) GLOB @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be("*oh*");
    }

    [Fact]
    public void Should_Handle_CollectionContains()
    {
        var clause = BuildClause(x => x.Labels.Contains("urgent"));

        clause.Sql.Should().Be("EXISTS (SELECT 1 FROM json_each(document, '$.Labels') WHERE value = @p0)");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be("urgent");
    }

    [Fact]
    public void Should_Handle_CollectionNotContains()
    {
        var clause = BuildClause(x => !x.Labels.Contains("urgent"));

        clause.Sql.Should().Be("NOT EXISTS (SELECT 1 FROM json_each(document, '$.Labels') WHERE value = @p0)");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be("urgent");
    }

    [Fact]
    public void Should_Handle_Nested_CollectionContains()
    {
        var clause = BuildClause(x => x.Author.Roles.Contains("admin"));

        clause.Sql.Should().Be("EXISTS (SELECT 1 FROM json_each(document, '$.Author.Roles') WHERE value = @p0)");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be("admin");
    }

    private static Clause BuildClause(Expression<Func<TestModel, bool>> predicate) => BuildClauses(predicate).Single();

    [Fact]
    public void Should_Handle_Multiple_Where_Calls()
    {
        // This simulates the scenario: request.Where(d => d.Title.Contains("Important")).Where(d => d.Views >= 100)
        var request = new SearchRequest<TestModel>()
            .Where(x => x.Name.Contains("Test"))
            .Where(x => x.Age >= 18);

        var clauses = WhereClauseBuilder<TestModel>.BuildClauses(request.Filters);
        
        clauses.Should().HaveCount(2);
        
        // Check that parameter names don't conflict
        var allParams = clauses.SelectMany(c => c.Parameters).ToList();
        var paramNames = allParams.Select(p => p.ParameterName).ToList();
        
        paramNames.Should().HaveCount(2);
        paramNames.Should().OnlyHaveUniqueItems("Parameter names should be unique to avoid conflicts");
        
        // The parameters should be @p0 and @p1, not both @p0
        paramNames.Should().Contain("@p0");
        paramNames.Should().Contain("@p1");
    }

    private static IReadOnlyList<Clause> BuildClauses(params IEnumerable<Expression<Func<TestModel, bool>>> predicates)
    {
        return predicates.SelectMany(predicate =>
                WhereClauseBuilder<TestModel>.BuildClauses(
                    [FilterMapper.Map(predicate)]))
            .ToList();
    }
}