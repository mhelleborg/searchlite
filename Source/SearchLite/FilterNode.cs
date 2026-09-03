namespace SearchLite;

public abstract record FilterNode<T>
{
    /// <summary>
    /// A single comparison between a document field and a value.
    /// </summary>
    public sealed record Condition : FilterNode<T>
    {
        /// <summary>
        /// The document field to filter on, encoded as a dot-separated path for nested
        /// fields (e.g. "Author.Name"). Top-level fields are a single segment ("Views").
        /// Inside a <see cref="Quantifier"/> or <see cref="CollectionCount"/> predicate the path is
        /// relative to the collection element, and the empty string denotes the element itself
        /// (used for collections of scalars, e.g. <c>d.Tags.Any(t =&gt; t.StartsWith("x"))</c>).
        /// </summary>
        public required string PropertyName { get; init; }
        public required object Value { get; init; }
        public Operator Operator { get; init; }
        public required Type PropertyType { get; init; }
    }

    /// <summary>
    /// A logical combination of child nodes. An empty <see cref="LogicalOperator.And"/> group is
    /// always true; an empty <see cref="LogicalOperator.Or"/> group is always false.
    /// </summary>
    public sealed record Group : FilterNode<T>
    {
        public required LogicalOperator Operator { get; init; }
        public required List<FilterNode<T>> Conditions { get; init; }
    }

    /// <summary>
    /// Logical negation of an arbitrary child node (e.g. <c>!(a || b)</c>, <c>!d.Items.Any(...)</c>).
    /// Simple conditions are negated by inverting their operator instead, so this node only
    /// wraps groups, quantifiers and counts.
    /// </summary>
    public sealed record Not : FilterNode<T>
    {
        public required FilterNode<T> Operand { get; init; }
    }

    /// <summary>
    /// A quantifier over a collection field: <c>Any</c> (at least one element satisfies the
    /// predicate, or the collection is non-empty when there is no predicate) or <c>All</c>
    /// (every element satisfies the predicate; vacuously true for an empty or missing collection).
    /// </summary>
    public sealed record Quantifier : FilterNode<T>
    {
        /// <summary>
        /// Dot-separated path to the collection field, relative to the enclosing scope.
        /// </summary>
        public required string PropertyName { get; init; }

        /// <summary>
        /// The CLR element type of the collection.
        /// </summary>
        public required Type ElementType { get; init; }

        public required QuantifierKind Kind { get; init; }

        /// <summary>
        /// The element predicate, with field paths relative to the element (see
        /// <see cref="Condition.PropertyName"/>). Null for the parameterless <c>Any()</c>.
        /// </summary>
        public FilterNode<T>? Predicate { get; init; }
    }

    /// <summary>
    /// A comparison against the number of elements in a collection field, optionally counting only
    /// the elements that satisfy a predicate (<c>d.Tags.Count &gt; 2</c>, <c>d.Items.Count(i =&gt; i.Price &gt; 10) &gt;= 1</c>).
    /// A missing or null collection counts as zero.
    /// </summary>
    public sealed record CollectionCount : FilterNode<T>
    {
        /// <summary>
        /// Dot-separated path to the collection field, relative to the enclosing scope.
        /// </summary>
        public required string PropertyName { get; init; }

        /// <summary>
        /// The CLR element type of the collection.
        /// </summary>
        public required Type ElementType { get; init; }

        /// <summary>
        /// Optional element predicate, with field paths relative to the element.
        /// </summary>
        public FilterNode<T>? Predicate { get; init; }

        /// <summary>
        /// One of the six comparison operators.
        /// </summary>
        public required Operator Operator { get; init; }

        public required long Value { get; init; }
    }
}

public enum QuantifierKind
{
    Any,
    All
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
