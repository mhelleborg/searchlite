using System.Reflection;

namespace SearchLite.CosmosDB;

/// <summary>
/// A fragment of a Cosmos NoSQL <c>WHERE</c> clause together with the parameters it references.
/// Parameter names are Cosmos-style (<c>@p0</c>, <c>@p1</c>, ...).
/// </summary>
public record Clause
{
    public required string Sql { get; init; }
    public List<CosmosParameter> Parameters { get; init; } = [];
}

/// <summary>
/// A named parameter bound into a Cosmos <see cref="Microsoft.Azure.Cosmos.QueryDefinition"/>.
/// </summary>
public record CosmosParameter(string Name, object? Value);

/// <summary>
/// Translates <see cref="FilterNode{T}"/> trees into parameterized Cosmos NoSQL <c>WHERE</c> fragments.
///
/// Documents are stored as JSON under the <c>doc</c> property of each Cosmos item (see
/// <see cref="SearchIndex{T}"/>), so a top-level field <c>Views</c> is addressed as
/// <c>c.doc["Views"]</c> and a nested field <c>Author.Name</c> as <c>c.doc["Author"]["Name"]</c>.
/// </summary>
public static class WhereClauseBuilder<T>
{
    private const string Root = "c.doc";

    public static IEnumerable<Clause> BuildClauses(List<FilterNode<T>> filters)
    {
        var globalParamCounter = 0;
        return filters.Select(filter => BuildClause(filter, ref globalParamCounter)).ToList();
    }

    private static Clause BuildClause(FilterNode<T> filter, ref int globalParamCounter)
    {
        var parameters = new List<CosmosParameter>();
        var sql = BuildSql(filter, ref globalParamCounter, parameters);
        return new Clause { Sql = sql, Parameters = parameters };
    }

    private static string BuildSql(FilterNode<T> node, ref int paramCounter, List<CosmosParameter> parameters)
    {
        return node switch
        {
            FilterNode<T>.Condition condition => BuildConditionSql(condition, ref paramCounter, parameters),
            FilterNode<T>.Group group => BuildGroupSql(group, ref paramCounter, parameters),
            _ => throw new ArgumentException($"Unsupported node type: {node.GetType()}")
        };
    }

    private static string BuildGroupSql(FilterNode<T>.Group group, ref int paramCounter,
        List<CosmosParameter> parameters)
    {
        var op = group.Operator switch
        {
            LogicalOperator.And => " AND ",
            LogicalOperator.Or => " OR ",
            _ => throw new ArgumentException($"Unsupported logical operator: {group.Operator}")
        };

        var conditions = new List<string>();
        foreach (var condition in group.Conditions)
        {
            conditions.Add(BuildSql(condition, ref paramCounter, parameters));
        }

        return conditions.Count > 1
            ? $"({string.Join(op, conditions)})"
            : conditions.FirstOrDefault() ?? "true";
    }

    /// <summary>
    /// Builds a property accessor rooted at the stored document.
    /// 1 segment  -> c.doc["seg"]
    /// N segments -> c.doc["seg1"]["seg2"]...
    /// </summary>
    private static string BuildAccessor(string propertyName)
    {
        var segments = FieldPath.Split(propertyName);
        return Root + string.Concat(segments.Select(s => $"[\"{EscapeIdentifier(s)}\"]"));
    }

    private static string EscapeIdentifier(string segment) => segment.Replace("\"", "\\\"");

    private static string BuildConditionSql(FilterNode<T>.Condition condition, ref int paramCounter,
        List<CosmosParameter> parameters)
    {
        if (IsCollectionOperator(condition.Operator))
        {
            return BuildCollectionCondition(condition, ref paramCounter, parameters);
        }

        if (IsNullOperator(condition.Operator))
        {
            return BuildNullCondition(condition.PropertyName, condition.Operator);
        }

        if (IsStringNullOrEmptyOperator(condition.Operator))
        {
            return BuildStringNullOrEmptyCondition(condition.PropertyName, condition.Operator);
        }

        if (IsSetOperator(condition.Operator))
        {
            return BuildSetCondition(condition, ref paramCounter, parameters);
        }

        if (IsStringOperator(condition.Operator))
        {
            return BuildStringCondition(condition, ref paramCounter, parameters);
        }

        var accessor = BuildAccessor(condition.PropertyName);
        var operatorString = GetOperatorString(condition.Operator);
        var paramName = $"@p{paramCounter++}";
        parameters.Add(new CosmosParameter(paramName, NormalizeValue(condition.Value, condition.PropertyType, condition.PropertyName)));
        return $"{accessor} {operatorString} {paramName}";
    }

    /// <summary>
    /// Normalizes a leaf value to the form it is stored as in the JSON document, so equality and
    /// comparisons match the stored representation (enums honoring their serialization format,
    /// Guids as their "D" string).
    /// </summary>
    private static object? NormalizeValue(object? value, Type propertyType, string propertyName)
    {
        if (value == null) return null;

        var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (underlyingType.IsEnum)
        {
            var format = ResolveEnumFormat(propertyName, underlyingType);
            return format == EnumSerializationFormat.String
                ? value.ToString()
                : Convert.ChangeType(value, underlyingType.GetEnumUnderlyingType());
        }

        if (underlyingType == typeof(Guid))
        {
            return ((Guid)value).ToString("D");
        }

        if (underlyingType == typeof(DateTime))
        {
            // System.Text.Json serializes DateTime in round-trip ("O") form.
            return ((DateTime)value).ToString("O");
        }

        if (underlyingType == typeof(DateTimeOffset))
        {
            return ((DateTimeOffset)value).ToString("O");
        }

        return value;
    }

    private static EnumSerializationFormat ResolveEnumFormat(string propertyName, Type underlyingType)
    {
        var prop = typeof(T).GetProperty(propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        return prop != null
            ? EnumSerializationAnalyzer.GetPropertyFormat(prop)
            : EnumSerializationAnalyzer.GetDefaultFormat(underlyingType);
    }

    private static bool IsCollectionOperator(Operator op) =>
        op is Operator.CollectionContains or Operator.CollectionNotContains;

    private static string BuildCollectionCondition(FilterNode<T>.Condition condition, ref int paramCounter,
        List<CosmosParameter> parameters)
    {
        var accessor = BuildAccessor(condition.PropertyName);
        var elementType = GetElementType(condition.PropertyType);
        var paramName = $"@p{paramCounter++}";
        parameters.Add(new CosmosParameter(paramName, NormalizeValue(condition.Value, elementType, condition.PropertyName)));

        var contains = $"ARRAY_CONTAINS({accessor}, {paramName})";
        return condition.Operator == Operator.CollectionContains ? contains : $"NOT ({contains})";
    }

    private static Type GetElementType(Type collectionType)
    {
        if (collectionType.IsArray)
        {
            return collectionType.GetElementType()!;
        }

        if (collectionType.IsGenericType)
        {
            return collectionType.GetGenericArguments()[0];
        }

        var enumerableInterface = collectionType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        if (enumerableInterface != null)
        {
            return enumerableInterface.GetGenericArguments()[0];
        }

        throw new NotSupportedException($"Cannot determine element type for collection type {collectionType}");
    }

    private static string GetOperatorString(Operator op) => op switch
    {
        Operator.Equal => "=",
        Operator.NotEqual => "!=",
        Operator.GreaterThan => ">",
        Operator.GreaterThanOrEqual => ">=",
        Operator.LessThan => "<",
        Operator.LessThanOrEqual => "<=",
        _ => throw new NotSupportedException($"Operator {op} is not supported")
    };

    private static bool IsNullOperator(Operator op) => op is Operator.IsNull or Operator.IsNotNull;

    private static string BuildNullCondition(string propertyName, Operator op)
    {
        var accessor = BuildAccessor(propertyName);
        // In Cosmos a missing field and an explicit JSON null are distinct; treat both as "null"
        // so behavior matches a relational NULL.
        return op switch
        {
            Operator.IsNull => $"(NOT IS_DEFINED({accessor}) OR IS_NULL({accessor}))",
            Operator.IsNotNull => $"(IS_DEFINED({accessor}) AND NOT IS_NULL({accessor}))",
            _ => throw new NotSupportedException($"Null operator {op} is not supported")
        };
    }

    private static bool IsStringNullOrEmptyOperator(Operator op) =>
        op is Operator.IsNullOrEmpty or Operator.IsNotNullOrEmpty or
            Operator.IsNullOrWhiteSpace or Operator.IsNotNullOrWhiteSpace;

    private static string BuildStringNullOrEmptyCondition(string propertyName, Operator op)
    {
        var accessor = BuildAccessor(propertyName);
        var isNullish = $"(NOT IS_DEFINED({accessor}) OR IS_NULL({accessor}))";

        return op switch
        {
            Operator.IsNullOrEmpty => $"({isNullish} OR {accessor} = \"\")",
            Operator.IsNotNullOrEmpty => $"(IS_DEFINED({accessor}) AND NOT IS_NULL({accessor}) AND {accessor} != \"\")",
            Operator.IsNullOrWhiteSpace => $"({isNullish} OR TRIM({accessor}) = \"\")",
            Operator.IsNotNullOrWhiteSpace => $"(IS_DEFINED({accessor}) AND NOT IS_NULL({accessor}) AND TRIM({accessor}) != \"\")",
            _ => throw new NotSupportedException($"String operator {op} is not supported")
        };
    }

    private static bool IsSetOperator(Operator op) => op is Operator.In or Operator.NotIn;

    private static string BuildSetCondition(FilterNode<T>.Condition condition, ref int paramCounter,
        List<CosmosParameter> parameters)
    {
        var accessor = BuildAccessor(condition.PropertyName);
        var elementType = Nullable.GetUnderlyingType(condition.PropertyType) ?? condition.PropertyType;

        if (condition.Value is System.Collections.IEnumerable enumerable and not string)
        {
            var paramNames = new List<string>();
            foreach (var item in enumerable)
            {
                var paramName = $"@p{paramCounter++}";
                parameters.Add(new CosmosParameter(paramName, NormalizeValue(item, elementType, condition.PropertyName)));
                paramNames.Add(paramName);
            }

            if (paramNames.Count == 0)
            {
                return condition.Operator == Operator.In ? "false" : "true";
            }

            var inClause = $"{accessor} IN ({string.Join(", ", paramNames)})";
            return condition.Operator == Operator.In ? inClause : $"NOT ({inClause})";
        }

        var singleParam = $"@p{paramCounter++}";
        parameters.Add(new CosmosParameter(singleParam, NormalizeValue(condition.Value, elementType, condition.PropertyName)));
        return condition.Operator == Operator.In
            ? $"{accessor} = {singleParam}"
            : $"{accessor} != {singleParam}";
    }

    private static bool IsStringOperator(Operator op) =>
        op is Operator.Contains or Operator.NotContains or
            Operator.ContainsIgnoreCase or Operator.NotContainsIgnoreCase or
            Operator.StartsWith or Operator.NotStartsWith or
            Operator.StartsWithIgnoreCase or Operator.NotStartsWithIgnoreCase or
            Operator.EndsWith or Operator.NotEndsWith or
            Operator.EndsWithIgnoreCase or Operator.NotEndsWithIgnoreCase;

    private static string BuildStringCondition(FilterNode<T>.Condition condition, ref int paramCounter,
        List<CosmosParameter> parameters)
    {
        var accessor = BuildAccessor(condition.PropertyName);
        var paramName = $"@p{paramCounter++}";
        parameters.Add(new CosmosParameter(paramName, condition.Value?.ToString()));

        // Cosmos string functions take an optional trailing bool for case-insensitive matching.
        return condition.Operator switch
        {
            Operator.Contains => $"CONTAINS({accessor}, {paramName})",
            Operator.NotContains => $"NOT CONTAINS({accessor}, {paramName})",
            Operator.ContainsIgnoreCase => $"CONTAINS({accessor}, {paramName}, true)",
            Operator.NotContainsIgnoreCase => $"NOT CONTAINS({accessor}, {paramName}, true)",
            Operator.StartsWith => $"STARTSWITH({accessor}, {paramName})",
            Operator.NotStartsWith => $"NOT STARTSWITH({accessor}, {paramName})",
            Operator.StartsWithIgnoreCase => $"STARTSWITH({accessor}, {paramName}, true)",
            Operator.NotStartsWithIgnoreCase => $"NOT STARTSWITH({accessor}, {paramName}, true)",
            Operator.EndsWith => $"ENDSWITH({accessor}, {paramName})",
            Operator.NotEndsWith => $"NOT ENDSWITH({accessor}, {paramName})",
            Operator.EndsWithIgnoreCase => $"ENDSWITH({accessor}, {paramName}, true)",
            Operator.NotEndsWithIgnoreCase => $"NOT ENDSWITH({accessor}, {paramName}, true)",
            _ => throw new NotSupportedException($"String operator {condition.Operator} is not supported")
        };
    }
}
