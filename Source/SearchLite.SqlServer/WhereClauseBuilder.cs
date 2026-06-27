using Microsoft.Data.SqlClient;

namespace SearchLite.SqlServer;

public record Clause
{
    public required string Sql { get; init; }
    public List<SqlParameter> Parameters { get; init; } = [];
}

public static class WhereClauseBuilder<T>
{
    public static IReadOnlyList<Clause> BuildClauses(List<FilterNode<T>> filters)
    {
        var globalParamCounter = 0;
        return filters.Select(filter => BuildClause(filter, ref globalParamCounter)).ToList();
    }

    private static Clause BuildClause(FilterNode<T> filter, ref int globalParamCounter)
    {
        var parameters = new List<SqlParameter>();
        var sql = BuildSql(filter, ref globalParamCounter, parameters);
        return new Clause
        {
            Sql = sql,
            Parameters = parameters
        };
    }

    private static string BuildSql(FilterNode<T> node, ref int paramCounter, List<SqlParameter> parameters)
    {
        return node switch
        {
            FilterNode<T>.Condition condition => BuildConditionSql(condition, ref paramCounter, parameters),
            FilterNode<T>.Group group => BuildGroupSql(group, ref paramCounter, parameters),
            _ => throw new ArgumentException($"Unsupported node type: {node.GetType()}")
        };
    }

    private static string BuildGroupSql(FilterNode<T>.Group group, ref int paramCounter,
        List<SqlParameter> parameters)
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
            : conditions.FirstOrDefault() ?? "1=1"; // SQL Server uses 1=1 for TRUE
    }

    /// <summary>
    /// Builds a JSON path expression (e.g. "$.Author.Name") for the given dotted property path.
    /// SQL Server's JSON_VALUE / OPENJSON accept this lax path syntax directly.
    /// </summary>
    private static string BuildJsonPath(string propertyName)
    {
        var segments = FieldPath.Split(propertyName);
        return "$." + string.Join(".", segments);
    }

    private static string BuildValueAccessor(string propertyName)
    {
        return $"JSON_VALUE(document, '{BuildJsonPath(propertyName)}')";
    }

    private static string BuildConditionSql(FilterNode<T>.Condition condition, ref int paramCounter,
        List<SqlParameter> parameters)
    {
        if (IsNullOperator(condition.Operator))
        {
            return BuildNullCondition(condition.PropertyName, condition.Operator);
        }

        if (IsStringNullOrEmptyOperator(condition.Operator))
        {
            return BuildStringNullOrEmptyCondition(condition.PropertyName, condition.Operator);
        }

        if (IsCollectionContainsOperator(condition.Operator))
        {
            return BuildCollectionContainsCondition(condition, ref paramCounter, parameters);
        }

        if (IsSetOperator(condition.Operator))
        {
            return BuildSetCondition(condition, ref paramCounter, parameters);
        }

        if (IsStringOperator(condition.Operator))
        {
            return BuildStringCondition(condition, ref paramCounter, parameters);
        }

        var sqlType = GetSqlType(condition.PropertyType, condition.PropertyName);
        var operatorString = GetOperatorString(condition.Operator);
        var paramName = $"@p{paramCounter++}";

        object? paramValue = condition.Value;
        var underlyingType = Nullable.GetUnderlyingType(condition.PropertyType) ?? condition.PropertyType;

        if (underlyingType.IsEnum)
        {
            var format = ResolveEnumFormat(condition.PropertyName, underlyingType);

            if (format == EnumSerializationFormat.String)
            {
                paramValue = condition.Value?.ToString();
            }
            else
            {
                if (condition.Value != null)
                    paramValue = Convert.ChangeType(condition.Value, underlyingType.GetEnumUnderlyingType());
            }
        }
        else if (underlyingType == typeof(Guid))
        {
            paramValue = condition.Value?.ToString();
        }

        parameters.Add(new SqlParameter(paramName, paramValue ?? DBNull.Value));

        return $"CAST({BuildValueAccessor(condition.PropertyName)} AS {sqlType}) {operatorString} {paramName}";
    }

    private static EnumSerializationFormat ResolveEnumFormat(string propertyName, Type underlyingType)
    {
        var prop = typeof(T).GetProperty(propertyName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase);
        return prop != null
            ? EnumSerializationAnalyzer.GetPropertyFormat(prop)
            : EnumSerializationAnalyzer.GetDefaultFormat(underlyingType);
    }

    private static string GetSqlType(Type type, string propertyName)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        if (underlyingType.IsEnum)
        {
            var format = ResolveEnumFormat(propertyName, underlyingType);
            return format == EnumSerializationFormat.String ? "NVARCHAR(MAX)" : "BIGINT";
        }

        return underlyingType switch
        {
            { } t when t == typeof(int) => "INT",
            { } t when t == typeof(string) => "NVARCHAR(MAX)",
            { } t when t == typeof(bool) => "BIT",
            { } t when t == typeof(double) => "FLOAT",
            { } t when t == typeof(decimal) => "DECIMAL(38,18)",
            { } t when t == typeof(DateTime) => "DATETIME2",
            { } t when t == typeof(DateTimeOffset) => "DATETIMEOFFSET",
            { } t when t == typeof(Guid) => "UNIQUEIDENTIFIER",
            { } t when t == typeof(byte) => "INT",
            { } t when t == typeof(short) => "INT",
            { } t when t == typeof(long) => "BIGINT",
            { } t when t == typeof(float) => "REAL",
            { } t when t == typeof(char) => "INT",
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
        var fieldExpression = BuildValueAccessor(propertyName);

        return op switch
        {
            Operator.IsNull => $"{fieldExpression} IS NULL",
            Operator.IsNotNull => $"{fieldExpression} IS NOT NULL",
            _ => throw new NotSupportedException($"Null operator {op} is not supported")
        };
    }

    private static string BuildStringNullOrEmptyCondition(string propertyName, Operator op)
    {
        var fieldExpression = BuildValueAccessor(propertyName);

        return op switch
        {
            Operator.IsNullOrEmpty => $"({fieldExpression} IS NULL OR {fieldExpression} = '')",
            Operator.IsNotNullOrEmpty => $"({fieldExpression} IS NOT NULL AND {fieldExpression} != '')",
            Operator.IsNullOrWhiteSpace => $"({fieldExpression} IS NULL OR LTRIM(RTRIM(REPLACE(REPLACE(REPLACE({fieldExpression}, CHAR(9), ' '), CHAR(10), ' '), CHAR(13), ' '))) = '')",
            Operator.IsNotNullOrWhiteSpace => $"({fieldExpression} IS NOT NULL AND LTRIM(RTRIM(REPLACE(REPLACE(REPLACE({fieldExpression}, CHAR(9), ' '), CHAR(10), ' '), CHAR(13), ' '))) != '')",
            _ => throw new NotSupportedException($"String operator {op} is not supported")
        };
    }

    private static bool IsSetOperator(Operator op)
    {
        return op is Operator.In or Operator.NotIn;
    }

    private static bool IsCollectionContainsOperator(Operator op)
    {
        return op is Operator.CollectionContains or Operator.CollectionNotContains;
    }

    private static string BuildCollectionContainsCondition(FilterNode<T>.Condition condition, ref int paramCounter, List<SqlParameter> parameters)
    {
        var paramName = $"@p{paramCounter++}";

        var elementType = GetCollectionElementType(condition.PropertyType);
        var underlyingType = Nullable.GetUnderlyingType(elementType) ?? elementType;

        object? paramValue = condition.Value;

        if (underlyingType.IsEnum)
        {
            var format = EnumSerializationAnalyzer.GetDefaultFormat(underlyingType);

            if (format == EnumSerializationFormat.String)
            {
                paramValue = condition.Value?.ToString();
            }
            else
            {
                if (condition.Value != null)
                    paramValue = Convert.ChangeType(condition.Value, underlyingType.GetEnumUnderlyingType());
            }
        }
        else if (underlyingType == typeof(Guid))
        {
            paramValue = condition.Value?.ToString();
        }

        parameters.Add(new SqlParameter(paramName, paramValue ?? DBNull.Value));

        // OPENJSON over the array, comparing each element's scalar value to the parameter.
        var existsClause =
            $"EXISTS (SELECT 1 FROM OPENJSON(document, '{BuildJsonPath(condition.PropertyName)}') WHERE value = {paramName})";

        return condition.Operator switch
        {
            Operator.CollectionContains => existsClause,
            Operator.CollectionNotContains => $"NOT {existsClause}",
            _ => throw new NotSupportedException($"Collection operator {condition.Operator} is not supported")
        };
    }

    private static Type GetCollectionElementType(Type collectionType)
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

    private static bool IsStringOperator(Operator op)
    {
        return op is Operator.Contains or Operator.NotContains or
                     Operator.ContainsIgnoreCase or Operator.NotContainsIgnoreCase or
                     Operator.StartsWith or Operator.NotStartsWith or
                     Operator.StartsWithIgnoreCase or Operator.NotStartsWithIgnoreCase or
                     Operator.EndsWith or Operator.NotEndsWith or
                     Operator.EndsWithIgnoreCase or Operator.NotEndsWithIgnoreCase;
    }

    private static string BuildSetCondition(FilterNode<T>.Condition condition, ref int paramCounter, List<SqlParameter> parameters)
    {
        var fieldExpression = $"CAST({BuildValueAccessor(condition.PropertyName)} AS {GetSqlType(condition.PropertyType, condition.PropertyName)})";

        return condition.Operator switch
        {
            Operator.In => BuildInCondition(fieldExpression, condition.Value, ref paramCounter, parameters, condition.PropertyType, condition.PropertyName),
            Operator.NotIn => $"NOT ({BuildInCondition(fieldExpression, condition.Value, ref paramCounter, parameters, condition.PropertyType, condition.PropertyName)})",
            _ => throw new NotSupportedException($"Set operator {condition.Operator} is not supported")
        };
    }

    private static string BuildStringCondition(FilterNode<T>.Condition condition, ref int paramCounter, List<SqlParameter> parameters)
    {
        var fieldExpression = $"CAST({BuildValueAccessor(condition.PropertyName)} AS NVARCHAR(MAX))";

        return condition.Operator switch
        {
            Operator.Contains => BuildLikeCondition(fieldExpression, condition.Value, "%{0}%", ref paramCounter, parameters),
            Operator.NotContains => $"NOT ({BuildLikeCondition(fieldExpression, condition.Value, "%{0}%", ref paramCounter, parameters)})",
            Operator.ContainsIgnoreCase => BuildLikeIgnoreCaseCondition(fieldExpression, condition.Value, "%{0}%", ref paramCounter, parameters),
            Operator.NotContainsIgnoreCase => $"NOT ({BuildLikeIgnoreCaseCondition(fieldExpression, condition.Value, "%{0}%", ref paramCounter, parameters)})",
            Operator.StartsWith => BuildLikeCondition(fieldExpression, condition.Value, "{0}%", ref paramCounter, parameters),
            Operator.NotStartsWith => $"NOT ({BuildLikeCondition(fieldExpression, condition.Value, "{0}%", ref paramCounter, parameters)})",
            Operator.StartsWithIgnoreCase => BuildLikeIgnoreCaseCondition(fieldExpression, condition.Value, "{0}%", ref paramCounter, parameters),
            Operator.NotStartsWithIgnoreCase => $"NOT ({BuildLikeIgnoreCaseCondition(fieldExpression, condition.Value, "{0}%", ref paramCounter, parameters)})",
            Operator.EndsWith => BuildLikeCondition(fieldExpression, condition.Value, "%{0}", ref paramCounter, parameters),
            Operator.NotEndsWith => $"NOT ({BuildLikeCondition(fieldExpression, condition.Value, "%{0}", ref paramCounter, parameters)})",
            Operator.EndsWithIgnoreCase => BuildLikeIgnoreCaseCondition(fieldExpression, condition.Value, "%{0}", ref paramCounter, parameters),
            Operator.NotEndsWithIgnoreCase => $"NOT ({BuildLikeIgnoreCaseCondition(fieldExpression, condition.Value, "%{0}", ref paramCounter, parameters)})",
            _ => throw new NotSupportedException($"String operator {condition.Operator} is not supported")
        };
    }

    /// <summary>
    /// Case-sensitive LIKE. The pattern is built into the parameter, with LIKE special
    /// characters escaped and an ESCAPE clause so user input is treated literally. The
    /// collation forces a case- and accent-sensitive comparison regardless of column collation.
    /// </summary>
    private static string BuildLikeCondition(string fieldExpression, object value, string patternFormat, ref int paramCounter, List<SqlParameter> parameters)
    {
        var paramName = $"@p{paramCounter++}";
        parameters.Add(new SqlParameter(paramName, string.Format(patternFormat, EscapeLike(value?.ToString() ?? string.Empty))));
        return $"{fieldExpression} COLLATE Latin1_General_CS_AS LIKE {paramName} ESCAPE '\\'";
    }

    private static string BuildLikeIgnoreCaseCondition(string fieldExpression, object value, string patternFormat, ref int paramCounter, List<SqlParameter> parameters)
    {
        var paramName = $"@p{paramCounter++}";
        parameters.Add(new SqlParameter(paramName, string.Format(patternFormat, EscapeLike(value?.ToString() ?? string.Empty))));
        return $"LOWER({fieldExpression}) LIKE LOWER({paramName}) ESCAPE '\\'";
    }

    private static string EscapeLike(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_")
            .Replace("[", "\\[");
    }

    private static string BuildInCondition(string fieldExpression, object? value, ref int paramCounter, List<SqlParameter> parameters, Type? propertyType = null, string? propertyName = null)
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
                return "1 = 0";
            }

            var paramNames = new List<string>();
            foreach (var item in values)
            {
                var paramName = $"@p{paramCounter++}";
                object? paramValue = item;

                var underlyingType = propertyType != null ? Nullable.GetUnderlyingType(propertyType) ?? propertyType : null;

                if (underlyingType != null && underlyingType.IsEnum)
                {
                    var format = ResolveEnumFormat(propertyName ?? string.Empty, underlyingType);

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
                else if (underlyingType == typeof(Guid))
                {
                    paramValue = item?.ToString();
                }

                parameters.Add(new SqlParameter(paramName, paramValue ?? DBNull.Value));
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

        parameters.Add(new SqlParameter(singleParamName, singleParamValue ?? DBNull.Value));
        return $"{fieldExpression} = {singleParamName}";
    }
}
