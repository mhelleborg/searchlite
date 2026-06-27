using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;

namespace SearchLite.Postgres;

public record Clause
{
    public required string Sql { get; init; }
    public List<NpgsqlParameter> Parameters { get; init; } = [];
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
        var parameters = new List<NpgsqlParameter>();
        var sql = BuildSql(filter, ref globalParamCounter, parameters);
        return new Clause
        {
            Sql = sql,
            Parameters = parameters
        };
    }

    private static string BuildSql(FilterNode<T> node, ref int paramCounter, List<NpgsqlParameter> parameters)
    {
        return node switch
        {
            FilterNode<T>.Condition condition => BuildConditionSql(condition, ref paramCounter, parameters),
            FilterNode<T>.Group group => BuildGroupSql(group, ref paramCounter, parameters),
            _ => throw new ArgumentException($"Unsupported node type: {node.GetType()}")
        };
    }

    private static string BuildGroupSql(FilterNode<T>.Group group, ref int paramCounter,
        List<NpgsqlParameter> parameters)
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
    /// Builds a path-aware JSONB text accessor for the given dotted property path.
    /// 1 segment  -> (document->>'seg')
    /// N segments -> (document #>> '{seg1,seg2,...}')
    /// </summary>
    private static string BuildTextAccessor(string propertyName)
    {
        var segments = FieldPath.Split(propertyName);
        return segments.Length == 1
            ? $"(document->>'{segments[0]}')"
            : $"(document #>> '{{{string.Join(",", segments)}}}')";
    }

    private static string BuildConditionSql(FilterNode<T>.Condition condition, ref int paramCounter,
        List<NpgsqlParameter> parameters)
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

        // Equal/NotEqual on containment-eligible types use JSONB @> so the GIN index is used.
        if ((condition.Operator is Operator.Equal or Operator.NotEqual)
            && IsContainmentEligible(underlyingType))
        {
            return BuildContainmentEqualityCondition(condition, underlyingType, ref paramCounter, parameters);
        }

        var fieldExpression = BuildTextAccessor(condition.PropertyName);
        var postgresType = GetPostgresType(condition.PropertyType, condition.PropertyName);
        var operatorString = GetOperatorString(condition.Operator);
        var paramName = $"@p{paramCounter++}";

        object? paramValue = condition.Value;

        if (underlyingType.IsEnum)
        {
            var format = ResolveEnumFormat(condition.PropertyName, underlyingType);

            if (format == EnumSerializationFormat.String)
            {
                paramValue = condition.Value?.ToString();
            }
            else
            {
                paramValue = condition.Value != null ? Convert.ChangeType(condition.Value, underlyingType.GetEnumUnderlyingType()) : null;
            }
        }

        parameters.Add(new NpgsqlParameter(paramName, paramValue ?? DBNull.Value));

        return $"{fieldExpression}{postgresType} {operatorString} {paramName}";
    }

    /// <summary>
    /// Underlying types for which JSONB containment (@>) reliably matches the stored representation.
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

    /// <summary>
    /// Wraps a leaf JSON node inside a nested object following the dotted path.
    /// e.g. path "Author.Name", leaf "Alice" -> {"Author":{"Name":"Alice"}}
    /// </summary>
    private static string BuildPathJson(string propertyName, JsonNode? leaf)
    {
        var segments = FieldPath.Split(propertyName);
        JsonNode? node = leaf;
        for (var i = segments.Length - 1; i >= 0; i--)
        {
            var obj = new JsonObject { [segments[i]] = node };
            node = obj;
        }

        return node!.ToJsonString();
    }

    private static string BuildContainmentEqualityCondition(FilterNode<T>.Condition condition, Type underlyingType,
        ref int paramCounter, List<NpgsqlParameter> parameters)
    {
        var leaf = BuildLeafJson(condition.Value, underlyingType, condition.PropertyName);
        var json = BuildPathJson(condition.PropertyName, leaf);

        var paramName = $"@p{paramCounter++}";
        parameters.Add(new NpgsqlParameter(paramName, json));

        return condition.Operator == Operator.Equal
            ? $"document @> {paramName}::jsonb"
            : $"NOT (document @> {paramName}::jsonb)";
    }

    private static bool IsCollectionOperator(Operator op)
    {
        return op is Operator.CollectionContains or Operator.CollectionNotContains;
    }

    private static string BuildCollectionCondition(FilterNode<T>.Condition condition, ref int paramCounter,
        List<NpgsqlParameter> parameters)
    {
        var elementType = GetElementType(condition.PropertyType);
        var underlyingElementType = Nullable.GetUnderlyingType(elementType) ?? elementType;

        var leaf = BuildLeafJson(condition.Value, underlyingElementType, condition.PropertyName);
        var array = new JsonArray { leaf };
        var json = BuildPathJson(condition.PropertyName, array);

        var paramName = $"@p{paramCounter++}";
        parameters.Add(new NpgsqlParameter(paramName, json));

        return condition.Operator == Operator.CollectionContains
            ? $"document @> {paramName}::jsonb"
            : $"NOT (document @> {paramName}::jsonb)";
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

        // IEnumerable<TElem> implemented somewhere in the hierarchy.
        var enumerableInterface = collectionType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        if (enumerableInterface != null)
        {
            return enumerableInterface.GetGenericArguments()[0];
        }

        throw new NotSupportedException($"Cannot determine element type for collection type {collectionType}");
    }

    private static string GetPostgresType(Type type, string propertyName)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        if (underlyingType.IsEnum)
        {
            var prop = typeof(T).GetProperty(propertyName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase);
            if (prop != null)
            {
                var format = EnumSerializationAnalyzer.GetPropertyFormat(prop);
                return format == EnumSerializationFormat.String ? "::text" : "::integer";
            }
            else
            {
                var defaultFormat = EnumSerializationAnalyzer.GetDefaultFormat(underlyingType);
                return defaultFormat == EnumSerializationFormat.String ? "::text" : "::integer";
            }
        }

        return underlyingType switch
        {
            { } t when t == typeof(int) => "::integer",
            { } t when t == typeof(string) => "::text",
            { } t when t == typeof(bool) => "::boolean",
            { } t when t == typeof(double) => "::numeric",
            { } t when t == typeof(decimal) => "::numeric",
            { } t when t == typeof(DateTime) => "::timestamp",
            { } t when t == typeof(DateTimeOffset) => "::timestamptz",
            { } t when t == typeof(Guid) => "::uuid",
            { } t when t == typeof(byte) => "::smallint",
            { } t when t == typeof(short) => "::smallint",
            { } t when t == typeof(long) => "::bigint",
            { } t when t == typeof(float) => "::real",
            { } t when t == typeof(char) => "::integer",
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

        return op switch
        {
            Operator.IsNullOrEmpty => $"({fieldExpression} IS NULL OR {fieldExpression} = '')",
            Operator.IsNotNullOrEmpty => $"({fieldExpression} IS NOT NULL AND {fieldExpression} != '')",
            Operator.IsNullOrWhiteSpace => $"({fieldExpression} IS NULL OR trim(replace(replace(replace({fieldExpression}, chr(9), ' '), chr(10), ' '), chr(13), ' ')) = '')",
            Operator.IsNotNullOrWhiteSpace => $"({fieldExpression} IS NOT NULL AND trim(replace(replace(replace({fieldExpression}, chr(9), ' '), chr(10), ' '), chr(13), ' ')) != '')",
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

    private static string BuildSetCondition(FilterNode<T>.Condition condition, ref int paramCounter, List<NpgsqlParameter> parameters)
    {
        var accessor = BuildTextAccessor(condition.PropertyName);
        var fieldExpression = condition.PropertyType == typeof(string)
            ? $"{accessor}::text"
            : $"{accessor}{GetPostgresType(condition.PropertyType, condition.PropertyName)}";

        return condition.Operator switch
        {
            Operator.In => BuildInCondition(fieldExpression, condition.Value, ref paramCounter, parameters, condition.PropertyType, condition.PropertyName),
            Operator.NotIn => $"NOT ({BuildInCondition(fieldExpression, condition.Value, ref paramCounter, parameters, condition.PropertyType, condition.PropertyName)})",
            _ => throw new NotSupportedException($"Set operator {condition.Operator} is not supported")
        };
    }

    private static string BuildStringCondition(FilterNode<T>.Condition condition, ref int paramCounter, List<NpgsqlParameter> parameters)
    {
        var fieldExpression = $"{BuildTextAccessor(condition.PropertyName)}::text";

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

    private static string BuildContainsCondition(string fieldExpression, object value, ref int paramCounter, List<NpgsqlParameter> parameters)
    {
        var paramName = $"@p{paramCounter++}";
        parameters.Add(new NpgsqlParameter(paramName, $"%{value}%"));
        return $"{fieldExpression} LIKE {paramName}";
    }

        private static string BuildInCondition(string fieldExpression, object? value, ref int paramCounter, List<NpgsqlParameter> parameters, Type? propertyType = null, string? propertyName = null)
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
                    EnumSerializationFormat format;
                    if (prop != null)
                    {
                        format = EnumSerializationAnalyzer.GetPropertyFormat(prop);
                    }
                    else
                    {
                        format = EnumSerializationAnalyzer.GetDefaultFormat(underlyingType);
                    }

                    if (format == EnumSerializationFormat.String)
                    {
                        paramValue = item?.ToString();
                    }
                    else
                    {
                        if (item != null)
                            paramValue = Convert.ChangeType(item, underlyingType.GetEnumUnderlyingType());
                    }
                }

                parameters.Add(new NpgsqlParameter(paramName, paramValue ?? DBNull.Value));
                paramNames.Add(paramName);
            }

            return $"{fieldExpression} IN ({string.Join(", ", paramNames)})";
        }

        var singleParamName = $"@p{paramCounter++}";
        parameters.Add(new NpgsqlParameter(singleParamName, value ?? DBNull.Value));
        return $"{fieldExpression} = {singleParamName}";
    }

    private static string BuildContainsIgnoreCaseCondition(string fieldExpression, object value, ref int paramCounter, List<NpgsqlParameter> parameters)
    {
        var paramName = $"@p{paramCounter++}";
        parameters.Add(new NpgsqlParameter(paramName, $"%{value}%"));
        return $"LOWER({fieldExpression}) LIKE LOWER({paramName})";
    }

    private static string BuildStartsWithCondition(string fieldExpression, object value, ref int paramCounter, List<NpgsqlParameter> parameters)
    {
        var paramName = $"@p{paramCounter++}";
        parameters.Add(new NpgsqlParameter(paramName, $"{value}%"));
        return $"{fieldExpression} LIKE {paramName}";
    }

    private static string BuildStartsWithIgnoreCaseCondition(string fieldExpression, object value, ref int paramCounter, List<NpgsqlParameter> parameters)
    {
        var paramName = $"@p{paramCounter++}";
        parameters.Add(new NpgsqlParameter(paramName, $"{value}%"));
        return $"LOWER({fieldExpression}) LIKE LOWER({paramName})";
    }

    private static string BuildEndsWithCondition(string fieldExpression, object value, ref int paramCounter, List<NpgsqlParameter> parameters)
    {
        var paramName = $"@p{paramCounter++}";
        parameters.Add(new NpgsqlParameter(paramName, $"%{value}"));
        return $"{fieldExpression} LIKE {paramName}";
    }

    private static string BuildEndsWithIgnoreCaseCondition(string fieldExpression, object value, ref int paramCounter, List<NpgsqlParameter> parameters)
    {
        var paramName = $"@p{paramCounter++}";
        parameters.Add(new NpgsqlParameter(paramName, $"%{value}"));
        return $"LOWER({fieldExpression}) LIKE LOWER({paramName})";
    }
}
