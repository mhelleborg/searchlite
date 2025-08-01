using FluentAssertions;
using System.Linq.Expressions;

namespace SearchLite.Tests;

public class MethodSupportTests
{
    public class TestEntity
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? Description { get; set; }
    }

    [Fact]
    public void Map_WithEqualsMethod_ShouldReturnEqualOperator()
    {
        // Arrange
        Expression<Func<TestEntity, bool>> predicate = x => x.Name.Equals("John");
        
        // Act
        var result = FilterMapper.Map(predicate);
        
        // Assert
        result.Should().BeOfType<FilterNode<TestEntity>.Condition>();
        var condition = (FilterNode<TestEntity>.Condition)result;
        condition.PropertyName.Should().Be("Name");
        condition.Operator.Should().Be(Operator.Equal);
        condition.Value.Should().Be("John");
    }

    [Fact] 
    public void Map_WithNegatedEqualsMethod_ShouldReturnNotEqualOperator()
    {
        // Arrange
        Expression<Func<TestEntity, bool>> predicate = x => !x.Name.Equals("John");
        
        // Act
        var result = FilterMapper.Map(predicate);
        
        // Assert
        result.Should().BeOfType<FilterNode<TestEntity>.Condition>();
        var condition = (FilterNode<TestEntity>.Condition)result;
        condition.PropertyName.Should().Be("Name");
        condition.Operator.Should().Be(Operator.NotEqual);
        condition.Value.Should().Be("John");
    }

    [Fact]
    public void Map_WithIntegerEqualsMethod_ShouldWork()
    {
        // Arrange
        Expression<Func<TestEntity, bool>> predicate = x => x.Age.Equals(25);
        
        // Act
        var result = FilterMapper.Map(predicate);
        
        // Assert
        result.Should().BeOfType<FilterNode<TestEntity>.Condition>();
        var condition = (FilterNode<TestEntity>.Condition)result;
        condition.PropertyName.Should().Be("Age");
        condition.Operator.Should().Be(Operator.Equal);
        condition.Value.Should().Be(25);
    }

    [Fact]
    public void Map_WithCompareToEqualZero_ShouldReturnEqualOperator()
    {
        // Arrange
        Expression<Func<TestEntity, bool>> predicate = x => x.Name.CompareTo("John") == 0;
        
        // Act
        var result = FilterMapper.Map(predicate);
        
        // Assert
        result.Should().BeOfType<FilterNode<TestEntity>.Condition>();
        var condition = (FilterNode<TestEntity>.Condition)result;
        condition.PropertyName.Should().Be("Name");
        condition.Operator.Should().Be(Operator.Equal);
        condition.Value.Should().Be("John");
    }

    [Fact]
    public void Map_WithCompareToNotEqualZero_ShouldReturnNotEqualOperator()
    {
        // Arrange
        Expression<Func<TestEntity, bool>> predicate = x => x.Name.CompareTo("John") != 0;
        
        // Act
        var result = FilterMapper.Map(predicate);
        
        // Assert
        result.Should().BeOfType<FilterNode<TestEntity>.Condition>();
        var condition = (FilterNode<TestEntity>.Condition)result;
        condition.PropertyName.Should().Be("Name");
        condition.Operator.Should().Be(Operator.NotEqual);
        condition.Value.Should().Be("John");
    }

    [Fact]
    public void Map_WithCompareToGreaterThanZero_ShouldReturnGreaterThanOperator()
    {
        // Arrange
        Expression<Func<TestEntity, bool>> predicate = x => x.Name.CompareTo("John") > 0;
        
        // Act
        var result = FilterMapper.Map(predicate);
        
        // Assert
        result.Should().BeOfType<FilterNode<TestEntity>.Condition>();
        var condition = (FilterNode<TestEntity>.Condition)result;
        condition.PropertyName.Should().Be("Name");
        condition.Operator.Should().Be(Operator.GreaterThan);
        condition.Value.Should().Be("John");
    }

    [Fact]
    public void Map_WithCompareToLessThanZero_ShouldReturnLessThanOperator()
    {
        // Arrange
        Expression<Func<TestEntity, bool>> predicate = x => x.Name.CompareTo("John") < 0;
        
        // Act
        var result = FilterMapper.Map(predicate);
        
        // Assert
        result.Should().BeOfType<FilterNode<TestEntity>.Condition>();
        var condition = (FilterNode<TestEntity>.Condition)result;
        condition.PropertyName.Should().Be("Name");
        condition.Operator.Should().Be(Operator.LessThan);
        condition.Value.Should().Be("John");
    }

    [Fact]
    public void Map_WithCompareToGreaterThanOrEqualZero_ShouldReturnGreaterThanOrEqualOperator()
    {
        // Arrange
        Expression<Func<TestEntity, bool>> predicate = x => x.Name.CompareTo("John") >= 0;
        
        // Act
        var result = FilterMapper.Map(predicate);
        
        // Assert
        result.Should().BeOfType<FilterNode<TestEntity>.Condition>();
        var condition = (FilterNode<TestEntity>.Condition)result;
        condition.PropertyName.Should().Be("Name");
        condition.Operator.Should().Be(Operator.GreaterThanOrEqual);
        condition.Value.Should().Be("John");
    }

    [Fact]
    public void Map_WithCompareToLessThanOrEqualZero_ShouldReturnLessThanOrEqualOperator()
    {
        // Arrange
        Expression<Func<TestEntity, bool>> predicate = x => x.Name.CompareTo("John") <= 0;
        
        // Act
        var result = FilterMapper.Map(predicate);
        
        // Assert
        result.Should().BeOfType<FilterNode<TestEntity>.Condition>();
        var condition = (FilterNode<TestEntity>.Condition)result;
        condition.PropertyName.Should().Be("Name");
        condition.Operator.Should().Be(Operator.LessThanOrEqual);
        condition.Value.Should().Be("John");
    }

    [Fact]
    public void Map_WithIntegerCompareToEqualZero_ShouldWork()
    {
        // Arrange
        Expression<Func<TestEntity, bool>> predicate = x => x.Age.CompareTo(25) == 0;
        
        // Act
        var result = FilterMapper.Map(predicate);
        
        // Assert
        result.Should().BeOfType<FilterNode<TestEntity>.Condition>();
        var condition = (FilterNode<TestEntity>.Condition)result;
        condition.PropertyName.Should().Be("Age");
        condition.Operator.Should().Be(Operator.Equal);
        condition.Value.Should().Be(25);
    }

    [Fact]
    public void Map_WithDateTimeCompareToGreaterThanZero_ShouldWork()
    {
        // Arrange
        var targetDate = new DateTime(2023, 1, 1);
        Expression<Func<TestEntity, bool>> predicate = x => x.CreatedAt.CompareTo(targetDate) > 0;
        
        // Act
        var result = FilterMapper.Map(predicate);
        
        // Assert
        result.Should().BeOfType<FilterNode<TestEntity>.Condition>();
        var condition = (FilterNode<TestEntity>.Condition)result;
        condition.PropertyName.Should().Be("CreatedAt");
        condition.Operator.Should().Be(Operator.GreaterThan);
        condition.Value.Should().Be(targetDate);
    }

    [Fact]
    public void Map_WithCompareToInvalidComparison_ShouldThrowException()
    {
        // Arrange
        Expression<Func<TestEntity, bool>> predicate = x => x.Name.CompareTo("John") == 1;
        
        // Act & Assert
        var exception = Assert.Throws<NotSupportedException>(() => FilterMapper.Map(predicate));
        exception.Message.Should().Contain("CompareTo with operator Equal and result 1 is not supported");
    }

    [Fact]
    public void Map_WithCompareToValidComparison_ShouldWork()
    {
        // Arrange - Test that CompareTo with valid comparison operators works
        Expression<Func<TestEntity, bool>> predicate = x => x.Name.CompareTo("John") > 0;
        
        // Act
        var result = FilterMapper.Map(predicate);
        
        // Assert
        result.Should().BeOfType<FilterNode<TestEntity>.Condition>();
        var condition = (FilterNode<TestEntity>.Condition)result;
        condition.PropertyName.Should().Be("Name");
        condition.Operator.Should().Be(Operator.GreaterThan);
        condition.Value.Should().Be("John");
    }

    [Fact]
    public void Map_WithCombinedMethodsAndOperators_ShouldWork()
    {
        // Arrange
        Expression<Func<TestEntity, bool>> predicate = x => 
            x.Name.Equals("John") && x.Age.CompareTo(25) > 0;
        
        // Act
        var result = FilterMapper.Map(predicate);
        
        // Assert
        result.Should().BeOfType<FilterNode<TestEntity>.Group>();
        var group = (FilterNode<TestEntity>.Group)result;
        group.Operator.Should().Be(LogicalOperator.And);
        group.Conditions.Should().HaveCount(2);
        
        var nameCondition = (FilterNode<TestEntity>.Condition)group.Conditions[0];
        nameCondition.PropertyName.Should().Be("Name");
        nameCondition.Operator.Should().Be(Operator.Equal);
        nameCondition.Value.Should().Be("John");
        
        var ageCondition = (FilterNode<TestEntity>.Condition)group.Conditions[1];
        ageCondition.PropertyName.Should().Be("Age");
        ageCondition.Operator.Should().Be(Operator.GreaterThan);
        ageCondition.Value.Should().Be(25);
    }

    [Fact]
    public void Map_WithToStringEqualComparison_ShouldWork()
    {
        // Arrange
        Expression<Func<TestEntity, bool>> predicate = x => x.Age.ToString() == "25";
        
        // Act
        var result = FilterMapper.Map(predicate);
        
        // Assert
        result.Should().BeOfType<FilterNode<TestEntity>.Condition>();
        var condition = (FilterNode<TestEntity>.Condition)result;
        condition.PropertyName.Should().Be("Age");
        condition.Operator.Should().Be(Operator.Equal);
        condition.Value.Should().Be("25");
    }

    [Fact]
    public void Map_WithToStringNotEqualComparison_ShouldWork()
    {
        // Arrange
        Expression<Func<TestEntity, bool>> predicate = x => x.Age.ToString() != "25";
        
        // Act
        var result = FilterMapper.Map(predicate);
        
        // Assert
        result.Should().BeOfType<FilterNode<TestEntity>.Condition>();
        var condition = (FilterNode<TestEntity>.Condition)result;
        condition.PropertyName.Should().Be("Age");
        condition.Operator.Should().Be(Operator.NotEqual);
        condition.Value.Should().Be("25");
    }

}