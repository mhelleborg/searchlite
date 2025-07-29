using System.Linq.Expressions;
using FluentAssertions;

namespace SearchLite.Tests;

public class FilterMapperTests
{
    [Fact]
    public void Map_WithSimpleEqualityCondition_ShouldReturnCorrectFilterNode()
    {
        Expression<Func<TestEntity, bool>> predicate = x => x.Age == 30;
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Age",
            PropertyType = typeof(int),
            Operator = Operator.Equal,
            Value = 30
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithAndExpression_ShouldReturnGroupWithAndOperator()
    {
        Expression<Func<TestEntity, bool>> predicate = x => x.Age > 30 && x.Name == "John";
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Group
        {
            Operator = LogicalOperator.And,
            Conditions =
            [
                new FilterNode<TestEntity>.Condition
                {
                    PropertyName = "Age",
                    PropertyType = typeof(int),
                    Operator = Operator.GreaterThan,
                    Value = 30
                },

                new FilterNode<TestEntity>.Condition
                {
                    PropertyName = "Name",
                    PropertyType = typeof(string),
                    Operator = Operator.Equal,
                    Value = "John"
                }
            ]
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithOrExpression_ShouldReturnGroupWithOrOperator()
    {
        Expression<Func<TestEntity, bool>> predicate = x => x.Age > 30 || x.Name == "John";
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Group
        {
            Operator = LogicalOperator.Or,
            Conditions =
            [
                new FilterNode<TestEntity>.Condition
                {
                    PropertyName = "Age",
                    PropertyType = typeof(int),
                    Operator = Operator.GreaterThan,
                    Value = 30
                },

                new FilterNode<TestEntity>.Condition
                {
                    PropertyName = "Name",
                    PropertyType = typeof(string),
                    Operator = Operator.Equal,
                    Value = "John"
                }
            ]
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithUnsupportedOperator_ShouldThrowNotSupportedException()
    {
        Expression<Func<TestEntity, bool>> predicate = x => x.Age + 5 == 35;

        Action act = () => FilterMapper.Map(predicate);

        act.Should().Throw<NotSupportedException>().WithMessage("Operator Add is not supported");
    }

    [Fact]
    public void Map_WithTrueConstantExpression_ShouldReturnEmptyGroup()
    {
        Expression<Func<TestEntity, bool>> predicate = x => true;
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Group
        {
            Operator = LogicalOperator.And,
            Conditions = new List<FilterNode<TestEntity>>()
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithNestedLogicalExpressions_ShouldReturnCorrectNestedGroups()
    {
        // Create a complex predicate with nested AND/OR expressions
        Expression<Func<TestEntity, bool>> predicate = x =>
            (x.Age > 30 && x.Name == "John") || (x.Age < 20 && x.IsActive);
        var result = FilterMapper.Map(predicate);
        // Expected structure:
        // OR
        // ├── AND
        // │   ├── Age > 30
        // │   └── Name == "John"
        // └── AND
        //     ├── Age < 20
        //     └── IsActive == true
        var expected = new FilterNode<TestEntity>.Group
        {
            Operator = LogicalOperator.Or,
            Conditions =
            [
                new FilterNode<TestEntity>.Group
                {
                    Operator = LogicalOperator.And,
                    Conditions =
                    [
                        new FilterNode<TestEntity>.Condition
                        {
                            PropertyName = "Age",
                            PropertyType = typeof(int),
                            Operator = Operator.GreaterThan,
                            Value = 30
                        },
                        new FilterNode<TestEntity>.Condition
                        {
                            PropertyName = "Name",
                            PropertyType = typeof(string),
                            Operator = Operator.Equal,
                            Value = "John"
                        }
                    ]
                },
                new FilterNode<TestEntity>.Group
                {
                    Operator = LogicalOperator.And,
                    Conditions =
                    [
                        new FilterNode<TestEntity>.Condition
                        {
                            PropertyName = "Age",
                            PropertyType = typeof(int),
                            Operator = Operator.LessThan,
                            Value = 20
                        },
                        new FilterNode<TestEntity>.Condition
                        {
                            PropertyName = "IsActive",
                            PropertyType = typeof(bool),
                            Operator = Operator.Equal,
                            Value = true
                        }
                    ]
                }
            ]
        };
        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithAllComparisonOperators_ShouldReturnCorrectOperators()
    {
        // Test all supported comparison operators
        Expression<Func<TestEntity, bool>> equalPredicate = x => x.Age == 30;
        Expression<Func<TestEntity, bool>> notEqualPredicate = x => x.Age != 30;
        Expression<Func<TestEntity, bool>> greaterThanPredicate = x => x.Age > 30;
        Expression<Func<TestEntity, bool>> greaterThanOrEqualPredicate = x => x.Age >= 30;
        Expression<Func<TestEntity, bool>> lessThanPredicate = x => x.Age < 30;
        Expression<Func<TestEntity, bool>> lessThanOrEqualPredicate = x => x.Age <= 30;
        var equalResult = FilterMapper.Map(equalPredicate);
        var notEqualResult = FilterMapper.Map(notEqualPredicate);
        var greaterThanResult = FilterMapper.Map(greaterThanPredicate);
        var greaterThanOrEqualResult = FilterMapper.Map(greaterThanOrEqualPredicate);
        var lessThanResult = FilterMapper.Map(lessThanPredicate);
        var lessThanOrEqualResult = FilterMapper.Map(lessThanOrEqualPredicate);
        ((FilterNode<TestEntity>.Condition)equalResult).Operator.Should().Be(Operator.Equal);
        ((FilterNode<TestEntity>.Condition)notEqualResult).Operator.Should().Be(Operator.NotEqual);
        ((FilterNode<TestEntity>.Condition)greaterThanResult).Operator.Should().Be(Operator.GreaterThan);
        ((FilterNode<TestEntity>.Condition)greaterThanOrEqualResult).Operator.Should().Be(Operator.GreaterThanOrEqual);
        ((FilterNode<TestEntity>.Condition)lessThanResult).Operator.Should().Be(Operator.LessThan);
        ((FilterNode<TestEntity>.Condition)lessThanOrEqualResult).Operator.Should().Be(Operator.LessThanOrEqual);
    }

    [Fact]
    public void Map_WithStringComparison_ShouldHandleStringValues()
    {
        Expression<Func<TestEntity, bool>> predicate = x => x.Name != "Jane";
        var result = FilterMapper.Map(predicate);
        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Name",
            PropertyType = typeof(string),
            Operator = Operator.NotEqual,
            Value = "Jane"
        };
        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithBooleanComparison_ShouldHandleBooleanValues()
    {
        Expression<Func<TestEntity, bool>> predicate = x => x.IsActive == false;
        var result = FilterMapper.Map(predicate);
        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "IsActive",
            PropertyType = typeof(bool),
            Operator = Operator.Equal,
            Value = false
        };
        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithVariableInExpression_ShouldEvaluateVariable()
    {
        var targetAge = 25;
        Expression<Func<TestEntity, bool>> predicate = x => x.Age >= targetAge;
        var result = FilterMapper.Map(predicate);
        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Age",
            PropertyType = typeof(int),
            Operator = Operator.GreaterThanOrEqual,
            Value = 25
        };
        result.Should().BeEquivalentTo(expected);
    }

    private class TestEntity
    {
        public int Age { get; set; }
        public string? Name { get; set; }
        public bool IsActive { get; set; }
    }
}