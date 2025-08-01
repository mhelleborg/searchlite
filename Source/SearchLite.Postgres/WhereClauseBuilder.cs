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
        return filters.Select(BuildClause);
    }

    private static Clause BuildClause(FilterNode<T> filter)
    {
        var paramCounter = 0;
        var parameters = new List<NpgsqlParameter>();
        var sql = BuildSql(filter, ref paramCounter, parameters);
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

    private static string BuildConditionSql(FilterNode<T>.Condition condition, ref int paramCounter,
        List<NpgsqlParameter> parameters)
    {
        // Handle null checks first
        if (IsNullOperator(condition.Operator))
        {
            return BuildNullCondition(condition.PropertyName, condition.Operator);
        }

        // Handle string null/empty checks differently
        if (IsStringNullOrEmptyOperator(condition.Operator))
        {
            return BuildStringNullOrEmptyCondition(condition.PropertyName, condition.Operator);
        }

        // Handle Contains and In operators
        if (IsSetOperator(condition.Operator))
        {
            return BuildSetCondition(condition, ref paramCounter, parameters);
        }

        // Handle string operators (Contains, StartsWith, EndsWith and their variants)
        if (IsStringOperator(condition.Operator))
        {
            return BuildStringCondition(condition, ref paramCounter, parameters);
        }

        var postgresType = GetPostgresType(condition.PropertyType);
        var operatorString = GetOperatorString(condition.Operator);
        var paramName = $"@p{paramCounter++}";

        parameters.Add(new NpgsqlParameter(paramName, condition.Value));

        return $"(document->>'{condition.PropertyName}'){postgresType} {operatorString} {paramName}";
    }

    private static string GetPostgresType(Type type)
    {
        return type switch
        {
            { } t when t == typeof(int) => "::integer",
            { } t when t == typeof(string) => "::text",
            { } t when t == typeof(bool) => "::boolean",
            { } t when t == typeof(double) => "::numeric",
            { } t when t == typeof(decimal) => "::numeric",
            { } t when t == typeof(DateTime) => "::timestamp",
            _ => throw new NotSupportedException($"Type {type} is not supported")
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
        var fieldExpression = $"(document->>'{propertyName}')";
        
        return op switch
        {
            Operator.IsNull => $"{fieldExpression} IS NULL",
            Operator.IsNotNull => $"{fieldExpression} IS NOT NULL",
            _ => throw new NotSupportedException($"Null operator {op} is not supported")
        };
    }

    private static string BuildStringNullOrEmptyCondition(string propertyName, Operator op)
    {
        var fieldExpression = $"(document->>'{propertyName}')";
        
        return op switch
        {
            Operator.IsNullOrEmpty => $"({fieldExpression} IS NULL OR {fieldExpression} = '')",
            Operator.IsNotNullOrEmpty => $"({fieldExpression} IS NOT NULL AND {fieldExpression} != '')",
            // Use replace to handle various whitespace characters like .NET's IsNullOrWhiteSpace
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
        var fieldExpression = condition.PropertyType == typeof(string) 
            ? $"(document->>'{condition.PropertyName}')::text"
            : $"(document->>'{condition.PropertyName}'){GetPostgresType(condition.PropertyType)}";

        return condition.Operator switch
        {
            Operator.In => BuildInCondition(fieldExpression, condition.Value, ref paramCounter, parameters),
            Operator.NotIn => $"NOT ({BuildInCondition(fieldExpression, condition.Value, ref paramCounter, parameters)})",
            _ => throw new NotSupportedException($"Set operator {condition.Operator} is not supported")
        };
    }

    private static string BuildStringCondition(FilterNode<T>.Condition condition, ref int paramCounter, List<NpgsqlParameter> parameters)
    {
        var fieldExpression = $"(document->>'{condition.PropertyName}')::text";

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

    private static string BuildInCondition(string fieldExpression, object value, ref int paramCounter, List<NpgsqlParameter> parameters)
    {
        // Handle collections (arrays, lists, etc.)
        if (value is System.Collections.IEnumerable enumerable and not string)
        {
            var values = new List<object>();
            foreach (var item in enumerable)
            {
                values.Add(item);
            }

            if (values.Count == 0)
            {
                return "FALSE"; // Always false condition
            }

            var paramNames = new List<string>();
            foreach (var item in values)
            {
                var paramName = $"@p{paramCounter++}";
                parameters.Add(new NpgsqlParameter(paramName, item));
                paramNames.Add(paramName);
            }

            return $"{fieldExpression} IN ({string.Join(", ", paramNames)})";
        }

        // Single value (fallback)
        var singleParamName = $"@p{paramCounter++}";
        parameters.Add(new NpgsqlParameter(singleParamName, value));
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