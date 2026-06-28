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
    public static IEnumerable<Clause> BuildClauses(List<FilterNode<T>> filters)
    {
        var globalParamCounter = 0;
        return filters.Select(filter => BuildClause(filter, ref globalParamCounter));
    }

    private static Clause BuildClause(FilterNode<T> filter, ref int globalParamCounter)
    {
        var parameters = new List<MySqlParameter>();
        var sql = BuildSql(filter, ref globalParamCounter, parameters);
        return new Clause
        {
            Sql = sql,
            Parameters = parameters
        };
    }

    private static string BuildSql(FilterNode<T> node, ref int paramCounter, List<MySqlParameter> parameters)
    {
        return node switch
        {
            FilterNode<T>.Condition condition => BuildConditionSql(condition, ref paramCounter, parameters),
            FilterNode<T>.Group group => BuildGroupSql(group, ref paramCounter, parameters),
            _ => throw new ArgumentException($"Unsupported node type: {node.GetType()}")
        };
    }

    private static string BuildGroupSql(FilterNode<T>.Group group, ref int paramCounter,
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
            conditions.Add(BuildSql(condition, ref paramCounter, parameters));
        }

        return conditions.Count > 1
            ? $"({string.Join(op, conditions)})"
            : conditions.FirstOrDefault() ?? "TRUE";
    }

    /// <summary>
    /// Builds a JSON path string for the given dotted property path.
    /// 1 segment  -> $.seg
    /// N segments -> $.seg1.seg2...
    /// </summary>
    private static string BuildJsonPath(string propertyName)
    {
        var segments = FieldPath.Split(propertyName);
        return "$." + string.Join(".", segments);
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
    private static string BuildTextAccessor(string propertyName)
    {
        var extract = $"JSON_EXTRACT(document, '{BuildJsonPath(propertyName)}')";
        return $"(CASE WHEN JSON_TYPE({extract}) = 'NULL' THEN NULL ELSE JSON_UNQUOTE({extract}) END)";
    }

    /// <summary>
    /// Builds an ORDER BY accessor for a dotted property path. Uses the scalar accessor (so a JSON
    /// null / missing key sorts as SQL NULL rather than as a JSON-null value), and casts numeric
    /// fields so they sort numerically instead of lexically ("100" before "20"). Other types
    /// (string, DateTime as ISO text, Guid, bool, enum) order on their text form.
    /// </summary>
    public static string BuildOrderAccessor(string propertyName)
    {
        var accessor = BuildTextAccessor(propertyName);
        var leaf = ResolveLeafType(propertyName);
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

    private static Type? ResolveLeafType(string propertyName)
    {
        Type? current = typeof(T);
        foreach (var segment in FieldPath.Split(propertyName))
        {
            var prop = current?.GetProperty(segment,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase);
            if (prop == null)
            {
                return null;
            }

            current = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
        }

        return current;
    }

    private static string BuildConditionSql(FilterNode<T>.Condition condition, ref int paramCounter,
        List<MySqlParameter> parameters)
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

        var underlyingType = Nullable.GetUnderlyingType(condition.PropertyType) ?? condition.PropertyType;

        // Equal/NotEqual on containment-eligible types use JSON_CONTAINS so the value is matched
        // against the stored JSON representation (mirrors Postgres @> containment).
        if ((condition.Operator is Operator.Equal or Operator.NotEqual)
            && IsContainmentEligible(underlyingType))
        {
            return BuildContainmentEqualityCondition(condition, underlyingType, ref paramCounter, parameters);
        }

        var fieldExpression = BuildCastAccessor(condition.PropertyName, condition.PropertyType);
        var operatorString = GetOperatorString(condition.Operator);
        var paramName = $"@p{paramCounter++}";

        object? paramValue = condition.Value;

        if (underlyingType.IsEnum)
        {
            var format = ResolveEnumFormat(condition.PropertyName, underlyingType);

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
    private static string BuildCastAccessor(string propertyName, Type propertyType)
    {
        var castType = GetMariaDbCastType(propertyType, propertyName);
        var accessor = BuildTextAccessor(propertyName);
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

    private static EnumSerializationFormat ResolveEnumFormat(string propertyName, Type underlyingType)
    {
        var prop = typeof(T).GetProperty(propertyName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase);
        return prop != null
            ? EnumSerializationAnalyzer.GetPropertyFormat(prop)
            : EnumSerializationAnalyzer.GetDefaultFormat(underlyingType);
    }

    /// <summary>
    /// Produces the JSON node representation of a scalar leaf value, honoring enum format.
    /// </summary>
    private static JsonNode? BuildLeafJson(object? value, Type underlyingType, string propertyName)
    {
        if (value == null) return null;

        if (underlyingType.IsEnum)
        {
            var format = ResolveEnumFormat(propertyName, underlyingType);
            if (format == EnumSerializationFormat.String)
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

    private static string BuildContainmentEqualityCondition(FilterNode<T>.Condition condition, Type underlyingType,
        ref int paramCounter, List<MySqlParameter> parameters)
    {
        var leaf = BuildLeafJson(condition.Value, underlyingType, condition.PropertyName);
        var json = leaf?.ToJsonString() ?? "null";
        var path = BuildJsonPath(condition.PropertyName);

        var paramName = $"@p{paramCounter++}";
        parameters.Add(new MySqlParameter(paramName, json));

        // JSON_CONTAINS(document, candidate, path) returns 1 when the value at `path` contains
        // the candidate value (or, for scalars, equals it).
        return condition.Operator == Operator.Equal
            ? $"JSON_CONTAINS(document, {paramName}, '{path}')"
            : $"NOT JSON_CONTAINS(document, {paramName}, '{path}')";
    }

    private static bool IsCollectionOperator(Operator op)
    {
        return op is Operator.CollectionContains or Operator.CollectionNotContains;
    }

    private static string BuildCollectionCondition(FilterNode<T>.Condition condition, ref int paramCounter,
        List<MySqlParameter> parameters)
    {
        var elementType = GetElementType(condition.PropertyType);
        var underlyingElementType = Nullable.GetUnderlyingType(elementType) ?? elementType;

        var leaf = BuildLeafJson(condition.Value, underlyingElementType, condition.PropertyName);
        var json = leaf?.ToJsonString() ?? "null";
        var path = BuildJsonPath(condition.PropertyName);

        var paramName = $"@p{paramCounter++}";
        parameters.Add(new MySqlParameter(paramName, json));

        // JSON_CONTAINS against an array path matches when the array contains the candidate element.
        return condition.Operator == Operator.CollectionContains
            ? $"JSON_CONTAINS(document, {paramName}, '{path}')"
            : $"NOT JSON_CONTAINS(document, {paramName}, '{path}')";
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

    /// <summary>
    /// Returns the MariaDB CAST target type for the given .NET type, or null when no cast is needed
    /// (the value is compared as the unquoted text scalar).
    /// </summary>
    private static string? GetMariaDbCastType(Type type, string propertyName)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        if (underlyingType.IsEnum)
        {
            var format = ResolveEnumFormat(propertyName, underlyingType);
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

    private static string BuildNullCondition(string propertyName, Operator op)
    {
        var fieldExpression = BuildTextAccessor(propertyName);

        return op switch
        {
            Operator.IsNull => $"{fieldExpression} IS NULL",
            Operator.IsNotNull => $"{fieldExpression} IS NOT NULL",
            _ => throw new NotSupportedException($"Null operator {op} is not supported")
        };
    }

    private static string BuildStringNullOrEmptyCondition(string propertyName, Operator op)
    {
        var fieldExpression = BuildTextAccessor(propertyName);

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

    private static string BuildSetCondition(FilterNode<T>.Condition condition, ref int paramCounter, List<MySqlParameter> parameters)
    {
        var fieldExpression = BuildCastAccessor(condition.PropertyName, condition.PropertyType);

        return condition.Operator switch
        {
            Operator.In => BuildInCondition(fieldExpression, condition.Value, ref paramCounter, parameters, condition.PropertyType, condition.PropertyName),
            Operator.NotIn => $"NOT ({BuildInCondition(fieldExpression, condition.Value, ref paramCounter, parameters, condition.PropertyType, condition.PropertyName)})",
            _ => throw new NotSupportedException($"Set operator {condition.Operator} is not supported")
        };
    }

    private static string BuildStringCondition(FilterNode<T>.Condition condition, ref int paramCounter, List<MySqlParameter> parameters)
    {
        var fieldExpression = BuildTextAccessor(condition.PropertyName);

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

    private static string BuildInCondition(string fieldExpression, object? value, ref int paramCounter, List<MySqlParameter> parameters, Type? propertyType = null, string? propertyName = null)
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
                    var prop = typeof(T).GetProperty(propertyName ?? string.Empty, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase);
                    var format = prop != null
                        ? EnumSerializationAnalyzer.GetPropertyFormat(prop)
                        : EnumSerializationAnalyzer.GetDefaultFormat(underlyingType);

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
