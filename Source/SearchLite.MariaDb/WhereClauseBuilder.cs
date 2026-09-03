using System.Text.Json;
using System.Text.Json.Nodes;
using MySqlConnector;

namespace SearchLite.MariaDb;

public record Clause
{
    public required string Sql { get; init; }
    public List<MySqlParameter> Parameters { get; init; } = [];
}

public static class WhereClauseBuilder<T>
{
    /// <summary>
    /// The JSON value that field paths are resolved against. At the top level this is the
    /// <c>document</c> column of type <typeparamref name="T"/>; inside a collection predicate it is
    /// the <c>elem</c> column of the <c>JSON_TABLE</c> row for the current element.
    /// </summary>
    private sealed record Scope(string Root, Type RootType, int Depth)
    {
        public static readonly Scope Document = new("document", typeof(T), 0);

        /// <summary>
        /// Creates the scope for the elements of a collection at this level, returning the alias
        /// the enclosing <c>JSON_TABLE</c> must be given.
        /// </summary>
        public (string alias, Scope inner) Nested(Type elementType)
        {
            var alias = $"e{Depth}";
            return (alias, new Scope($"{alias}.elem", elementType, Depth + 1));
        }
    }

    public static IEnumerable<Clause> BuildClauses(List<FilterNode<T>> filters)
    {
        var globalParamCounter = 0;
        return filters.Select(filter => BuildClause(filter, ref globalParamCounter));
    }

    private static Clause BuildClause(FilterNode<T> filter, ref int globalParamCounter)
    {
        var parameters = new List<MySqlParameter>();
        var sql = BuildSql(filter, Scope.Document, ref globalParamCounter, parameters);
        return new Clause
        {
            Sql = sql,
            Parameters = parameters
        };
    }

    private static string BuildSql(FilterNode<T> node, Scope scope, ref int paramCounter, List<MySqlParameter> parameters)
    {
        return node switch
        {
            FilterNode<T>.Condition condition => BuildConditionSql(condition, scope, ref paramCounter, parameters),
            FilterNode<T>.Group group => BuildGroupSql(group, scope, ref paramCounter, parameters),
            FilterNode<T>.Not not => $"NOT ({BuildSql(not.Operand, scope, ref paramCounter, parameters)})",
            FilterNode<T>.Quantifier quantifier => BuildQuantifierSql(quantifier, scope, ref paramCounter, parameters),
            FilterNode<T>.CollectionCount count => BuildCollectionCountSql(count, scope, ref paramCounter, parameters),
            _ => throw new ArgumentException($"Unsupported node type: {node.GetType()}")
        };
    }

    private static string BuildGroupSql(FilterNode<T>.Group group, Scope scope, ref int paramCounter,
        List<MySqlParameter> parameters)
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
            conditions.Add(BuildSql(condition, scope, ref paramCounter, parameters));
        }

        if (conditions.Count == 0)
        {
            return group.Operator == LogicalOperator.And ? "TRUE" : "FALSE";
        }

        return conditions.Count > 1
            ? $"({string.Join(op, conditions)})"
            : conditions[0];
    }

    /// <summary>
    /// Builds a JSON path string for the given dotted property path.
    /// 0 segments -> $        (the scope root itself, i.e. a scalar element)
    /// 1 segment  -> $.seg
    /// N segments -> $.seg1.seg2...
    /// </summary>
    private static string BuildJsonPath(string propertyName)
    {
        var segments = FieldPath.Split(propertyName);
        return segments.Length == 0 ? "$" : "$." + string.Join(".", segments);
    }

    /// <summary>
    /// Builds a scalar text accessor for the given dotted property path that yields SQL NULL for a
    /// JSON null OR a missing key, and the unquoted scalar text otherwise (preserving the empty
    /// string). This is deliberately NOT JSON_VALUE — MariaDB's JSON_VALUE collapses an empty
    /// string to NULL and nulls out objects — and NOT a bare JSON_UNQUOTE(JSON_EXTRACT(...)), which
    /// returns the literal text 'null' for a present-but-null field. Both break IS NULL /
    /// IsNullOrEmpty / ordering / nested null-guard semantics. The CASE on JSON_TYPE handles every
    /// case: missing -> JSON_EXTRACT is SQL NULL; JSON null -> JSON_TYPE 'NULL'; anything else
    /// (incl. "") -> the unquoted value, and for an object the (non-null) object text so an
    /// IS NOT NULL guard on a nested object still holds.
    /// </summary>
    private static string BuildTextAccessor(Scope scope, string propertyName)
    {
        var extract = $"JSON_EXTRACT({scope.Root}, '{BuildJsonPath(propertyName)}')";
        return $"(CASE WHEN JSON_TYPE({extract}) = 'NULL' THEN NULL ELSE JSON_UNQUOTE({extract}) END)";
    }

    /// <summary>
    /// A <c>JSON_TABLE</c> source over the array at the given path, exposing each element as a JSON
    /// column named <c>elem</c>. A missing, null or non-array value yields no rows.
    /// </summary>
    private static string ArraySource(Scope scope, string propertyName, string alias) =>
        $"JSON_TABLE({scope.Root}, '{BuildJsonPath(propertyName)}[*]' COLUMNS (elem JSON PATH '$')) AS {alias}";

    private static string BuildQuantifierSql(FilterNode<T>.Quantifier quantifier, Scope scope, ref int paramCounter,
        List<MySqlParameter> parameters)
    {
        var (alias, inner) = scope.Nested(quantifier.ElementType);

        // Any(...) whose predicate is a conjunction of equalities compiles to a single JSON
        // containment check against the array, e.g. Items.Any(i => i.Sku == "x" && i.Qty == 2)
        // => JSON_CONTAINS(document, '{"Sku":"x","Qty":2}', '$.Items'), which holds when some
        // element contains the candidate (mirrors Postgres @> containment).
        if (quantifier is { Kind: QuantifierKind.Any, Predicate: not null }
            && TryBuildContainmentNode(quantifier.Predicate, inner, out var elementJson))
        {
            var containmentParam = $"@p{paramCounter++}";
            parameters.Add(new MySqlParameter(containmentParam, elementJson?.ToJsonString() ?? "null"));
            return $"JSON_CONTAINS({scope.Root}, {containmentParam}, '{BuildJsonPath(quantifier.PropertyName)}')";
        }

        var source = ArraySource(scope, quantifier.PropertyName, alias);

        if (quantifier.Predicate == null)
        {
            return quantifier.Kind == QuantifierKind.Any
                ? $"EXISTS (SELECT 1 FROM {source})"
                : throw new NotSupportedException("All requires a predicate");
        }

        var predicate = BuildSql(quantifier.Predicate, inner, ref paramCounter, parameters);

        return quantifier.Kind == QuantifierKind.Any
            ? $"EXISTS (SELECT 1 FROM {source} WHERE {predicate})"
            : $"NOT EXISTS (SELECT 1 FROM {source} WHERE NOT ({predicate}))";
    }

    private static string BuildCollectionCountSql(FilterNode<T>.CollectionCount count, Scope scope, ref int paramCounter,
        List<MySqlParameter> parameters)
    {
        string countExpression;
        if (count.Predicate == null)
        {
            // JSON_LENGTH of a scalar (including a JSON null) is 1, so guard on the type.
            var path = BuildJsonPath(count.PropertyName);
            countExpression = $"(CASE WHEN JSON_TYPE(JSON_EXTRACT({scope.Root}, '{path}')) = 'ARRAY' THEN JSON_LENGTH({scope.Root}, '{path}') ELSE 0 END)";
        }
        else
        {
            var (alias, inner) = scope.Nested(count.ElementType);
            var predicate = BuildSql(count.Predicate, inner, ref paramCounter, parameters);
            countExpression = $"(SELECT COUNT(*) FROM {ArraySource(scope, count.PropertyName, alias)} WHERE {predicate})";
        }

        var paramName = $"@p{paramCounter++}";
        parameters.Add(new MySqlParameter(paramName, count.Value));
        return $"{countExpression} {GetOperatorString(count.Operator)} {paramName}";
    }

    /// <summary>
    /// Builds an ORDER BY accessor for a dotted property path. Uses the scalar accessor (so a JSON
    /// null / missing key sorts as SQL NULL rather than as a JSON-null value), and casts numeric
    /// fields so they sort numerically instead of lexically ("100" before "20"). Other types
    /// (string, DateTime as ISO text, Guid, bool, enum) order on their text form.
    /// </summary>
    public static string BuildOrderAccessor(string propertyName)
    {
        var accessor = BuildTextAccessor(Scope.Document, propertyName);
        var leaf = FieldPath.ResolveProperty(typeof(T), propertyName)?.PropertyType;
        if (leaf == null)
        {
            return accessor;
        }

        var underlying = Nullable.GetUnderlyingType(leaf) ?? leaf;
        string? cast = null;
        if (underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(short)
            || underlying == typeof(byte) || underlying == typeof(char))
        {
            cast = "SIGNED";
        }
        else if (underlying == typeof(double) || underlying == typeof(float) || underlying == typeof(decimal))
        {
            cast = "DECIMAL(65,30)";
        }

        return cast == null ? accessor : $"CAST({accessor} AS {cast})";
    }

    private static string BuildConditionSql(FilterNode<T>.Condition condition, Scope scope, ref int paramCounter,
        List<MySqlParameter> parameters)
    {
        if (IsCollectionOperator(condition.Operator))
        {
            return BuildCollectionCondition(condition, scope, ref paramCounter, parameters);
        }

        if (IsNullOperator(condition.Operator))
        {
            return BuildNullCondition(scope, condition.PropertyName, condition.Operator);
        }

        if (IsStringNullOrEmptyOperator(condition.Operator))
        {
            return BuildStringNullOrEmptyCondition(scope, condition.PropertyName, condition.Operator);
        }

        if (IsSetOperator(condition.Operator))
        {
            return BuildSetCondition(condition, scope, ref paramCounter, parameters);
        }

        if (IsStringOperator(condition.Operator))
        {
            return BuildStringCondition(condition, scope, ref paramCounter, parameters);
        }

        var underlyingType = Nullable.GetUnderlyingType(condition.PropertyType) ?? condition.PropertyType;

        // Equal/NotEqual on containment-eligible types use JSON_CONTAINS so the value is matched
        // against the stored JSON representation (mirrors Postgres @> containment).
        if ((condition.Operator is Operator.Equal or Operator.NotEqual)
            && IsContainmentEligible(underlyingType))
        {
            return BuildContainmentEqualityCondition(condition, underlyingType, scope, ref paramCounter, parameters);
        }

        var fieldExpression = BuildCastAccessor(scope, condition.PropertyName, condition.PropertyType);
        var operatorString = GetOperatorString(condition.Operator);
        var paramName = $"@p{paramCounter++}";

        object? paramValue = condition.Value;

        if (underlyingType.IsEnum)
        {
            var format = ResolveEnumFormat(scope, condition.PropertyName, underlyingType);

            paramValue = format == EnumSerializationFormat.String
                ? condition.Value?.ToString()
                : condition.Value != null
                    ? Convert.ChangeType(condition.Value, underlyingType.GetEnumUnderlyingType())
                    : null;
        }
        else if (underlyingType == typeof(Guid))
        {
            paramValue = condition.Value?.ToString();
        }

        parameters.Add(new MySqlParameter(paramName, paramValue ?? DBNull.Value));

        return $"{fieldExpression} {operatorString} {paramName}";
    }

    /// <summary>
    /// Builds a typed accessor that casts the extracted JSON scalar to the appropriate SQL type
    /// for range/scalar comparisons.
    /// </summary>
    private static string BuildCastAccessor(Scope scope, string propertyName, Type propertyType)
    {
        var castType = GetMariaDbCastType(propertyType, scope, propertyName);
        var accessor = BuildTextAccessor(scope, propertyName);
        return castType == null
            ? accessor
            : $"CAST({accessor} AS {castType})";
    }

    /// <summary>
    /// Underlying types for which JSON containment reliably matches the stored representation.
    /// </summary>
    private static bool IsContainmentEligible(Type underlyingType)
    {
        if (underlyingType.IsEnum) return true;

        return underlyingType == typeof(string)
               || underlyingType == typeof(bool)
               || underlyingType == typeof(Guid)
               || underlyingType == typeof(int)
               || underlyingType == typeof(long)
               || underlyingType == typeof(short)
               || underlyingType == typeof(byte);
    }

    /// <summary>
    /// Resolves the serialization format of an enum field by walking the path from the scope's
    /// root type, so a property-level converter is honored for nested and element fields too.
    /// Falls back to the enum type's own default when the path cannot be resolved (e.g. the bare
    /// element of an enum collection).
    /// </summary>
    private static EnumSerializationFormat ResolveEnumFormat(Scope scope, string propertyName, Type underlyingType)
    {
        if (!underlyingType.IsEnum)
        {
            return default;
        }

        var prop = FieldPath.ResolveProperty(scope.RootType, propertyName);
        return prop != null && (Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType).IsEnum
            ? EnumSerializationAnalyzer.GetPropertyFormat(prop)
            : EnumSerializationAnalyzer.GetDefaultFormat(underlyingType);
    }

    /// <summary>
    /// Produces the JSON node representation of a scalar leaf value, honoring enum format.
    /// </summary>
    private static JsonNode? BuildLeafJson(object? value, Type underlyingType, EnumSerializationFormat enumFormat)
    {
        if (value == null) return null;

        if (underlyingType.IsEnum)
        {
            if (enumFormat == EnumSerializationFormat.String)
            {
                return JsonValue.Create(value.ToString());
            }

            var numeric = Convert.ChangeType(value, underlyingType.GetEnumUnderlyingType());
            return JsonSerializer.SerializeToNode(numeric, numeric!.GetType());
        }

        if (underlyingType == typeof(Guid))
        {
            return JsonValue.Create(((Guid)value).ToString("D"));
        }

        if (underlyingType == typeof(string))
        {
            return JsonValue.Create((string)value);
        }

        if (underlyingType == typeof(bool))
        {
            return JsonValue.Create((bool)value);
        }

        // Integer types: serialize as JSON numbers.
        return JsonSerializer.SerializeToNode(value, underlyingType);
    }

    /// <summary>
    /// Wraps a leaf JSON node inside nested objects following the dotted path.
    /// e.g. path "Author.Name", leaf "Alice" -> {"Author":{"Name":"Alice"}}
    /// An empty path yields the leaf itself.
    /// </summary>
    private static JsonNode? BuildPathNode(string propertyName, JsonNode? leaf)
    {
        var segments = FieldPath.Split(propertyName);
        JsonNode? node = leaf;
        for (var i = segments.Length - 1; i >= 0; i--)
        {
            var obj = new JsonObject { [segments[i]] = node };
            node = obj;
        }

        return node;
    }

    private static string BuildContainmentEqualityCondition(FilterNode<T>.Condition condition, Type underlyingType,
        Scope scope, ref int paramCounter, List<MySqlParameter> parameters)
    {
        var leaf = BuildLeafJson(condition.Value, underlyingType, ResolveEnumFormat(scope, condition.PropertyName, underlyingType));
        var json = leaf?.ToJsonString() ?? "null";
        var path = BuildJsonPath(condition.PropertyName);

        var paramName = $"@p{paramCounter++}";
        parameters.Add(new MySqlParameter(paramName, json));

        // JSON_CONTAINS(document, candidate, path) returns 1 when the value at `path` contains
        // the candidate value (or, for scalars, equals it).
        return condition.Operator == Operator.Equal
            ? $"JSON_CONTAINS({scope.Root}, {paramName}, '{path}')"
            : $"NOT JSON_CONTAINS({scope.Root}, {paramName}, '{path}')";
    }

    private static bool IsCollectionOperator(Operator op)
    {
        return op is Operator.CollectionContains or Operator.CollectionNotContains;
    }

    private static string BuildCollectionCondition(FilterNode<T>.Condition condition, Scope scope, ref int paramCounter,
        List<MySqlParameter> parameters)
    {
        var leaf = BuildCollectionElementJson(condition);
        var json = leaf?.ToJsonString() ?? "null";
        var path = BuildJsonPath(condition.PropertyName);

        var paramName = $"@p{paramCounter++}";
        parameters.Add(new MySqlParameter(paramName, json));

        // JSON_CONTAINS against an array path matches when the array contains the candidate element.
        return condition.Operator == Operator.CollectionContains
            ? $"JSON_CONTAINS({scope.Root}, {paramName}, '{path}')"
            : $"NOT JSON_CONTAINS({scope.Root}, {paramName}, '{path}')";
    }

    private static JsonNode? BuildCollectionElementJson(FilterNode<T>.Condition condition)
    {
        var elementType = CollectionTypes.GetElementType(condition.PropertyType);
        var underlyingElementType = Nullable.GetUnderlyingType(elementType) ?? elementType;

        // A converter attribute on the collection property does not apply to its elements, so
        // the element format is the enum type's own default.
        var enumFormat = underlyingElementType.IsEnum
            ? EnumSerializationAnalyzer.GetDefaultFormat(underlyingElementType)
            : default;
        return BuildLeafJson(condition.Value, underlyingElementType, enumFormat);
    }

    /// <summary>
    /// Attempts to express a predicate as a JSON value that a matching element must contain
    /// (JSON_CONTAINS semantics). Supported: equality on containment-eligible types, collection
    /// membership, nested <c>Any</c> with a containable predicate, and AND groups of those
    /// (deep-merged into one object). Anything else — negations, ranges, OR groups, string
    /// operators — returns false so the caller falls back to an EXISTS subquery.
    /// </summary>
    private static bool TryBuildContainmentNode(FilterNode<T> node, Scope scope, out JsonNode? json)
    {
        json = null;
        switch (node)
        {
            case FilterNode<T>.Condition { Operator: Operator.Equal } condition:
            {
                var underlyingType = Nullable.GetUnderlyingType(condition.PropertyType) ?? condition.PropertyType;
                if (!IsContainmentEligible(underlyingType))
                {
                    return false;
                }

                var leaf = BuildLeafJson(condition.Value, underlyingType, ResolveEnumFormat(scope, condition.PropertyName, underlyingType));
                json = BuildPathNode(condition.PropertyName, leaf);
                return true;
            }
            case FilterNode<T>.Condition { Operator: Operator.CollectionContains } condition:
                json = BuildPathNode(condition.PropertyName, new JsonArray { BuildCollectionElementJson(condition) });
                return true;
            case FilterNode<T>.Quantifier { Kind: QuantifierKind.Any, Predicate: not null } quantifier:
            {
                var (_, inner) = scope.Nested(quantifier.ElementType);
                if (!TryBuildContainmentNode(quantifier.Predicate, inner, out var element))
                {
                    return false;
                }

                json = BuildPathNode(quantifier.PropertyName, new JsonArray { element });
                return true;
            }
            case FilterNode<T>.Group { Operator: LogicalOperator.And, Conditions.Count: > 0 } group:
            {
                JsonNode? merged = null;
                foreach (var child in group.Conditions)
                {
                    if (!TryBuildContainmentNode(child, scope, out var part))
                    {
                        return false;
                    }

                    if (merged == null)
                    {
                        merged = part;
                        continue;
                    }

                    if (!TryMerge(merged, part, out merged))
                    {
                        return false;
                    }
                }

                json = merged;
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>
    /// Deep-merges two containment candidates: objects merge key by key, arrays concatenate
    /// (every candidate element must be matched), and scalars must be identical. A conflict
    /// (the same key with different scalar values) makes the merge fail.
    /// </summary>
    private static bool TryMerge(JsonNode? left, JsonNode? right, out JsonNode? merged)
    {
        merged = null;
        switch (left, right)
        {
            case (JsonObject leftObject, JsonObject rightObject):
            {
                var result = new JsonObject();
                foreach (var (key, value) in leftObject)
                {
                    result[key] = value?.DeepClone();
                }

                foreach (var (key, value) in rightObject)
                {
                    if (!result.ContainsKey(key))
                    {
                        result[key] = value?.DeepClone();
                        continue;
                    }

                    if (!TryMerge(result[key], value, out var child))
                    {
                        return false;
                    }

                    result[key] = child;
                }

                merged = result;
                return true;
            }
            case (JsonArray leftArray, JsonArray rightArray):
            {
                var result = new JsonArray();
                foreach (var item in leftArray.Concat(rightArray))
                {
                    result.Add(item?.DeepClone());
                }

                merged = result;
                return true;
            }
            case (null, null):
                return true;
            case (JsonValue, JsonValue) when JsonNode.DeepEquals(left, right):
                merged = left.DeepClone();
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Returns the MariaDB CAST target type for the given .NET type, or null when no cast is needed
    /// (the value is compared as the unquoted text scalar).
    /// </summary>
    private static string? GetMariaDbCastType(Type type, Scope scope, string propertyName)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        if (underlyingType.IsEnum)
        {
            var format = ResolveEnumFormat(scope, propertyName, underlyingType);
            return format == EnumSerializationFormat.String ? "CHAR" : "SIGNED";
        }

        return underlyingType switch
        {
            { } t when t == typeof(int) => "SIGNED",
            { } t when t == typeof(string) => "CHAR",
            { } t when t == typeof(bool) => "SIGNED",
            { } t when t == typeof(double) => "DECIMAL(65,30)",
            { } t when t == typeof(decimal) => "DECIMAL(65,30)",
            { } t when t == typeof(DateTime) => "DATETIME(6)",
            { } t when t == typeof(DateTimeOffset) => "DATETIME(6)",
            { } t when t == typeof(Guid) => "CHAR",
            { } t when t == typeof(byte) => "SIGNED",
            { } t when t == typeof(short) => "SIGNED",
            { } t when t == typeof(long) => "SIGNED",
            { } t when t == typeof(float) => "DECIMAL(65,30)",
            { } t when t == typeof(char) => "SIGNED",
            _ => throw new NotSupportedException($"Type {underlyingType} is not supported")
        };
    }

    private static string GetOperatorString(Operator op)
    {
        return op switch
        {
            Operator.Equal => "=",
            Operator.NotEqual => "!=",
            Operator.GreaterThan => ">",
            Operator.GreaterThanOrEqual => ">=",
            Operator.LessThan => "<",
            Operator.LessThanOrEqual => "<=",
            _ => throw new NotSupportedException($"Operator {op} is not supported")
        };
    }

    private static bool IsStringNullOrEmptyOperator(Operator op)
    {
        return op is Operator.IsNullOrEmpty or Operator.IsNotNullOrEmpty or
                     Operator.IsNullOrWhiteSpace or Operator.IsNotNullOrWhiteSpace;
    }

    private static bool IsNullOperator(Operator op)
    {
        return op is Operator.IsNull or Operator.IsNotNull;
    }

    private static string BuildNullCondition(Scope scope, string propertyName, Operator op)
    {
        var fieldExpression = BuildTextAccessor(scope, propertyName);

        return op switch
        {
            Operator.IsNull => $"{fieldExpression} IS NULL",
            Operator.IsNotNull => $"{fieldExpression} IS NOT NULL",
            _ => throw new NotSupportedException($"Null operator {op} is not supported")
        };
    }

    private static string BuildStringNullOrEmptyCondition(Scope scope, string propertyName, Operator op)
    {
        var fieldExpression = BuildTextAccessor(scope, propertyName);

        // Emptiness is tested with CHAR_LENGTH, not `= ''`: MySQL's PAD SPACE collation treats a
        // string of only spaces ("   ") as equal to '', which would make IsNullOrEmpty wrongly match
        // whitespace. CHAR_LENGTH counts the actual characters.
        var trimmed = $"TRIM(REPLACE(REPLACE(REPLACE({fieldExpression}, CHAR(9), ' '), CHAR(10), ' '), CHAR(13), ' '))";
        return op switch
        {
            Operator.IsNullOrEmpty => $"({fieldExpression} IS NULL OR CHAR_LENGTH({fieldExpression}) = 0)",
            Operator.IsNotNullOrEmpty => $"({fieldExpression} IS NOT NULL AND CHAR_LENGTH({fieldExpression}) > 0)",
            Operator.IsNullOrWhiteSpace => $"({fieldExpression} IS NULL OR CHAR_LENGTH({trimmed}) = 0)",
            Operator.IsNotNullOrWhiteSpace => $"({fieldExpression} IS NOT NULL AND CHAR_LENGTH({trimmed}) > 0)",
            _ => throw new NotSupportedException($"String operator {op} is not supported")
        };
    }

    private static bool IsSetOperator(Operator op)
    {
        return op is Operator.In or Operator.NotIn;
    }

    private static bool IsStringOperator(Operator op)
    {
        return op is Operator.Contains or Operator.NotContains or
                     Operator.ContainsIgnoreCase or Operator.NotContainsIgnoreCase or
                     Operator.StartsWith or Operator.NotStartsWith or
                     Operator.StartsWithIgnoreCase or Operator.NotStartsWithIgnoreCase or
                     Operator.EndsWith or Operator.NotEndsWith or
                     Operator.EndsWithIgnoreCase or Operator.NotEndsWithIgnoreCase;
    }

    private static string BuildSetCondition(FilterNode<T>.Condition condition, Scope scope, ref int paramCounter, List<MySqlParameter> parameters)
    {
        var fieldExpression = BuildCastAccessor(scope, condition.PropertyName, condition.PropertyType);

        return condition.Operator switch
        {
            Operator.In => BuildInCondition(fieldExpression, condition.Value, scope, ref paramCounter, parameters, condition.PropertyType, condition.PropertyName),
            Operator.NotIn => $"NOT ({BuildInCondition(fieldExpression, condition.Value, scope, ref paramCounter, parameters, condition.PropertyType, condition.PropertyName)})",
            _ => throw new NotSupportedException($"Set operator {condition.Operator} is not supported")
        };
    }

    private static string BuildStringCondition(FilterNode<T>.Condition condition, Scope scope, ref int paramCounter, List<MySqlParameter> parameters)
    {
        var fieldExpression = BuildTextAccessor(scope, condition.PropertyName);

        return condition.Operator switch
        {
            Operator.Contains => BuildContainsCondition(fieldExpression, condition.Value, ref paramCounter, parameters),
            Operator.NotContains => $"NOT ({BuildContainsCondition(fieldExpression, condition.Value, ref paramCounter, parameters)})",
            Operator.ContainsIgnoreCase => BuildContainsIgnoreCaseCondition(fieldExpression, condition.Value, ref paramCounter, parameters),
            Operator.NotContainsIgnoreCase => $"NOT ({BuildContainsIgnoreCaseCondition(fieldExpression, condition.Value, ref paramCounter, parameters)})",
            Operator.StartsWith => BuildStartsWithCondition(fieldExpression, condition.Value, ref paramCounter, parameters),
            Operator.NotStartsWith => $"NOT ({BuildStartsWithCondition(fieldExpression, condition.Value, ref paramCounter, parameters)})",
            Operator.StartsWithIgnoreCase => BuildStartsWithIgnoreCaseCondition(fieldExpression, condition.Value, ref paramCounter, parameters),
            Operator.NotStartsWithIgnoreCase => $"NOT ({BuildStartsWithIgnoreCaseCondition(fieldExpression, condition.Value, ref paramCounter, parameters)})",
            Operator.EndsWith => BuildEndsWithCondition(fieldExpression, condition.Value, ref paramCounter, parameters),
            Operator.NotEndsWith => $"NOT ({BuildEndsWithCondition(fieldExpression, condition.Value, ref paramCounter, parameters)})",
            Operator.EndsWithIgnoreCase => BuildEndsWithIgnoreCaseCondition(fieldExpression, condition.Value, ref paramCounter, parameters),
            Operator.NotEndsWithIgnoreCase => $"NOT ({BuildEndsWithIgnoreCaseCondition(fieldExpression, condition.Value, ref paramCounter, parameters)})",
            _ => throw new NotSupportedException($"String operator {condition.Operator} is not supported")
        };
    }

    private static string BuildContainsCondition(string fieldExpression, object value, ref int paramCounter, List<MySqlParameter> parameters)
    {
        var paramName = $"@p{paramCounter++}";
        parameters.Add(new MySqlParameter(paramName, $"%{value}%"));
        return $"{fieldExpression} LIKE {paramName}";
    }

    private static string BuildInCondition(string fieldExpression, object? value, Scope scope, ref int paramCounter, List<MySqlParameter> parameters, Type? propertyType = null, string? propertyName = null)
    {
        if (value is System.Collections.IEnumerable enumerable and not string)
        {
            var values = new List<object?>();
            foreach (var item in enumerable)
            {
                values.Add(item);
            }

            if (values.Count == 0)
            {
                return "FALSE";
            }

            var paramNames = new List<string>();
            foreach (var item in values)
            {
                var paramName = $"@p{paramCounter++}";
                object? paramValue = item;

                var underlyingType = propertyType != null ? Nullable.GetUnderlyingType(propertyType) ?? propertyType : null;

                if (underlyingType != null && underlyingType.IsEnum)
                {
                    var format = ResolveEnumFormat(scope, propertyName ?? string.Empty, underlyingType);

                    if (format == EnumSerializationFormat.String)
                    {
                        paramValue = item?.ToString();
                    }
                    else if (item != null)
                    {
                        paramValue = Convert.ChangeType(item, underlyingType.GetEnumUnderlyingType());
                    }
                }
                else if (underlyingType == typeof(Guid))
                {
                    paramValue = item?.ToString();
                }

                parameters.Add(new MySqlParameter(paramName, paramValue ?? DBNull.Value));
                paramNames.Add(paramName);
            }

            return $"{fieldExpression} IN ({string.Join(", ", paramNames)})";
        }

        var singleParamName = $"@p{paramCounter++}";
        object? singleParamValue = value;
        if (propertyType == typeof(Guid) || propertyType == typeof(Guid?))
        {
            singleParamValue = value?.ToString() ?? "";
        }

        parameters.Add(new MySqlParameter(singleParamName, singleParamValue ?? DBNull.Value));
        return $"{fieldExpression} = {singleParamName}";
    }

    private static string BuildContainsIgnoreCaseCondition(string fieldExpression, object value, ref int paramCounter, List<MySqlParameter> parameters)
    {
        var paramName = $"@p{paramCounter++}";
        parameters.Add(new MySqlParameter(paramName, $"%{value}%"));
        return $"LOWER({fieldExpression}) LIKE LOWER({paramName})";
    }

    private static string BuildStartsWithCondition(string fieldExpression, object value, ref int paramCounter, List<MySqlParameter> parameters)
    {
        var paramName = $"@p{paramCounter++}";
        parameters.Add(new MySqlParameter(paramName, $"{value}%"));
        return $"{fieldExpression} LIKE {paramName}";
    }

    private static string BuildStartsWithIgnoreCaseCondition(string fieldExpression, object value, ref int paramCounter, List<MySqlParameter> parameters)
    {
        var paramName = $"@p{paramCounter++}";
        parameters.Add(new MySqlParameter(paramName, $"{value}%"));
        return $"LOWER({fieldExpression}) LIKE LOWER({paramName})";
    }

    private static string BuildEndsWithCondition(string fieldExpression, object value, ref int paramCounter, List<MySqlParameter> parameters)
    {
        var paramName = $"@p{paramCounter++}";
        parameters.Add(new MySqlParameter(paramName, $"%{value}"));
        return $"{fieldExpression} LIKE {paramName}";
    }

    private static string BuildEndsWithIgnoreCaseCondition(string fieldExpression, object value, ref int paramCounter, List<MySqlParameter> parameters)
    {
        var paramName = $"@p{paramCounter++}";
        parameters.Add(new MySqlParameter(paramName, $"%{value}"));
        return $"LOWER({fieldExpression}) LIKE LOWER({paramName})";
    }
}
