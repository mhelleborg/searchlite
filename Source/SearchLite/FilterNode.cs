namespace SearchLite;

public abstract record FilterNode<T>
{
    public sealed record Condition : FilterNode<T>
    {
        /// <summary>
        /// The document field to filter on, encoded as a dot-separated path for nested
        /// fields (e.g. "Author.Name"). Top-level fields are a single segment ("Views").
        /// </summary>
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
    NotIn,
    CollectionContains,
    CollectionNotContains
}

public enum LogicalOperator
{
    And,
    Or
}