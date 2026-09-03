using Microsoft.Data.Sqlite;

namespace SearchLite.Sqlite;

public record Clause
{
    public required string Sql { get; init; }
    public List<SqliteParameter> Parameters { get; init; } = [];
}

public static class WhereClauseBuilder<T>
{
    /// <summary>
    /// The JSON value that field paths are resolved against. At the top level this is the
    /// <c>document</c> column of type <typeparamref name="T"/>; inside a collection predicate it is
    /// the <c>value</c> column of the <c>json_each</c> row for the current element.
    /// </summary>
    private sealed record Scope(string Root, Type RootType, int Depth)
    {
        public static readonly Scope Document = new("document", typeof(T), 0);

        /// <summary>
        /// Creates the scope for the elements of a collection at this level, returning the alias
        /// the enclosing <c>json_each</c> must be given.
        /// </summary>
        public (string alias, Scope inner) Nested(Type elementType)
        {
            var alias = $"e{Depth}";
            return (alias, new Scope($"{alias}.value", elementType, Depth + 1));
        }
    }

    public static IReadOnlyList<Clause> BuildClauses(List<FilterNode<T>> filters)
    {
        var globalParamCounter = 0;
        return filters.Select(filter => BuildClause(filter, ref globalParamCounter)).ToList();
    }

    private static Clause BuildClause(FilterNode<T> filter, ref int globalParamCounter)
    {
        var parameters = new List<SqliteParameter>();
        var sql = BuildSql(filter, Scope.Document, ref globalParamCounter, parameters);
        return new Clause
        {
            Sql = sql,
            Parameters = parameters
        };
    }

    private static string BuildSql(FilterNode<T> node, Scope scope, ref int paramCounter, List<SqliteParameter> parameters)
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
        List<SqliteParameter> parameters)
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
            // Sqlite uses 1 for TRUE and 0 for FALSE.
            return group.Operator == LogicalOperator.And ? "1" : "0";
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

    /// <summary>
    /// The extracted (untyped) JSON scalar at the given path. The bare element of a scalar
    /// collection is already a SQL value in <c>json_each</c>'s <c>value</c> column, so it is
    /// used directly rather than re-parsed.
    /// </summary>
    private static string Extract(Scope scope, string propertyName)
    {
        if (propertyName.Length == 0 && scope.Depth > 0)
        {
            return scope.Root;
        }

        return $"json_extract({scope.Root}, '{JsonPath(propertyName)}')";
    }

    /// <summary>
    /// A <c>json_each</c> source over the array at the given path that yields no rows when the
    /// value is missing, null or not an array (a bare <c>json_each</c> on a JSON null yields one
    /// null row).
    /// </summary>
    private static string ArraySource(Scope scope, string propertyName)
    {
        var path = JsonPath(propertyName);
        return $"json_each(CASE WHEN json_type({scope.Root}, '{path}') = 'array' THEN json_extract({scope.Root}, '{path}') ELSE '[]' END)";
    }

    private static string BuildQuantifierSql(FilterNode<T>.Quantifier quantifier, Scope scope, ref int paramCounter,
        List<SqliteParameter> parameters)
    {
        var (alias, inner) = scope.Nested(quantifier.ElementType);
        var source = $"{ArraySource(scope, quantifier.PropertyName)} AS {alias}";

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
        List<SqliteParameter> parameters)
    {
        string countExpression;
        if (count.Predicate == null)
        {
            // json_array_length is 0 for non-arrays and NULL for a missing path.
            countExpression = $"COALESCE(json_array_length({scope.Root}, '{JsonPath(count.PropertyName)}'), 0)";
        }
        else
        {
            var (alias, inner) = scope.Nested(count.ElementType);
            var predicate = BuildSql(count.Predicate, inner, ref paramCounter, parameters);
            countExpression = $"(SELECT COUNT(*) FROM {ArraySource(scope, count.PropertyName)} AS {alias} WHERE {predicate})";
        }

        var paramName = $"@p{paramCounter++}";
        parameters.Add(new SqliteParameter(paramName, count.Value));
        return $"{countExpression} {GetOperatorString(count.Operator)} {paramName}";
    }

    private static string BuildConditionSql(FilterNode<T>.Condition condition, Scope scope, ref int paramCounter,
        List<SqliteParameter> parameters)
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

        var sqliteType = GetSqliteType(condition.PropertyType, scope, condition.PropertyName);
        var operatorString = GetOperatorString(condition.Operator);
        var paramName = $"@p{paramCounter++}";

        object? paramValue = condition.Value;
        var underlyingType = Nullable.GetUnderlyingType(condition.PropertyType) ?? condition.PropertyType;

        if (underlyingType.IsEnum)
        {
            var format = ResolveEnumFormat(scope, condition.PropertyName, underlyingType);

            if (format == EnumSerializationFormat.String)
            {
                paramValue = condition.Value?.ToString();
            }
            else
            {
                if(condition.Value != null)
                    paramValue = Convert.ChangeType(condition.Value, underlyingType.GetEnumUnderlyingType());
            }
        }
        else if (underlyingType == typeof(Guid))
        {
            paramValue = condition.Value?.ToString();
        }

        parameters.Add(new SqliteParameter(paramName, paramValue ?? DBNull.Value));

        return $"CAST({Extract(scope, condition.PropertyName)} AS {sqliteType}) {operatorString} {paramName}";
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

    private static string GetSqliteType(Type type, Scope scope, string propertyName)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        if (underlyingType.IsEnum)
        {
            var format = ResolveEnumFormat(scope, propertyName, underlyingType);
            return format == EnumSerializationFormat.String ? "TEXT" : "INTEGER";
        }

        return underlyingType switch
        {
            { } t when t == typeof(int) => "INTEGER",
            { } t when t == typeof(string) => "TEXT",
            { } t when t == typeof(bool) => "INTEGER",
            { } t when t == typeof(double) => "REAL",
            { } t when t == typeof(decimal) => "REAL",
            { } t when t == typeof(DateTime) => "TEXT",
            { } t when t == typeof(DateTimeOffset) => "TEXT",
            { } t when t == typeof(Guid) => "TEXT",
            { } t when t == typeof(byte) => "INTEGER",
            { } t when t == typeof(short) => "INTEGER",
            { } t when t == typeof(long) => "INTEGER",
            { } t when t == typeof(float) => "REAL",
            { } t when t == typeof(char) => "INTEGER",
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
        var fieldExpression = Extract(scope, propertyName);

        return op switch
        {
            Operator.IsNull => $"{fieldExpression} IS NULL",
            Operator.IsNotNull => $"{fieldExpression} IS NOT NULL",
            _ => throw new NotSupportedException($"Null operator {op} is not supported")
        };
    }

    private static string BuildStringNullOrEmptyCondition(Scope scope, string propertyName, Operator op)
    {
        var fieldExpression = $"CAST({Extract(scope, propertyName)} AS TEXT)";

        return op switch
        {
            Operator.IsNullOrEmpty => $"({fieldExpression} IS NULL OR {fieldExpression} = '')",
            Operator.IsNotNullOrEmpty => $"({fieldExpression} IS NOT NULL AND {fieldExpression} != '')",
            Operator.IsNullOrWhiteSpace => $"({fieldExpression} IS NULL OR trim(replace(replace(replace({fieldExpression}, char(9), ' '), char(10), ' '), char(13), ' ')) = '')",
            Operator.IsNotNullOrWhiteSpace => $"({fieldExpression} IS NOT NULL AND trim(replace(replace(replace({fieldExpression}, char(9), ' '), char(10), ' '), char(13), ' ')) != '')",
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

    private static string BuildCollectionContainsCondition(FilterNode<T>.Condition condition, Scope scope, ref int paramCounter, List<SqliteParameter> parameters)
    {
        var paramName = $"@p{paramCounter++}";

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

        parameters.Add(new SqliteParameter(paramName, paramValue ?? DBNull.Value));

        var (alias, _) = scope.Nested(elementType);
        var existsClause = $"EXISTS (SELECT 1 FROM json_each({scope.Root}, '{JsonPath(condition.PropertyName)}') AS {alias} WHERE {alias}.value = {paramName})";

        return condition.Operator switch
        {
            Operator.CollectionContains => existsClause,
            Operator.CollectionNotContains => $"NOT {existsClause}",
            _ => throw new NotSupportedException($"Collection operator {condition.Operator} is not supported")
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

    private static string BuildSetCondition(FilterNode<T>.Condition condition, Scope scope, ref int paramCounter, List<SqliteParameter> parameters)
    {
        var fieldExpression = $"CAST({Extract(scope, condition.PropertyName)} AS {GetSqliteType(condition.PropertyType, scope, condition.PropertyName)})";

        return condition.Operator switch
        {
            Operator.In => BuildInCondition(fieldExpression, condition.Value, scope, ref paramCounter, parameters, condition.PropertyType, condition.PropertyName),
            Operator.NotIn => $"NOT ({BuildInCondition(fieldExpression, condition.Value, scope, ref paramCounter, parameters, condition.PropertyType, condition.PropertyName)})",
            _ => throw new NotSupportedException($"Set operator {condition.Operator} is not supported")
        };
    }

    private static string BuildStringCondition(FilterNode<T>.Condition condition, Scope scope, ref int paramCounter, List<SqliteParameter> parameters)
    {
        var fieldExpression = $"CAST({Extract(scope, condition.PropertyName)} AS TEXT)";

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

    private static string BuildContainsCondition(string fieldExpression, object value, ref int paramCounter, List<SqliteParameter> parameters)
    {
        var paramName = $"@p{paramCounter++}";
        parameters.Add(new SqliteParameter(paramName, $"*{value}*"));
        return $"{fieldExpression} GLOB {paramName}";
    }

    private static string BuildInCondition(string fieldExpression, object? value, Scope scope, ref int paramCounter, List<SqliteParameter> parameters, Type? propertyType = null, string? propertyName = null)
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
                    var format = ResolveEnumFormat(scope, propertyName ?? string.Empty, underlyingType);

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

                parameters.Add(new SqliteParameter(paramName, paramValue ?? DBNull.Value));
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

        parameters.Add(new SqliteParameter(singleParamName, singleParamValue ?? DBNull.Value));
        return $"{fieldExpression} = {singleParamName}";
    }

    private static string BuildContainsIgnoreCaseCondition(string fieldExpression, object value, ref int paramCounter, List<SqliteParameter> parameters)
    {
        var paramName = $"@p{paramCounter++}";
        parameters.Add(new SqliteParameter(paramName, $"%{value}%"));
        return $"LOWER({fieldExpression}) LIKE LOWER({paramName})";
    }

    private static string BuildStartsWithCondition(string fieldExpression, object value, ref int paramCounter, List<SqliteParameter> parameters)
    {
        var paramName = $"@p{paramCounter++}";
        parameters.Add(new SqliteParameter(paramName, $"{value}*"));
        return $"{fieldExpression} GLOB {paramName}";
    }

    private static string BuildStartsWithIgnoreCaseCondition(string fieldExpression, object value, ref int paramCounter, List<SqliteParameter> parameters)
    {
        var paramName = $"@p{paramCounter++}";
        parameters.Add(new SqliteParameter(paramName, $"{value}%"));
        return $"LOWER({fieldExpression}) LIKE LOWER({paramName})";
    }

    private static string BuildEndsWithCondition(string fieldExpression, object value, ref int paramCounter, List<SqliteParameter> parameters)
    {
        var paramName = $"@p{paramCounter++}";
        parameters.Add(new SqliteParameter(paramName, $"*{value}"));
        return $"{fieldExpression} GLOB {paramName}";
    }

    private static string BuildEndsWithIgnoreCaseCondition(string fieldExpression, object value, ref int paramCounter, List<SqliteParameter> parameters)
    {
        var paramName = $"@p{paramCounter++}";
        parameters.Add(new SqliteParameter(paramName, $"%{value}"));
        return $"LOWER({fieldExpression}) LIKE LOWER({paramName})";
    }
}
