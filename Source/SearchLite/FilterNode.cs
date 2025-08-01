namespace SearchLite;

public abstract record FilterNode<T>
{
    public sealed record Condition : FilterNode<T>
    {
        public required string PropertyName { get; init; }
        public required object Value { get; init; }
        public Operator Operator { get; init; }
        public required Type PropertyType { get; init; }
    }

    public sealed record Group : FilterNode<T>
    {
        public required LogicalOperator Operator { get; init; }
        public required List<FilterNode<T>> Conditions { get; init; }
    }
}

public enum Operator
{
    Equal,
    NotEqual,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    IsNullOrEmpty,
    IsNotNullOrEmpty,
    IsNullOrWhiteSpace,
    IsNotNullOrWhiteSpace,
    IsNull,
    IsNotNull,
    Contains,
    NotContains,
    ContainsIgnoreCase,
    NotContainsIgnoreCase,
    StartsWith,
    NotStartsWith,
    StartsWithIgnoreCase,
    NotStartsWithIgnoreCase,
    EndsWith,
    NotEndsWith,
    EndsWithIgnoreCase,
    NotEndsWithIgnoreCase,
    In,
    NotIn
}

public enum LogicalOperator
{
    And,
    Or
}