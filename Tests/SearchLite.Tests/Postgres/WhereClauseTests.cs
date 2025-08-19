using System.Linq.Expressions;
using System.Text.Json.Serialization;
using FluentAssertions;
using SearchLite.Postgres;

namespace SearchLite.Tests.Postgres;

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
    }

    [Fact]
    public void Should_Handle_Simple_Integer_Comparison()
    {
        var clause = BuildClause(x => x.Age > 18);

        clause.Sql.Should().Be("(document->>'Age')::integer > @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be(18);
    }

    [Fact]
    public void Should_Handle_Enum_Comparison()
    {
        var clause = BuildClause(x => x.EnumValue == TestEnum.Value2);

        clause.Sql.Should().Be("(document->>'EnumValue')::integer = @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be((int)TestEnum.Value2);
    }

    [Fact]
    public void Should_Handle_String_Enum_Comparison()
    {
        var clause = BuildClause(x => x.StringEnumValue == TestStringEnum.String2);

        clause.Sql.Should().Be("(document->>'StringEnumValue')::text = @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be(nameof(TestStringEnum.String2));
    }

    [Fact]
    public void Should_Handle_String_Equality()
    {
        var clause = BuildClause(x => x.Name == "John");

        clause.Sql.Should().Be("(document->>'Name')::text = @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be("John");
    }

    [Fact]
    public void Should_Handle_Boolean_Comparison()
    {
        var clause = BuildClause(x => x.IsActive == true);
        clause.Sql.Should().Be("(document->>'IsActive')::boolean = @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be(true);
    }

    [Fact]
    public void Should_Handle_Double_Comparison()
    {
        var clause = BuildClause(x => x.Score >= 95.5);
        clause.Sql.Should().Be("(document->>'Score')::numeric >= @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be(95.5);
    }

    [Fact]
    public void Should_Handle_Decimal_Comparison()
    {
        var clause = BuildClause(x => x.Price < 199.99m);

        clause.Sql.Should().Be("(document->>'Price')::numeric < @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be(199.99m);
    }

    [Fact]
    public void Should_Handle_DateTime_Comparison()
    {
        var date = new DateTime(2024, 1, 1);
        var clause = BuildClause(x => x.CreatedAt > date);

        clause.Sql.Should().Be("(document->>'CreatedAt')::timestamp > @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be(date);
    }

    [Fact]
    public void Should_Handle_Multiple_Conditions_With_And()
    {
        var clause = BuildClause(x => x.Age > 18 && x.IsActive == true);

        clause.Sql.Should().Be("((document->>'Age')::integer > @p0 AND (document->>'IsActive')::boolean = @p1)");
        clause.Parameters.Should().HaveCount(2);
        clause.Parameters[0].Value.Should().Be(18);
        clause.Parameters[1].Value.Should().Be(true);
    }

    [Fact]
    public void Should_Handle_Multiple_Conditions_With_Or()
    {
        var clause = BuildClause(x => x.Name == "John" || x.Name == "Jane");

        clause.Sql.Should().Be("((document->>'Name')::text = @p0 OR (document->>'Name')::text = @p1)");
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
            "(((document->>'Age')::integer > @p0 AND (document->>'IsActive')::boolean = @p1) OR " +
            "((document->>'Score')::numeric >= @p2 AND (document->>'Name')::text = @p3))");
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

        var clauses = BuildClauses(predicates).ToList();

        clauses.Should().HaveCount(2);
        clauses[0].Sql.Should().Be("(document->>'Age')::integer > @p0");
        clauses[1].Sql.Should().Be("(document->>'IsActive')::boolean = @p0");
        clauses[0].Parameters.Should().HaveCount(1);
        clauses[1].Parameters.Should().HaveCount(1);
    }

    [Fact]
    public void Should_Handle_All_Comparison_Operators()
    {
        (Expression<Func<TestModel, bool>> expression, string expected)[] cases =
        [
            (x => x.Age == 18, "(document->>'Age')::integer = @p0"),
            (x => x.Age != 18, "(document->>'Age')::integer != @p0"),
            (x => x.Age > 18, "(document->>'Age')::integer > @p0"),
            (x => x.Age >= 18, "(document->>'Age')::integer >= @p0"),
            (x => x.Age < 18, "(document->>'Age')::integer < @p0"),
            (x => x.Age <= 18, "(document->>'Age')::integer <= @p0")
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
    public void Should_Handle_Multiple_Where_Calls()
    {
        // This simulates the scenario: request.Where(d => d.Title.Contains("Important")).Where(d => d.Views >= 100)
        var request = new SearchRequest<TestModel>()
            .Where(x => x.Name.Contains("Test"))
            .Where(x => x.Age >= 18);

        var clauses = WhereClauseBuilder<TestModel>.BuildClauses(request.Filters).ToList();
        
        clauses.Should().HaveCount(2);
        
        // Check that parameter names don't conflict
        var allParams = clauses.SelectMany(c => c.Parameters).ToList();
        var paramNames = allParams.Select(p => p.ParameterName).ToList();
        
        paramNames.Should().HaveCount(2);
        paramNames.Should().OnlyHaveUniqueItems("Parameter names should be unique to avoid conflicts");
        
        paramNames.Should().Contain("@p0");
        paramNames.Should().Contain("@p1");
    }

    private static Clause BuildClause(Expression<Func<TestModel, bool>> predicate) => BuildClauses(predicate).Single();

    private static IReadOnlyList<Clause> BuildClauses(params IEnumerable<Expression<Func<TestModel, bool>>> predicates)
    {
        return predicates.SelectMany(predicate =>
                WhereClauseBuilder<TestModel>.BuildClauses(
                    [FilterMapper.Map(predicate)]))
            .ToList();
    }
}