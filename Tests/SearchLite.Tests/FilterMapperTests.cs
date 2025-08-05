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
    public void Map_WithSimpleEqualityConditionReversed_ShouldReturnCorrectFilterNode()
    {
        Expression<Func<TestEntity, bool>> predicate = x => 30 == x.Age;
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
    public void Map_PropertyReference_ShouldExtractValue()
    {
        var testEntity = new TestEntity { Age = 30 };
        
        Expression<Func<TestEntity, bool>> predicate = x => x.Age == testEntity.Age;
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
        Expression<Func<TestEntity, bool>> equalMethodPredicate = x => x.Age.Equals(30);
        Expression<Func<TestEntity, bool>> notEqualMethodPredicate = x => !x.Age.Equals(30);
        Expression<Func<TestEntity, bool>> notEqualPredicate = x => x.Age != 30;
        Expression<Func<TestEntity, bool>> greaterThanPredicate = x => x.Age > 30;
        Expression<Func<TestEntity, bool>> greaterThanOrEqualPredicate = x => x.Age >= 30;
        Expression<Func<TestEntity, bool>> lessThanPredicate = x => x.Age < 30;
        Expression<Func<TestEntity, bool>> lessThanOrEqualPredicate = x => x.Age <= 30;
        var equalResult = FilterMapper.Map(equalPredicate);
        var equalMethodResult = FilterMapper.Map(equalMethodPredicate);
        var notEqualResult = FilterMapper.Map(notEqualPredicate);
        var notEqualMethodResult = FilterMapper.Map(notEqualMethodPredicate);
        var greaterThanResult = FilterMapper.Map(greaterThanPredicate);
        var greaterThanOrEqualResult = FilterMapper.Map(greaterThanOrEqualPredicate);
        var lessThanResult = FilterMapper.Map(lessThanPredicate);
        var lessThanOrEqualResult = FilterMapper.Map(lessThanOrEqualPredicate);
        ((FilterNode<TestEntity>.Condition)equalResult).Operator.Should().Be(Operator.Equal);
        ((FilterNode<TestEntity>.Condition)equalMethodResult).Operator.Should().Be(Operator.Equal);
        ((FilterNode<TestEntity>.Condition)notEqualResult).Operator.Should().Be(Operator.NotEqual);
        ((FilterNode<TestEntity>.Condition)notEqualMethodResult).Operator.Should().Be(Operator.NotEqual);
        ((FilterNode<TestEntity>.Condition)greaterThanResult).Operator.Should().Be(Operator.GreaterThan);
        ((FilterNode<TestEntity>.Condition)greaterThanOrEqualResult).Operator.Should().Be(Operator.GreaterThanOrEqual);
        ((FilterNode<TestEntity>.Condition)lessThanResult).Operator.Should().Be(Operator.LessThan);
        ((FilterNode<TestEntity>.Condition)lessThanOrEqualResult).Operator.Should().Be(Operator.LessThanOrEqual);
    }

        [Fact]
    public void Map_WithAllComparisonOperatorsReversed_ShouldReturnCorrectOperators()
    {
        // Test all supported comparison operators
        Expression<Func<TestEntity, bool>> equalPredicate = x => 30 == x.Age;
        Expression<Func<TestEntity, bool>> equalMethodPredicate = x => 30.Equals(x.Age);
        Expression<Func<TestEntity, bool>> notEqualMethodPredicate = x => !30.Equals(x.Age);
        Expression<Func<TestEntity, bool>> notEqualPredicate = x => 30 != x.Age;
        Expression<Func<TestEntity, bool>> greaterThanPredicate = x => 30 < x.Age;
        Expression<Func<TestEntity, bool>> greaterThanOrEqualPredicate = x => 30 <= x.Age;
        Expression<Func<TestEntity, bool>> lessThanPredicate = x => 30 > x.Age;
        Expression<Func<TestEntity, bool>> lessThanOrEqualPredicate = x => 30 >= x.Age;
        var equalResult = FilterMapper.Map(equalPredicate);
        var equalMethodResult = FilterMapper.Map(equalMethodPredicate);
        var notEqualResult = FilterMapper.Map(notEqualPredicate);
        var notEqualMethodResult = FilterMapper.Map(notEqualMethodPredicate);
        var greaterThanResult = FilterMapper.Map(greaterThanPredicate);
        var greaterThanOrEqualResult = FilterMapper.Map(greaterThanOrEqualPredicate);
        var lessThanResult = FilterMapper.Map(lessThanPredicate);
        var lessThanOrEqualResult = FilterMapper.Map(lessThanOrEqualPredicate);
        ((FilterNode<TestEntity>.Condition)equalResult).Operator.Should().Be(Operator.Equal);
        ((FilterNode<TestEntity>.Condition)equalMethodResult).Operator.Should().Be(Operator.Equal);
        ((FilterNode<TestEntity>.Condition)notEqualResult).Operator.Should().Be(Operator.NotEqual);
        ((FilterNode<TestEntity>.Condition)notEqualMethodResult).Operator.Should().Be(Operator.NotEqual);
        ((FilterNode<TestEntity>.Condition)greaterThanResult).Operator.Should().Be(Operator.GreaterThan);
        ((FilterNode<TestEntity>.Condition)greaterThanOrEqualResult).Operator.Should().Be(Operator.GreaterThanOrEqual);
        ((FilterNode<TestEntity>.Condition)lessThanResult).Operator.Should().Be(Operator.LessThan);
        ((FilterNode<TestEntity>.Condition)lessThanOrEqualResult).Operator.Should().Be(Operator.LessThanOrEqual);
    }
    
    [Fact]
    public void Map_Composite()
    {
        Expression<Func<TestEntity, bool>> compositeEqual = x => x.Age.Equals(30) || x.Age.Equals(40);

        var compositeMapped = FilterMapper.Map(compositeEqual);

        ((FilterNode<TestEntity>.Group)compositeMapped).Operator.Should().Be(LogicalOperator.Or);
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

    [Fact]
    public void Map_WithDeepNestedExpressions_ShouldReturnCorrectNestedStructure()
    {
        // Test deeper nesting: (A && B) || (C && (D || E))
        Expression<Func<TestEntity, bool>> predicate = x =>
            (x.Age > 30 && x.Name == "John") ||
            (x.IsActive && (x.Age < 20 || x.Name == "Jane"));

        var result = FilterMapper.Map(predicate);

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
                            PropertyName = "IsActive",
                            PropertyType = typeof(bool),
                            Operator = Operator.Equal,
                            Value = true
                        },
                        new FilterNode<TestEntity>.Group
                        {
                            Operator = LogicalOperator.Or,
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
                                    PropertyName = "Name",
                                    PropertyType = typeof(string),
                                    Operator = Operator.Equal,
                                    Value = "Jane"
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithComplexTripleNestedExpression_ShouldReturnCorrectStructure()
    {
        // Test: ((A && B) || C) && (D || (E && F))
        Expression<Func<TestEntity, bool>> predicate = x =>
            ((x.Age > 30 && x.Name == "John") || x.IsActive) &&
            (x.Age < 50 || (x.Name != "Bob" && x.IsActive));

        var result = FilterMapper.Map(predicate);

        result.Should().BeOfType<FilterNode<TestEntity>.Group>();
        var rootGroup = (FilterNode<TestEntity>.Group)result;
        rootGroup.Operator.Should().Be(LogicalOperator.And);
        rootGroup.Conditions.Should().HaveCount(2);

        // First condition should be: (Age > 30 && Name == "John") || IsActive
        var firstCondition = rootGroup.Conditions[0].Should().BeOfType<FilterNode<TestEntity>.Group>().Subject;
        firstCondition.Operator.Should().Be(LogicalOperator.Or);

        // Second condition should be: Age < 50 || (Name != "Bob" && IsActive)
        var secondCondition = rootGroup.Conditions[1].Should().BeOfType<FilterNode<TestEntity>.Group>().Subject;
        secondCondition.Operator.Should().Be(LogicalOperator.Or);
    }

    [Fact]
    public void Map_WithStringIsNullOrEmpty_ShouldReturnIsNullOrEmptyOperator()
    {
        Expression<Func<TestEntity, bool>> predicate = x => string.IsNullOrEmpty(x.Name);

        Action act = () => FilterMapper.Map(predicate);

        // This should work once we implement string.IsNullOrEmpty support
        act.Should().NotThrow();

        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Name",
            PropertyType = typeof(string),
            Operator = Operator.IsNullOrEmpty,
            Value = true
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithNegatedStringIsNullOrEmpty_ShouldReturnNotIsNullOrEmptyOperator()
    {
        Expression<Func<TestEntity, bool>> predicate = x => !string.IsNullOrEmpty(x.Name);

        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Name",
            PropertyType = typeof(string),
            Operator = Operator.IsNotNullOrEmpty,
            Value = true
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithStringIsNullOrWhiteSpace_ShouldReturnIsNullOrWhiteSpaceOperator()
    {
        Expression<Func<TestEntity, bool>> predicate = x => string.IsNullOrWhiteSpace(x.Name);

        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Name",
            PropertyType = typeof(string),
            Operator = Operator.IsNullOrWhiteSpace,
            Value = true
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithNegatedStringIsNullOrWhiteSpace_ShouldReturnNotIsNullOrWhiteSpaceOperator()
    {
        Expression<Func<TestEntity, bool>> predicate = x => !string.IsNullOrWhiteSpace(x.Name);

        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Name",
            PropertyType = typeof(string),
            Operator = Operator.IsNotNullOrWhiteSpace,
            Value = true
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithStringIsNullOrEmptyInComplexExpression_ShouldWorkCorrectly()
    {
        Expression<Func<TestEntity, bool>> predicate = x =>
            x.Age > 25 && (string.IsNullOrEmpty(x.Name) || x.IsActive);

        var result = FilterMapper.Map(predicate);

        result.Should().BeOfType<FilterNode<TestEntity>.Group>();
        var rootGroup = (FilterNode<TestEntity>.Group)result;
        rootGroup.Operator.Should().Be(LogicalOperator.And);
        rootGroup.Conditions.Should().HaveCount(2);

        // First condition: Age > 25
        var firstCondition = rootGroup.Conditions[0].Should().BeOfType<FilterNode<TestEntity>.Condition>().Subject;
        firstCondition.PropertyName.Should().Be("Age");
        firstCondition.Operator.Should().Be(Operator.GreaterThan);

        // Second condition: string.IsNullOrEmpty(Name) || IsActive
        var secondCondition = rootGroup.Conditions[1].Should().BeOfType<FilterNode<TestEntity>.Group>().Subject;
        secondCondition.Operator.Should().Be(LogicalOperator.Or);

        var nameNullCheck = secondCondition.Conditions[0].Should().BeOfType<FilterNode<TestEntity>.Condition>().Subject;
        nameNullCheck.PropertyName.Should().Be("Name");
        nameNullCheck.Operator.Should().Be(Operator.IsNullOrEmpty);
    }

    [Fact]
    public void Map_WithStringContains_ShouldReturnContainsOperator()
    {
        Expression<Func<TestEntity, bool>> predicate = x => x.Name!.Contains("test");

        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Name",
            PropertyType = typeof(string),
            Operator = Operator.Contains,
            Value = "test"
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithNegatedStringContains_ShouldReturnNotContainsOperator()
    {
        Expression<Func<TestEntity, bool>> predicate = x => !x.Name!.Contains("test");

        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Name",
            PropertyType = typeof(string),
            Operator = Operator.NotContains,
            Value = "test"
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithCollectionContains_ShouldReturnInOperator()
    {
        var validAges = new[] { 25, 30, 35 };
        Expression<Func<TestEntity, bool>> predicate = x => validAges.Contains(x.Age);

        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Age",
            PropertyType = typeof(int),
            Operator = Operator.In,
            Value = validAges
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithNegatedCollectionContains_ShouldReturnNotInOperator()
    {
        var validAges = new[] { 25, 30, 35 };
        Expression<Func<TestEntity, bool>> predicate = x => !validAges.Contains(x.Age);

        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Age",
            PropertyType = typeof(int),
            Operator = Operator.NotIn,
            Value = validAges
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithListContains_ShouldReturnInOperator()
    {
        var validNames = new List<string> { "John", "Jane", "Bob" };
        Expression<Func<TestEntity, bool>> predicate = x => validNames.Contains(x.Name!);

        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Name",
            PropertyType = typeof(string),
            Operator = Operator.In,
            Value = validNames
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithEnumerableContains_ShouldReturnInOperator()
    {
        var validAges = new[] { 25, 30, 35 };
        Expression<Func<TestEntity, bool>> predicate = x => Enumerable.Contains(validAges, x.Age);

        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Age",
            PropertyType = typeof(int),
            Operator = Operator.In,
            Value = validAges
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithStringContainsVariable_ShouldEvaluateVariable()
    {
        var searchTerm = "test";
        Expression<Func<TestEntity, bool>> predicate = x => x.Name!.Contains(searchTerm);

        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Name",
            PropertyType = typeof(string),
            Operator = Operator.Contains,
            Value = "test"
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithSetOperatorsInComplexExpression_ShouldWorkCorrectly()
    {
        var validAges = new[] { 25, 30, 35 };
        Expression<Func<TestEntity, bool>> predicate = x =>
            x.Name!.Contains("John") && (validAges.Contains(x.Age) || x.IsActive);

        var result = FilterMapper.Map(predicate);

        result.Should().BeOfType<FilterNode<TestEntity>.Group>();
        var rootGroup = (FilterNode<TestEntity>.Group)result;
        rootGroup.Operator.Should().Be(LogicalOperator.And);
        rootGroup.Conditions.Should().HaveCount(2);

        // First condition: Name.Contains("John")
        var firstCondition = rootGroup.Conditions[0].Should().BeOfType<FilterNode<TestEntity>.Condition>().Subject;
        firstCondition.PropertyName.Should().Be("Name");
        firstCondition.Operator.Should().Be(Operator.Contains);
        firstCondition.Value.Should().Be("John");

        // Second condition: validAges.Contains(Age) || IsActive
        var secondCondition = rootGroup.Conditions[1].Should().BeOfType<FilterNode<TestEntity>.Group>().Subject;
        secondCondition.Operator.Should().Be(LogicalOperator.Or);

        var ageInCheck = secondCondition.Conditions[0].Should().BeOfType<FilterNode<TestEntity>.Condition>().Subject;
        ageInCheck.PropertyName.Should().Be("Age");
        ageInCheck.Operator.Should().Be(Operator.In);
        ageInCheck.Value.Should().BeEquivalentTo(validAges);
    }

    [Fact]
    public void Map_WithNullComparison_ShouldReturnNullOperators()
    {
        Expression<Func<TestEntity, bool>> equalNullPredicate = x => x.Name == null;
        Expression<Func<TestEntity, bool>> notEqualNullPredicate = x => x.Name != null;

        var equalResult = FilterMapper.Map(equalNullPredicate);
        var notEqualResult = FilterMapper.Map(notEqualNullPredicate);

        var expectedEqual = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Name",
            PropertyType = typeof(string),
            Operator = Operator.IsNull,
            Value = true
        };

        var expectedNotEqual = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Name",
            PropertyType = typeof(string),
            Operator = Operator.IsNotNull,
            Value = true
        };

        equalResult.Should().BeEquivalentTo(expectedEqual);
        notEqualResult.Should().BeEquivalentTo(expectedNotEqual);
    }

    [Fact]
    public void Map_WithStringStartsWith_ShouldReturnStartsWithOperator()
    {
        Expression<Func<TestEntity, bool>> predicate = x => x.Name!.StartsWith("test");
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Name",
            PropertyType = typeof(string),
            Operator = Operator.StartsWith,
            Value = "test"
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithNegatedStringStartsWith_ShouldReturnNotStartsWithOperator()
    {
        Expression<Func<TestEntity, bool>> predicate = x => !x.Name!.StartsWith("test");
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Name",
            PropertyType = typeof(string),
            Operator = Operator.NotStartsWith,
            Value = "test"
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithStringStartsWithIgnoreCase_ShouldReturnStartsWithIgnoreCaseOperator()
    {
        Expression<Func<TestEntity, bool>> predicate = x =>
            x.Name!.StartsWith("test", StringComparison.OrdinalIgnoreCase);
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Name",
            PropertyType = typeof(string),
            Operator = Operator.StartsWithIgnoreCase,
            Value = "test"
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithNegatedStringStartsWithIgnoreCase_ShouldReturnNotStartsWithIgnoreCaseOperator()
    {
        Expression<Func<TestEntity, bool>> predicate = x =>
            !x.Name!.StartsWith("test", StringComparison.OrdinalIgnoreCase);
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Name",
            PropertyType = typeof(string),
            Operator = Operator.NotStartsWithIgnoreCase,
            Value = "test"
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithStringEndsWith_ShouldReturnEndsWithOperator()
    {
        Expression<Func<TestEntity, bool>> predicate = x => x.Name!.EndsWith("test");
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Name",
            PropertyType = typeof(string),
            Operator = Operator.EndsWith,
            Value = "test"
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithNegatedStringEndsWith_ShouldReturnNotEndsWithOperator()
    {
        Expression<Func<TestEntity, bool>> predicate = x => !x.Name!.EndsWith("test");
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Name",
            PropertyType = typeof(string),
            Operator = Operator.NotEndsWith,
            Value = "test"
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithStringEndsWithIgnoreCase_ShouldReturnEndsWithIgnoreCaseOperator()
    {
        Expression<Func<TestEntity, bool>>
            predicate = x => x.Name!.EndsWith("test", StringComparison.OrdinalIgnoreCase);
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Name",
            PropertyType = typeof(string),
            Operator = Operator.EndsWithIgnoreCase,
            Value = "test"
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithNegatedStringEndsWithIgnoreCase_ShouldReturnNotEndsWithIgnoreCaseOperator()
    {
        Expression<Func<TestEntity, bool>> predicate = x =>
            !x.Name!.EndsWith("test", StringComparison.OrdinalIgnoreCase);
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Name",
            PropertyType = typeof(string),
            Operator = Operator.NotEndsWithIgnoreCase,
            Value = "test"
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithStringContainsIgnoreCase_ShouldReturnContainsIgnoreCaseOperator()
    {
        Expression<Func<TestEntity, bool>>
            predicate = x => x.Name!.Contains("test", StringComparison.OrdinalIgnoreCase);
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Name",
            PropertyType = typeof(string),
            Operator = Operator.ContainsIgnoreCase,
            Value = "test"
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithNegatedStringContainsIgnoreCase_ShouldReturnNotContainsIgnoreCaseOperator()
    {
        Expression<Func<TestEntity, bool>> predicate = x =>
            !x.Name!.Contains("test", StringComparison.OrdinalIgnoreCase);
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Name",
            PropertyType = typeof(string),
            Operator = Operator.NotContainsIgnoreCase,
            Value = "test"
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithStringOperatorCaseSensitivityVariants_ShouldDetectCorrectly()
    {
        // Test different StringComparison values
        Expression<Func<TestEntity, bool>> ordinalPredicate = x => x.Name!.StartsWith("test", StringComparison.Ordinal);
        Expression<Func<TestEntity, bool>> currentCultureIgnoreCasePredicate =
            x => x.Name!.StartsWith("test", StringComparison.CurrentCultureIgnoreCase);
        Expression<Func<TestEntity, bool>> invariantCultureIgnoreCasePredicate =
            x => x.Name!.StartsWith("test", StringComparison.InvariantCultureIgnoreCase);

        var ordinalResult = FilterMapper.Map(ordinalPredicate);
        var currentCultureResult = FilterMapper.Map(currentCultureIgnoreCasePredicate);
        var invariantCultureResult = FilterMapper.Map(invariantCultureIgnoreCasePredicate);

        // Ordinal should be case-sensitive (regular StartsWith)
        ((FilterNode<TestEntity>.Condition)ordinalResult).Operator.Should().Be(Operator.StartsWith);

        // IgnoreCase variants should use IgnoreCase operators
        ((FilterNode<TestEntity>.Condition)currentCultureResult).Operator.Should().Be(Operator.StartsWithIgnoreCase);
        ((FilterNode<TestEntity>.Condition)invariantCultureResult).Operator.Should().Be(Operator.StartsWithIgnoreCase);
    }

    [Fact]
    public void Map_WithStringOperatorVariable_ShouldEvaluateVariable()
    {
        var searchTerm = "dynamic";
        Expression<Func<TestEntity, bool>> predicate = x => x.Name!.StartsWith(searchTerm);
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Name",
            PropertyType = typeof(string),
            Operator = Operator.StartsWith,
            Value = "dynamic"
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithComplexStringOperatorExpression_ShouldMapCorrectly()
    {
        var prefix = "pre";
        var suffix = "suf";
        Expression<Func<TestEntity, bool>> predicate = x =>
            x.Name!.StartsWith(prefix) && (x.Name.EndsWith(suffix) || x.Name.Contains("middle"));
        var result = FilterMapper.Map(predicate);

        result.Should().BeOfType<FilterNode<TestEntity>.Group>();
        var group = (FilterNode<TestEntity>.Group)result;
        group.Operator.Should().Be(LogicalOperator.And);
        group.Conditions.Should().HaveCount(2);

        // First condition: Name.StartsWith(prefix)
        var firstCondition = (FilterNode<TestEntity>.Condition)group.Conditions[0];
        firstCondition.PropertyName.Should().Be("Name");
        firstCondition.Operator.Should().Be(Operator.StartsWith);
        firstCondition.Value.Should().Be("pre");

        // Second condition: (Name.EndsWith(suffix) || Name.Contains("middle"))
        var secondCondition = (FilterNode<TestEntity>.Group)group.Conditions[1];
        secondCondition.Operator.Should().Be(LogicalOperator.Or);
        secondCondition.Conditions.Should().HaveCount(2);

        var endsWith = (FilterNode<TestEntity>.Condition)secondCondition.Conditions[0];
        endsWith.Operator.Should().Be(Operator.EndsWith);
        endsWith.Value.Should().Be("suf");

        var contains = (FilterNode<TestEntity>.Condition)secondCondition.Conditions[1];
        contains.Operator.Should().Be(Operator.Contains);
        contains.Value.Should().Be("middle");
    }

    [Fact]
    public void Map_WithDoubleRatingComparison_ShouldHandleDoubleValues()
    {
        Expression<Func<TestEntity, bool>> predicate = x => x.Rating > 4.5;
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Rating",
            PropertyType = typeof(double),
            Operator = Operator.GreaterThan,
            Value = 4.5
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithRatingRangeComparison_ShouldHandleComplexDoubleConditions()
    {
        Expression<Func<TestEntity, bool>> predicate = x => x.Rating >= 3.0 && x.Rating <= 5.0;
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Group
        {
            Operator = LogicalOperator.And,
            Conditions =
            [
                new FilterNode<TestEntity>.Condition
                {
                    PropertyName = "Rating",
                    PropertyType = typeof(double),
                    Operator = Operator.GreaterThanOrEqual,
                    Value = 3.0
                },
                new FilterNode<TestEntity>.Condition
                {
                    PropertyName = "Rating",
                    PropertyType = typeof(double),
                    Operator = Operator.LessThanOrEqual,
                    Value = 5.0
                }
            ]
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithDateTimeComparison_ShouldHandleDateTimeValues()
    {
        var date = new DateTime(2025, 1, 1);
        Expression<Func<TestEntity, bool>> predicate = x => x.CreatedAt >= date;
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "CreatedAt",
            PropertyType = typeof(DateTime),
            Operator = Operator.GreaterThanOrEqual,
            Value = date
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithDateTimeRangeComparison_ShouldHandleComplexDateTimeConditions()
    {
        var startDate = new DateTime(2025, 1, 1);
        var endDate = new DateTime(2025, 12, 31);

        Expression<Func<TestEntity, bool>> predicate = x => x.CreatedAt >= startDate && x.CreatedAt <= endDate;
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Group
        {
            Operator = LogicalOperator.And,
            Conditions =
            [
                new FilterNode<TestEntity>.Condition
                {
                    PropertyName = "CreatedAt",
                    PropertyType = typeof(DateTime),
                    Operator = Operator.GreaterThanOrEqual,
                    Value = startDate
                },
                new FilterNode<TestEntity>.Condition
                {
                    PropertyName = "CreatedAt",
                    PropertyType = typeof(DateTime),
                    Operator = Operator.LessThanOrEqual,
                    Value = endDate
                }
            ]
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithDateTimeOffsetComparison_ShouldHandleDateTimeOffsetValues()
    {
        var date = new DateTimeOffset(new DateTime(2025, 1, 1), TimeSpan.FromHours(-5));
        Expression<Func<TestEntity, bool>> predicate = x => x.CreatedAtTz >= date;
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "CreatedAtTz",
            PropertyType = typeof(DateTimeOffset),
            Operator = Operator.GreaterThanOrEqual,
            Value = date
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithDateTimeOffsetRangeComparison_ShouldHandleComplexDateTimeOffsetConditions()
    {
        var startDate = new DateTimeOffset(new DateTime(2025, 1, 1), TimeSpan.FromHours(-5));
        var endDate = new DateTimeOffset(new DateTime(2025, 12, 31), TimeSpan.FromHours(-5));

        Expression<Func<TestEntity, bool>> predicate = x => x.CreatedAtTz >= startDate && x.CreatedAtTz <= endDate;
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Group
        {
            Operator = LogicalOperator.And,
            Conditions =
            [
                new FilterNode<TestEntity>.Condition
                {
                    PropertyName = "CreatedAtTz",
                    PropertyType = typeof(DateTimeOffset),
                    Operator = Operator.GreaterThanOrEqual,
                    Value = startDate
                },
                new FilterNode<TestEntity>.Condition
                {
                    PropertyName = "CreatedAtTz",
                    PropertyType = typeof(DateTimeOffset),
                    Operator = Operator.LessThanOrEqual,
                    Value = endDate
                }
            ]
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithGuidComparison_ShouldHandleGuidValues()
    {
        var testGuid = Guid.NewGuid();
        Expression<Func<TestEntity, bool>> predicate = x => x.Id == testGuid;
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Id",
            PropertyType = typeof(Guid),
            Operator = Operator.Equal,
            Value = testGuid
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithGuidNotEqualComparison_ShouldHandleGuidValues()
    {
        var testGuid = Guid.NewGuid();
        Expression<Func<TestEntity, bool>> predicate = x => x.Id != testGuid;
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Id",
            PropertyType = typeof(Guid),
            Operator = Operator.NotEqual,
            Value = testGuid
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithGuidCollectionContains_ShouldHandleGuidInOperator()
    {
        var validIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        Expression<Func<TestEntity, bool>> predicate = x => validIds.Contains(x.Id);
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Id",
            PropertyType = typeof(Guid),
            Operator = Operator.In,
            Value = validIds
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithEnumComparison_ShouldHandleEnumValues()
    {
        Expression<Func<TestEntity, bool>> predicate = x => x.Status == TestStatus.Active;
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Status",
            PropertyType = typeof(TestStatus),
            Operator = Operator.Equal,
            Value = 1 // TestStatus.Active has underlying value 1
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithEnumNotEqualComparison_ShouldHandleEnumValues()
    {
        Expression<Func<TestEntity, bool>> predicate = x => x.Status != TestStatus.Pending;
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Status",
            PropertyType = typeof(TestStatus),
            Operator = Operator.NotEqual,
            Value = 0 // TestStatus.Pending has underlying value 0
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithEnumGreaterThanComparison_ShouldHandleEnumValues()
    {
        Expression<Func<TestEntity, bool>> predicate = x => x.Status > TestStatus.Pending;
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Status",
            PropertyType = typeof(TestStatus),
            Operator = Operator.GreaterThan,
            Value = 0 // TestStatus.Pending has underlying value 0
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithEnumCollectionContains_ShouldHandleEnumInOperator()
    {
        var validStatuses = new[] { TestStatus.Active, TestStatus.Completed };
        Expression<Func<TestEntity, bool>> predicate = x => validStatuses.Contains(x.Status);
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Status",
            PropertyType = typeof(TestStatus),
            Operator = Operator.In,
            Value = validStatuses // Array of enum values is preserved correctly
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithNullableEnumComparison_ShouldHandleNullableEnumValues()
    {
        Expression<Func<TestEntity, bool>> predicate = x => x.NullableStatus == TestStatus.Active;
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "NullableStatus",
            PropertyType = typeof(TestStatus?),
            Operator = Operator.Equal,
            Value = 1 // TestStatus.Active has underlying value 1
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithNullableEnumNullComparison_ShouldHandleNullableEnumNullValues()
    {
        Expression<Func<TestEntity, bool>> predicate = x => x.NullableStatus == null;
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "NullableStatus",
            PropertyType = typeof(TestStatus?),
            Operator = Operator.IsNull,
            Value = true
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithByteComparison_ShouldHandleByteValues()
    {
        Expression<Func<TestEntity, bool>> predicate = x => x.ByteValue >= 100;
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "ByteValue",
            PropertyType = typeof(byte),
            Operator = Operator.GreaterThanOrEqual,
            Value = (byte)100
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithShortComparison_ShouldHandleShortValues()
    {
        Expression<Func<TestEntity, bool>> predicate = x => x.ShortValue < 1000;
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "ShortValue",
            PropertyType = typeof(short),
            Operator = Operator.LessThan,
            Value = (short)1000
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithLongComparison_ShouldHandleLongValues()
    {
        Expression<Func<TestEntity, bool>> predicate = x => x.LongValue > 1000000L;
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "LongValue",
            PropertyType = typeof(long),
            Operator = Operator.GreaterThan,
            Value = 1000000L
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithFloatComparison_ShouldHandleFloatValues()
    {
        Expression<Func<TestEntity, bool>> predicate = x => x.FloatValue <= 3.14f;
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "FloatValue",
            PropertyType = typeof(float),
            Operator = Operator.LessThanOrEqual,
            Value = 3.14f
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithDecimalComparison_ShouldHandleDecimalValues()
    {
        Expression<Func<TestEntity, bool>> predicate = x => x.DecimalValue == 99.99m;
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "DecimalValue",
            PropertyType = typeof(decimal),
            Operator = Operator.Equal,
            Value = 99.99m
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithCharComparison_ShouldHandleCharValues()
    {
        Expression<Func<TestEntity, bool>> predicate = x => x.CharValue == 'A';
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "CharValue",
            PropertyType = typeof(char),
            Operator = Operator.Equal,
            Value = 65 // 'A' has ASCII value 65
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithCharRangeComparison_ShouldHandleCharRangeValues()
    {
        Expression<Func<TestEntity, bool>> predicate = x => x.CharValue >= 'A' && x.CharValue <= 'Z';
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Group
        {
            Operator = LogicalOperator.And,
            Conditions =
            [
                new FilterNode<TestEntity>.Condition
                {
                    PropertyName = "CharValue",
                    PropertyType = typeof(char),
                    Operator = Operator.GreaterThanOrEqual,
                    Value = 65 // 'A' has ASCII value 65
                },
                new FilterNode<TestEntity>.Condition
                {
                    PropertyName = "CharValue",
                    PropertyType = typeof(char),
                    Operator = Operator.LessThanOrEqual,
                    Value = 90 // 'Z' has ASCII value 90
                }
            ]
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithMixedTypesInComplexExpression_ShouldHandleAllTypes()
    {
        var testGuid = Guid.NewGuid();
        Expression<Func<TestEntity, bool>> predicate = x => 
            x.Id == testGuid && 
            x.Status == TestStatus.Active && 
            x.Age > 18 && 
            x.Rating >= 4.0 &&
            x.IsActive;
        
        var result = FilterMapper.Map(predicate);

        result.Should().BeOfType<FilterNode<TestEntity>.Group>();
        var rootGroup = (FilterNode<TestEntity>.Group)result;
        rootGroup.Operator.Should().Be(LogicalOperator.And);
        
        // The expression tree creates nested groups due to how && associates,
        // so we need to flatten and find all conditions recursively
        var allConditions = GetAllConditions(rootGroup);
        allConditions.Should().HaveCount(5);

        // Verify each condition type exists
        var guidCondition = allConditions.FirstOrDefault(c => c.PropertyName == "Id");
        guidCondition.Should().NotBeNull();
        guidCondition!.PropertyType.Should().Be(typeof(Guid));
        guidCondition.Value.Should().Be(testGuid);

        var enumCondition = allConditions.FirstOrDefault(c => c.PropertyName == "Status");
        enumCondition.Should().NotBeNull();
        enumCondition!.PropertyType.Should().Be(typeof(TestStatus));
        enumCondition.Value.Should().Be(1); // TestStatus.Active has underlying value 1

        var intCondition = allConditions.FirstOrDefault(c => c.PropertyName == "Age");
        intCondition.Should().NotBeNull();
        intCondition!.PropertyType.Should().Be(typeof(int));
        intCondition.Value.Should().Be(18);

        var doubleCondition = allConditions.FirstOrDefault(c => c.PropertyName == "Rating");
        doubleCondition.Should().NotBeNull();
        doubleCondition!.PropertyType.Should().Be(typeof(double));
        doubleCondition.Value.Should().Be(4.0);

        var boolCondition = allConditions.FirstOrDefault(c => c.PropertyName == "IsActive");
        boolCondition.Should().NotBeNull();
        boolCondition!.PropertyType.Should().Be(typeof(bool));
        boolCondition.Value.Should().Be(true);
    }

    private static List<FilterNode<TestEntity>.Condition> GetAllConditions(FilterNode<TestEntity> node)
    {
        var conditions = new List<FilterNode<TestEntity>.Condition>();
        
        switch (node)
        {
            case FilterNode<TestEntity>.Condition condition:
                conditions.Add(condition);
                break;
            case FilterNode<TestEntity>.Group group:
                foreach (var child in group.Conditions)
                {
                    conditions.AddRange(GetAllConditions(child));
                }
                break;
        }
        
        return conditions;
    }

    [Fact]
    public void Map_WithNumericTypesInCollection_ShouldHandleInOperator()
    {
        var validBytes = new byte[] { 1, 2, 3 };
        var validShorts = new short[] { 100, 200, 300 };
        var validLongs = new long[] { 1000L, 2000L, 3000L };
        var validFloats = new float[] { 1.1f, 2.2f, 3.3f };
        var validDecimals = new decimal[] { 10.5m, 20.5m, 30.5m };

        Expression<Func<TestEntity, bool>> bytePredicate = x => validBytes.Contains(x.ByteValue);
        Expression<Func<TestEntity, bool>> shortPredicate = x => validShorts.Contains(x.ShortValue);
        Expression<Func<TestEntity, bool>> longPredicate = x => validLongs.Contains(x.LongValue);
        Expression<Func<TestEntity, bool>> floatPredicate = x => validFloats.Contains(x.FloatValue);
        Expression<Func<TestEntity, bool>> decimalPredicate = x => validDecimals.Contains(x.DecimalValue);

        var byteResult = FilterMapper.Map(bytePredicate);
        var shortResult = FilterMapper.Map(shortPredicate);
        var longResult = FilterMapper.Map(longPredicate);
        var floatResult = FilterMapper.Map(floatPredicate);
        var decimalResult = FilterMapper.Map(decimalPredicate);

        ((FilterNode<TestEntity>.Condition)byteResult).Operator.Should().Be(Operator.In);
        ((FilterNode<TestEntity>.Condition)byteResult).PropertyType.Should().Be(typeof(byte));

        ((FilterNode<TestEntity>.Condition)shortResult).Operator.Should().Be(Operator.In);
        ((FilterNode<TestEntity>.Condition)shortResult).PropertyType.Should().Be(typeof(short));

        ((FilterNode<TestEntity>.Condition)longResult).Operator.Should().Be(Operator.In);
        ((FilterNode<TestEntity>.Condition)longResult).PropertyType.Should().Be(typeof(long));

        ((FilterNode<TestEntity>.Condition)floatResult).Operator.Should().Be(Operator.In);
        ((FilterNode<TestEntity>.Condition)floatResult).PropertyType.Should().Be(typeof(float));

        ((FilterNode<TestEntity>.Condition)decimalResult).Operator.Should().Be(Operator.In);
        ((FilterNode<TestEntity>.Condition)decimalResult).PropertyType.Should().Be(typeof(decimal));
    }

    private class TestEntity
    {
        public int Age { get; set; }
        public string? Name { get; set; }
        public bool IsActive { get; set; }
        public double Rating { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTimeOffset CreatedAtTz { get; set; }
        public Guid Id { get; set; }
        public TestStatus Status { get; set; }
        public byte ByteValue { get; set; }
        public short ShortValue { get; set; }
        public long LongValue { get; set; }
        public float FloatValue { get; set; }
        public decimal DecimalValue { get; set; }
        public char CharValue { get; set; }
        public TestStatus? NullableStatus { get; set; }
    }

    private enum TestStatus
    {
        Pending = 0,
        Active = 1,
        Inactive = 2,
        Completed = 3
    }

    [Fact]
    public void Map_WithStringSerializedEnum_ShouldWorkCorrectly()
    {
        // Test that the FilterMapper still works correctly for string-serialized enums
        // (The actual string vs integer serialization is handled in the WHERE clause builders)
        Expression<Func<TestEntity, bool>> predicate = x => x.Status == TestStatus.Active;
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "Status",
            PropertyType = typeof(TestStatus),
            Operator = Operator.Equal,
            Value = 1 // FilterMapper always converts enums to integers
        };

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Map_WithStringSerializedEnumCollection_ShouldWorkCorrectly()
    {
        // Test that the FilterMapper still works correctly for string-serialized enum collections
        var validStatuses = new[] { TestStatus.Active, TestStatus.Completed };
        Expression<Func<TestEntity, bool>> predicate = x => validStatuses.Contains(x.Status);
        var result = FilterMapper.Map(predicate);

        var condition = result.Should().BeOfType<FilterNode<TestEntity>.Condition>().Subject;
        condition.PropertyName.Should().Be("Status");
        condition.PropertyType.Should().Be(typeof(TestStatus));
        condition.Operator.Should().Be(Operator.In);
        
        // FilterMapper keeps the original enum collection (conversion happens in WHERE clause builders)
        var expectedValues = new[] { TestStatus.Active, TestStatus.Completed };
        condition.Value.Should().BeEquivalentTo(expectedValues);
    }

    [Fact]
    public void Map_WithNullableStringSerializedEnum_ShouldWorkCorrectly()
    {
        // Test nullable enum handling for string serialization
        Expression<Func<TestEntity, bool>> predicate = x => x.NullableStatus == TestStatus.Active;
        var result = FilterMapper.Map(predicate);

        var expected = new FilterNode<TestEntity>.Condition
        {
            PropertyName = "NullableStatus",
            PropertyType = typeof(TestStatus?),
            Operator = Operator.Equal,
            Value = 1 // FilterMapper always converts enums to integers
        };

        result.Should().BeEquivalentTo(expected);
    }
}
