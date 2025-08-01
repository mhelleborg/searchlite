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
        Expression<Func<TestEntity, bool>> predicate = x => !x.Name.Contains("test");
        
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
        Expression<Func<TestEntity, bool>> predicate = x => validNames.Contains(x.Name);
        
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
        Expression<Func<TestEntity, bool>> predicate = x => x.Name.Contains(searchTerm);
        
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
            x.Name.Contains("John") && (validAges.Contains(x.Age) || x.IsActive);
        
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
        Expression<Func<TestEntity, bool>> predicate = x => x.Name!.StartsWith("test", StringComparison.OrdinalIgnoreCase);
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
        Expression<Func<TestEntity, bool>> predicate = x => !x.Name!.StartsWith("test", StringComparison.OrdinalIgnoreCase);
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
        Expression<Func<TestEntity, bool>> predicate = x => x.Name!.EndsWith("test", StringComparison.OrdinalIgnoreCase);
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
        Expression<Func<TestEntity, bool>> predicate = x => !x.Name!.EndsWith("test", StringComparison.OrdinalIgnoreCase);
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
        Expression<Func<TestEntity, bool>> predicate = x => x.Name!.Contains("test", StringComparison.OrdinalIgnoreCase);
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
        Expression<Func<TestEntity, bool>> predicate = x => !x.Name!.Contains("test", StringComparison.OrdinalIgnoreCase);
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
        Expression<Func<TestEntity, bool>> currentCultureIgnoreCasePredicate = x => x.Name!.StartsWith("test", StringComparison.CurrentCultureIgnoreCase);
        Expression<Func<TestEntity, bool>> invariantCultureIgnoreCasePredicate = x => x.Name!.StartsWith("test", StringComparison.InvariantCultureIgnoreCase);

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

    private class TestEntity
    {
        public int Age { get; set; }
        public string? Name { get; set; }
        public bool IsActive { get; set; }
    }
}