using System.Linq.Expressions;
using System.Text.Json.Serialization;
using FluentAssertions;

namespace SearchLite.SqlServer.Tests;

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

        clause.Sql.Should().Be("CAST(JSON_VALUE(document, '$.Age') AS INT) > @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be(18);
    }

    [Fact]
    public void Should_Handle_Enum_Comparison()
    {
        var clause = BuildClause(x => x.EnumValue == TestEnum.Value2);

        clause.Sql.Should().Be("CAST(JSON_VALUE(document, '$.EnumValue') AS BIGINT) = @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be((long)(int)TestEnum.Value2);
    }

    [Fact]
    public void Should_Handle_String_Enum_Comparison()
    {
        var clause = BuildClause(x => x.StringEnumValue == TestStringEnum.String2);

        clause.Sql.Should().Be("CAST(JSON_VALUE(document, '$.StringEnumValue') AS NVARCHAR(MAX)) = @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be(nameof(TestStringEnum.String2));
    }

    [Fact]
    public void Should_Handle_String_Equality()
    {
        var clause = BuildClause(x => x.Name == "John");

        clause.Sql.Should().Be("CAST(JSON_VALUE(document, '$.Name') AS NVARCHAR(MAX)) = @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be("John");
    }

    [Fact]
    public void Should_Handle_Boolean_Comparison()
    {
        var clause = BuildClause(x => x.IsActive == true);
        clause.Sql.Should().Be("CAST(JSON_VALUE(document, '$.IsActive') AS BIT) = @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be(true);
    }

    [Fact]
    public void Should_Handle_Double_Comparison()
    {
        var clause = BuildClause(x => x.Score >= 95.5);
        clause.Sql.Should().Be("CAST(JSON_VALUE(document, '$.Score') AS FLOAT) >= @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be(95.5);
    }

    [Fact]
    public void Should_Handle_Decimal_Comparison()
    {
        var clause = BuildClause(x => x.Price < 199.99m);

        clause.Sql.Should().Be("CAST(JSON_VALUE(document, '$.Price') AS DECIMAL(38,18)) < @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be(199.99m);
    }

    [Fact]
    public void Should_Handle_DateTime_Comparison()
    {
        var date = new DateTime(2024, 1, 1);
        var clause = BuildClause(x => x.CreatedAt > date);

        clause.Sql.Should().Be("CAST(JSON_VALUE(document, '$.CreatedAt') AS DATETIME2) > @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be(date);
    }

    [Fact]
    public void Should_Handle_Multiple_Conditions_With_And()
    {
        var clause = BuildClause(x => x.Age > 18 && x.IsActive == true);

        clause.Sql.Should().Be(
            "(CAST(JSON_VALUE(document, '$.Age') AS INT) > @p0 AND CAST(JSON_VALUE(document, '$.IsActive') AS BIT) = @p1)");
        clause.Parameters.Should().HaveCount(2);
        clause.Parameters[0].Value.Should().Be(18);
        clause.Parameters[1].Value.Should().Be(true);
    }

    [Fact]
    public void Should_Handle_Multiple_Conditions_With_Or()
    {
        var clause = BuildClause(x => x.Name == "John" || x.Name == "Jane");

        clause.Sql.Should().Be(
            "(CAST(JSON_VALUE(document, '$.Name') AS NVARCHAR(MAX)) = @p0 OR CAST(JSON_VALUE(document, '$.Name') AS NVARCHAR(MAX)) = @p1)");
        clause.Parameters.Should().HaveCount(2);
        clause.Parameters[0].Value.Should().Be("John");
        clause.Parameters[1].Value.Should().Be("Jane");
    }

    [Fact]
    public void Should_Handle_All_Comparison_Operators()
    {
        (Expression<Func<TestModel, bool>> expression, string expected)[] comparisonCases =
        [
            (x => x.Age == 18, "CAST(JSON_VALUE(document, '$.Age') AS INT) = @p0"),
            (x => x.Age != 18, "CAST(JSON_VALUE(document, '$.Age') AS INT) != @p0"),
            (x => x.Age > 18, "CAST(JSON_VALUE(document, '$.Age') AS INT) > @p0"),
            (x => x.Age >= 18, "CAST(JSON_VALUE(document, '$.Age') AS INT) >= @p0"),
            (x => x.Age < 18, "CAST(JSON_VALUE(document, '$.Age') AS INT) < @p0"),
            (x => x.Age <= 18, "CAST(JSON_VALUE(document, '$.Age') AS INT) <= @p0")
        ];

        foreach (var testCase in comparisonCases)
        {
            var clause = BuildClause(testCase.expression);

            clause.Sql.Should().Be(testCase.expected);
            clause.Parameters.Should().HaveCount(1);
            clause.Parameters[0].Value.Should().Be(18);
        }
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
        clauses[0].Sql.Should().Be("CAST(JSON_VALUE(document, '$.Age') AS INT) > @p0");
        clauses[1].Sql.Should().Be("CAST(JSON_VALUE(document, '$.IsActive') AS BIT) = @p0");
        clauses[0].Parameters.Should().HaveCount(1);
        clauses[1].Parameters.Should().HaveCount(1);
        clauses[1].Parameters[0].Value.Should().Be(true);
    }

    [Fact]
    public void Should_Handle_Multiple_Where_Calls()
    {
        var request = new SearchRequest<TestModel>()
            .Where(x => x.Name.Contains("Test"))
            .Where(x => x.Age >= 18);

        var clauses = WhereClauseBuilder<TestModel>.BuildClauses(request.Filters).ToList();

        clauses.Should().HaveCount(2);

        var allParams = clauses.SelectMany(c => c.Parameters).ToList();
        var paramNames = allParams.Select(p => p.ParameterName).ToList();

        paramNames.Should().HaveCount(2);
        paramNames.Should().OnlyHaveUniqueItems("Parameter names should be unique to avoid conflicts");

        paramNames.Should().Contain("@p0");
        paramNames.Should().Contain("@p1");
    }

    [Fact]
    public void Should_Handle_Nested_Comparison_With_Path_Accessor()
    {
        var clause = BuildClause(x => x.Author.Rank > 3);

        clause.Sql.Should().Be("CAST(JSON_VALUE(document, '$.Author.Rank') AS INT) > @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be(3);
    }

    [Fact]
    public void Should_Handle_Nested_String_Equality_With_Path_Accessor()
    {
        var clause = BuildClause(x => x.Author.Name == "Alice");

        clause.Sql.Should().Be("CAST(JSON_VALUE(document, '$.Author.Name') AS NVARCHAR(MAX)) = @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be("Alice");
    }

    [Fact]
    public void Should_Handle_Deeply_Nested_String_Equality_With_Path_Accessor()
    {
        var clause = BuildClause(x => x.Author.Address.City == "Oslo");

        clause.Sql.Should().Be("CAST(JSON_VALUE(document, '$.Author.Address.City') AS NVARCHAR(MAX)) = @p0");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be("Oslo");
    }

    [Fact]
    public void Should_Handle_Nested_Null_Check_With_Path_Accessor()
    {
        var clause = BuildClause(x => x.Author.Name == null);

        clause.Sql.Should().Be("JSON_VALUE(document, '$.Author.Name') IS NULL");
        clause.Parameters.Should().HaveCount(0);
    }

    [Fact]
    public void Should_Handle_Nested_String_Operation_With_Path_Accessor()
    {
        var clause = BuildClause(x => x.Author.Name.Contains("li"));

        clause.Sql.Should().Be(
            "CAST(JSON_VALUE(document, '$.Author.Name') AS NVARCHAR(MAX)) COLLATE Latin1_General_CS_AS LIKE @p0 ESCAPE '\\'");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be("%li%");
    }

    [Fact]
    public void Should_Handle_CollectionContains_TopLevel()
    {
        var clause = BuildClause(x => x.Labels.Contains("urgent"));

        clause.Sql.Should().Be(
            "EXISTS (SELECT 1 FROM OPENJSON(document, '$.Labels') WHERE value = @p0)");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be("urgent");
    }

    [Fact]
    public void Should_Handle_CollectionNotContains_TopLevel()
    {
        var clause = BuildClause(x => !x.Labels.Contains("urgent"));

        clause.Sql.Should().Be(
            "NOT EXISTS (SELECT 1 FROM OPENJSON(document, '$.Labels') WHERE value = @p0)");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be("urgent");
    }

    [Fact]
    public void Should_Handle_CollectionContains_Nested()
    {
        var clause = BuildClause(x => x.Author.Roles.Contains("admin"));

        clause.Sql.Should().Be(
            "EXISTS (SELECT 1 FROM OPENJSON(document, '$.Author.Roles') WHERE value = @p0)");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be("admin");
    }

    [Fact]
    public void Should_Handle_StartsWith()
    {
        var clause = BuildClause(x => x.Name.StartsWith("Jo"));

        clause.Sql.Should().Be(
            "CAST(JSON_VALUE(document, '$.Name') AS NVARCHAR(MAX)) COLLATE Latin1_General_CS_AS LIKE @p0 ESCAPE '\\'");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be("Jo%");
    }

    [Fact]
    public void Should_Handle_EndsWith()
    {
        var clause = BuildClause(x => x.Name.EndsWith("hn"));

        clause.Sql.Should().Be(
            "CAST(JSON_VALUE(document, '$.Name') AS NVARCHAR(MAX)) COLLATE Latin1_General_CS_AS LIKE @p0 ESCAPE '\\'");
        clause.Parameters.Should().HaveCount(1);
        clause.Parameters[0].Value.Should().Be("%hn");
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
