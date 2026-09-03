using DuckDB.NET.Data;

namespace SearchLite.DuckDB;

public record Clause
{
    public required string Sql { get; init; }
    public List<DuckDBParameter> Parameters { get; init; } = [];
}

public static class WhereClauseBuilder<T>
{
    /// <summary>
    /// The JSON value that field paths are resolved against. At the top level this is the
    /// <c>document</c> column of type <typeparamref name="T"/>; inside a collection predicate it is
    /// the unnested JSON element of the enclosing collection.
    /// </summary>
    private sealed record Scope(string Root, Type RootType, int Depth)
    {
        public static readonly Scope Document = new("document", typeof(T), 0);

        /// <summary>
        /// Creates the scope for the elements of a collection at this level, returning the alias
        /// the unnested element column must be given.
        /// </summary>
        public (string alias, Scope inner) Nested(Type elementType)
        {
            var alias = $"e{Depth}";
            return (alias, new Scope(alias, elementType, Depth + 1));
        }
    }

    public static IReadOnlyList<Clause> BuildClauses(List<FilterNode<T>> filters)
    {
        var globalParamCounter = 0;
        return filters.Select(filter => BuildClause(filter, ref globalParamCounter)).ToList();
    }

    private static Clause BuildClause(FilterNode<T> filter, ref int globalParamCounter)
    {
        var parameters = new List<DuckDBParameter>();
        var sql = BuildSql(filter, Scope.Document, ref globalParamCounter, parameters);
        return new Clause
        {
            Sql = sql,
            Parameters = parameters
        };
    }

    private static string BuildSql(FilterNode<T> node, Scope scope, ref int paramCounter, List<DuckDBParameter> parameters)
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
        List<DuckDBParameter> parameters)
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
    /// JSON path for a dotted property path: "$" for the scope root itself, "$.a.b" otherwise.
    /// </summary>
    private static string JsonPath(string propertyName) =>
        propertyName.Length == 0 ? "$" : $"$.{propertyName}";

    private static string Extract(Scope scope, string propertyName) =>
        $"json_extract({scope.Root}, '{JsonPath(propertyName)}')";

    private static string ExtractString(Scope scope, string propertyName) =>
        $"json_extract_string({scope.Root}, '{JsonPath(propertyName)}')";

    /// <summary>
    /// A derived table that unnests the JSON array at the given path into one JSON element per
    /// row. A missing or null value casts to a NULL list, which unnests to no rows.
    /// </summary>
    private static string ArraySource(Scope scope, string propertyName, string alias) =>
        $"(SELECT unnest(CAST({Extract(scope, propertyName)} AS JSON[])) AS {alias})";

    private static string BuildQuantifierSql(FilterNode<T>.Quantifier quantifier, Scope scope, ref int paramCounter,
        List<DuckDBParameter> parameters)
    {
        var (alias, inner) = scope.Nested(quantifier.ElementType);
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
        List<DuckDBParameter> parameters)
    {
        string countExpression;
        if (count.Predicate == null)
        {
            // json_array_length is 0 for a JSON null and NULL for a missing path.
            countExpression = $"COALESCE(json_array_length({scope.Root}, '{JsonPath(count.PropertyName)}'), 0)";
        }
        else
        {
            var (alias, inner) = scope.Nested(count.ElementType);
            var predicate = BuildSql(count.Predicate, inner, ref paramCounter, parameters);
            countExpression = $"(SELECT COUNT(*) FROM {ArraySource(scope, count.PropertyName, alias)} WHERE {predicate})";
        }

        var paramName = $"p{paramCounter++}";
        parameters.Add(new DuckDBParameter(paramName, count.Value));
        return $"{countExpression} {GetOperatorString(count.Operator)} ${paramName}";
    }

    private static string BuildConditionSql(FilterNode<T>.Condition condition, Scope scope, ref int paramCounter,
        List<DuckDBParameter> parameters)
    {
        if (IsNullOperator(condition.Operator))
        {
            return BuildNullCondition(scope, condition.PropertyName, condition.Operator);
        }

        if (IsStringNullOrEmptyOperator(condition.Operator))
        {
            return BuildStringNullOrEmptyCondition(scope, condition.PropertyName, condition.Operator);
        }

        if (IsCollectionContainsOperator(condition.Operator))
        {
            return BuildCollectionContainsCondition(condition, scope, ref paramCounter, parameters);
        }

        if (IsSetOperator(condition.Operator))
        {
            return BuildSetCondition(condition, scope, ref paramCounter, parameters);
        }

        if (IsStringOperator(condition.Operator))
        {
            return BuildStringCondition(condition, scope, ref paramCounter, parameters);
        }

        var fieldExpression = BuildFieldExpression(condition.PropertyType, scope, condition.PropertyName);
        var operatorString = GetOperatorString(condition.Operator);
        var paramName = $"p{paramCounter++}";

        object? paramValue = condition.Value;
        var underlyingType = Nullable.GetUnderlyingType(condition.PropertyType) ?? condition.PropertyType;

        if (underlyingType.IsEnum)
        {
            var format = GetEnumFormat(underlyingType, scope, condition.PropertyName);

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

        parameters.Add(new DuckDBParameter(paramName, paramValue ?? DBNull.Value));

        return $"{fieldExpression} {operatorString} ${paramName}";
    }

    /// <summary>
    /// Builds the JSON accessor expression for a property, casting to a DuckDB type when the value is
    /// non-textual. Text-shaped values (strings, GUIDs, dates, string enums) use
    /// <c>json_extract_string</c> so the surrounding JSON quotes are stripped.
    /// </summary>
    private static string BuildFieldExpression(Type type, Scope scope, string propertyName)
    {
        if (IsTextType(type, scope, propertyName))
        {
            return ExtractString(scope, propertyName);
        }

        var duckType = GetDuckDbType(type, scope, propertyName);
        return $"CAST({Extract(scope, propertyName)} AS {duckType})";
    }

    private static bool IsTextType(Type type, Scope scope, string propertyName)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        if (underlyingType.IsEnum)
        {
            return GetEnumFormat(underlyingType, scope, propertyName) == EnumSerializationFormat.String;
        }

        return underlyingType == typeof(string)
               || underlyingType == typeof(Guid)
               || underlyingType == typeof(DateTime)
               || underlyingType == typeof(DateTimeOffset);
    }

    /// <summary>
    /// Resolves the serialization format of an enum field by walking the path from the scope's
    /// root type, so a property-level converter is honored for nested and element fields too.
    /// Falls back to the enum type's own default when the path cannot be resolved (e.g. the bare
    /// element of an enum collection).
    /// </summary>
    private static EnumSerializationFormat GetEnumFormat(Type underlyingType, Scope scope, string propertyName)
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

    private static string GetDuckDbType(Type type, Scope scope, string propertyName)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        if (underlyingType.IsEnum)
        {
            return GetEnumFormat(underlyingType, scope, propertyName) == EnumSerializationFormat.String ? "VARCHAR" : "BIGINT";
        }

        return underlyingType switch
        {
            { } t when t == typeof(int) => "BIGINT",
            { } t when t == typeof(string) => "VARCHAR",
            { } t when t == typeof(bool) => "BOOLEAN",
            { } t when t == typeof(double) => "DOUBLE",
            { } t when t == typeof(decimal) => "DOUBLE",
            { } t when t == typeof(DateTime) => "VARCHAR",
            { } t when t == typeof(DateTimeOffset) => "VARCHAR",
            { } t when t == typeof(Guid) => "VARCHAR",
            { } t when t == typeof(byte) => "BIGINT",
            { } t when t == typeof(short) => "BIGINT",
            { } t when t == typeof(long) => "BIGINT",
            { } t when t == typeof(float) => "DOUBLE",
            { } t when t == typeof(char) => "BIGINT",
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
        // json_extract returns SQL NULL when the path is absent, but the JSON literal 'null' when the
        // path is present with an explicit null value. Both represent a logical null here.
        var fieldExpression = Extract(scope, propertyName);

        return op switch
        {
            Operator.IsNull => $"({fieldExpression} IS NULL OR {fieldExpression} = 'null')",
            Operator.IsNotNull => $"({fieldExpression} IS NOT NULL AND {fieldExpression} != 'null')",
            _ => throw new NotSupportedException($"Null operator {op} is not supported")
        };
    }

    private static string BuildStringNullOrEmptyCondition(Scope scope, string propertyName, Operator op)
    {
        var fieldExpression = ExtractString(scope, propertyName);

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

    private static bool IsCollectionContainsOperator(Operator op)
    {
        return op is Operator.CollectionContains or Operator.CollectionNotContains;
    }

    private static string BuildCollectionContainsCondition(FilterNode<T>.Condition condition, Scope scope, ref int paramCounter, List<DuckDBParameter> parameters)
    {
        var paramName = $"p{paramCounter++}";

        // PropertyType is the collection type (e.g. List<string>, int[]). Derive the element type
        // so the searched value is typed the same way scalar params are typed elsewhere.
        var elementType = CollectionTypes.GetElementType(condition.PropertyType);
        var underlyingType = Nullable.GetUnderlyingType(elementType) ?? elementType;

        object? paramValue = condition.Value;

        if (underlyingType.IsEnum)
        {
            // A converter attribute on the collection property does not apply to its elements, so
            // the element format is the enum type's own default.
            var format = EnumSerializationAnalyzer.GetDefaultFormat(underlyingType);

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

        parameters.Add(new DuckDBParameter(paramName, paramValue ?? DBNull.Value));

        var elementSqlType = GetCollectionElementSqlType(underlyingType);
        var extracted = Extract(scope, condition.PropertyName);
        var listExpression = $"CAST({extracted} AS {elementSqlType}[])";
        var existsClause = $"({extracted} IS NOT NULL AND list_contains({listExpression}, ${paramName}))";

        return condition.Operator switch
        {
            Operator.CollectionContains => existsClause,
            Operator.CollectionNotContains => $"NOT {existsClause}",
            _ => throw new NotSupportedException($"Collection operator {condition.Operator} is not supported")
        };
    }

    private static string GetCollectionElementSqlType(Type underlyingElementType)
    {
        if (underlyingElementType.IsEnum)
        {
            return EnumSerializationAnalyzer.GetDefaultFormat(underlyingElementType) == EnumSerializationFormat.String
                ? "VARCHAR"
                : "BIGINT";
        }

        return underlyingElementType switch
        {
            { } t when t == typeof(int) => "BIGINT",
            { } t when t == typeof(long) => "BIGINT",
            { } t when t == typeof(short) => "BIGINT",
            { } t when t == typeof(byte) => "BIGINT",
            { } t when t == typeof(char) => "BIGINT",
            { } t when t == typeof(bool) => "BOOLEAN",
            { } t when t == typeof(double) => "DOUBLE",
            { } t when t == typeof(decimal) => "DOUBLE",
            { } t when t == typeof(float) => "DOUBLE",
            _ => "VARCHAR"
        };
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

    private static string BuildSetCondition(FilterNode<T>.Condition condition, Scope scope, ref int paramCounter, List<DuckDBParameter> parameters)
    {
        var fieldExpression = BuildFieldExpression(condition.PropertyType, scope, condition.PropertyName);

        return condition.Operator switch
        {
            Operator.In => BuildInCondition(fieldExpression, condition.Value, scope, ref paramCounter, parameters, condition.PropertyType, condition.PropertyName),
            Operator.NotIn => $"NOT ({BuildInCondition(fieldExpression, condition.Value, scope, ref paramCounter, parameters, condition.PropertyType, condition.PropertyName)})",
            _ => throw new NotSupportedException($"Set operator {condition.Operator} is not supported")
        };
    }

    private static string BuildStringCondition(FilterNode<T>.Condition condition, Scope scope, ref int paramCounter, List<DuckDBParameter> parameters)
    {
        var fieldExpression = ExtractString(scope, condition.PropertyName);

        return condition.Operator switch
        {
            Operator.Contains => BuildLikeCondition(fieldExpression, condition.Value, "%{0}%", false, ref paramCounter, parameters),
            Operator.NotContains => $"NOT ({BuildLikeCondition(fieldExpression, condition.Value, "%{0}%", false, ref paramCounter, parameters)})",
            Operator.ContainsIgnoreCase => BuildLikeCondition(fieldExpression, condition.Value, "%{0}%", true, ref paramCounter, parameters),
            Operator.NotContainsIgnoreCase => $"NOT ({BuildLikeCondition(fieldExpression, condition.Value, "%{0}%", true, ref paramCounter, parameters)})",
            Operator.StartsWith => BuildLikeCondition(fieldExpression, condition.Value, "{0}%", false, ref paramCounter, parameters),
            Operator.NotStartsWith => $"NOT ({BuildLikeCondition(fieldExpression, condition.Value, "{0}%", false, ref paramCounter, parameters)})",
            Operator.StartsWithIgnoreCase => BuildLikeCondition(fieldExpression, condition.Value, "{0}%", true, ref paramCounter, parameters),
            Operator.NotStartsWithIgnoreCase => $"NOT ({BuildLikeCondition(fieldExpression, condition.Value, "{0}%", true, ref paramCounter, parameters)})",
            Operator.EndsWith => BuildLikeCondition(fieldExpression, condition.Value, "%{0}", false, ref paramCounter, parameters),
            Operator.NotEndsWith => $"NOT ({BuildLikeCondition(fieldExpression, condition.Value, "%{0}", false, ref paramCounter, parameters)})",
            Operator.EndsWithIgnoreCase => BuildLikeCondition(fieldExpression, condition.Value, "%{0}", true, ref paramCounter, parameters),
            Operator.NotEndsWithIgnoreCase => $"NOT ({BuildLikeCondition(fieldExpression, condition.Value, "%{0}", true, ref paramCounter, parameters)})",
            _ => throw new NotSupportedException($"String operator {condition.Operator} is not supported")
        };
    }

    private static string BuildLikeCondition(string fieldExpression, object value, string pattern, bool ignoreCase,
        ref int paramCounter, List<DuckDBParameter> parameters)
    {
        var paramName = $"p{paramCounter++}";
        var escaped = EscapeLike(value?.ToString() ?? string.Empty);
        parameters.Add(new DuckDBParameter(paramName, string.Format(pattern, escaped)));

        return ignoreCase
            ? $"lower({fieldExpression}) LIKE lower(${paramName}) ESCAPE '\\'"
            : $"{fieldExpression} LIKE ${paramName} ESCAPE '\\'";
    }

    private static string EscapeLike(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
    }

    private static string BuildInCondition(string fieldExpression, object? value, Scope scope, ref int paramCounter, List<DuckDBParameter> parameters, Type? propertyType = null, string? propertyName = null)
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
                var paramName = $"p{paramCounter++}";
                object? paramValue = item;

                var underlyingType = propertyType != null ? Nullable.GetUnderlyingType(propertyType) ?? propertyType : null;

                if (underlyingType != null && underlyingType.IsEnum)
                {
                    var format = GetEnumFormat(underlyingType, scope, propertyName ?? string.Empty);

                    paramValue = format == EnumSerializationFormat.String
                        ? item?.ToString()
                        : item != null
                            ? Convert.ChangeType(item, underlyingType.GetEnumUnderlyingType())
                            : null;
                }
                else if (underlyingType == typeof(Guid))
                {
                    paramValue = item?.ToString();
                }

                parameters.Add(new DuckDBParameter(paramName, paramValue ?? DBNull.Value));
                paramNames.Add($"${paramName}");
            }

            return $"{fieldExpression} IN ({string.Join(", ", paramNames)})";
        }

        var singleParamName = $"p{paramCounter++}";
        object? singleParamValue = value;
        if (propertyType == typeof(Guid) || propertyType == typeof(Guid?))
        {
            singleParamValue = value?.ToString() ?? "";
        }

        parameters.Add(new DuckDBParameter(singleParamName, singleParamValue ?? DBNull.Value));
        return $"{fieldExpression} = ${singleParamName}";
    }
}
