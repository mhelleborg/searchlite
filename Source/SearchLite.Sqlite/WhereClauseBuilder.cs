using Microsoft.Data.Sqlite;

namespace SearchLite.Sqlite;

public record Clause
{
    public required string Sql { get; init; }
    public List<SqliteParameter> Parameters { get; init; } = [];
}

public static class WhereClauseBuilder<T>
{
    public static IReadOnlyList<Clause> BuildClauses(List<FilterNode<T>> filters)
    {
        return filters.Select(BuildClause).ToList();
    }

    private static Clause BuildClause(FilterNode<T> filter)
    {
        var paramCounter = 0;
        var parameters = new List<SqliteParameter>();
        var sql = BuildSql(filter, ref paramCounter, parameters);
        return new Clause
        {
            Sql = sql,
            Parameters = parameters
        };
    }

    private static string BuildSql(FilterNode<T> node, ref int paramCounter, List<SqliteParameter> parameters)
    {
        return node switch
        {
            FilterNode<T>.Condition condition => BuildConditionSql(condition, ref paramCounter, parameters),
            FilterNode<T>.Group group => BuildGroupSql(group, ref paramCounter, parameters),
            _ => throw new ArgumentException($"Unsupported node type: {node.GetType()}")
        };
    }

    private static string BuildGroupSql(FilterNode<T>.Group group, ref int paramCounter,
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
            conditions.Add(BuildSql(condition, ref paramCounter, parameters));
        }

        return conditions.Count > 1
            ? $"({string.Join(op, conditions)})"
            : conditions.FirstOrDefault() ?? "1"; // Sqlite uses 1 for TRUE
    }

    private static string BuildConditionSql(FilterNode<T>.Condition condition, ref int paramCounter,
        List<SqliteParameter> parameters)
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

        var sqliteType = GetSqliteType(condition.PropertyType);
        var operatorString = GetOperatorString(condition.Operator);
        var paramName = $"@p{paramCounter++}";

        parameters.Add(new SqliteParameter(paramName, condition.Value));

        // Use json_extract with proper CAST for Sqlite
        return
            $"CAST(json_extract(document, '$.{condition.PropertyName}') AS {sqliteType}) {operatorString} {paramName}";
    }

    private static string GetSqliteType(Type type)
    {
        return type switch
        {
            { } t when t == typeof(int) => "INTEGER",
            { } t when t == typeof(string) => "TEXT",
            { } t when t == typeof(bool) => "INTEGER", // Sqlite doesn't have a native boolean type
            { } t when t == typeof(double) => "REAL",
            { } t when t == typeof(decimal) => "REAL",
            { } t when t == typeof(DateTime) => "TEXT", // Sqlite stores dates as TEXT, REAL, or INTEGER
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
        var fieldExpression = $"json_extract(document, '$.{propertyName}')";
        
        return op switch
        {
            Operator.IsNull => $"{fieldExpression} IS NULL",
            Operator.IsNotNull => $"{fieldExpression} IS NOT NULL",
            _ => throw new NotSupportedException($"Null operator {op} is not supported")
        };
    }

    private static string BuildStringNullOrEmptyCondition(string propertyName, Operator op)
    {
        var fieldExpression = $"CAST(json_extract(document, '$.{propertyName}') AS TEXT)";
        
        return op switch
        {
            Operator.IsNullOrEmpty => $"({fieldExpression} IS NULL OR {fieldExpression} = '')",
            Operator.IsNotNullOrEmpty => $"({fieldExpression} IS NOT NULL AND {fieldExpression} != '')",
            // Use replace to handle various whitespace characters like .NET's IsNullOrWhiteSpace
            Operator.IsNullOrWhiteSpace => $"({fieldExpression} IS NULL OR trim(replace(replace(replace({fieldExpression}, char(9), ' '), char(10), ' '), char(13), ' ')) = '')",
            Operator.IsNotNullOrWhiteSpace => $"({fieldExpression} IS NOT NULL AND trim(replace(replace(replace({fieldExpression}, char(9), ' '), char(10), ' '), char(13), ' ')) != '')",
            _ => throw new NotSupportedException($"String operator {op} is not supported")
        };
    }

    private static bool IsSetOperator(Operator op)
    {
        return op is Operator.Contains or Operator.NotContains or Operator.In or Operator.NotIn;
    }

    private static string BuildSetCondition(FilterNode<T>.Condition condition, ref int paramCounter, List<SqliteParameter> parameters)
    {
        var fieldExpression = condition.PropertyType == typeof(string) 
            ? $"CAST(json_extract(document, '$.{condition.PropertyName}') AS TEXT)"
            : $"CAST(json_extract(document, '$.{condition.PropertyName}') AS {GetSqliteType(condition.PropertyType)})";

        return condition.Operator switch
        {
            Operator.Contains => BuildContainsCondition(fieldExpression, condition.Value, ref paramCounter, parameters),
            Operator.NotContains => $"NOT ({BuildContainsCondition(fieldExpression, condition.Value, ref paramCounter, parameters)})",
            Operator.In => BuildInCondition(fieldExpression, condition.Value, ref paramCounter, parameters),
            Operator.NotIn => $"NOT ({BuildInCondition(fieldExpression, condition.Value, ref paramCounter, parameters)})",
            _ => throw new NotSupportedException($"Set operator {condition.Operator} is not supported")
        };
    }

    private static string BuildContainsCondition(string fieldExpression, object value, ref int paramCounter, List<SqliteParameter> parameters)
    {
        var paramName = $"@p{paramCounter++}";
        parameters.Add(new SqliteParameter(paramName, $"%{value}%"));
        return $"{fieldExpression} LIKE {paramName}";
    }

    private static string BuildInCondition(string fieldExpression, object value, ref int paramCounter, List<SqliteParameter> parameters)
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
                return "1 = 0"; // Always false condition
            }

            var paramNames = new List<string>();
            foreach (var item in values)
            {
                var paramName = $"@p{paramCounter++}";
                parameters.Add(new SqliteParameter(paramName, item));
                paramNames.Add(paramName);
            }

            return $"{fieldExpression} IN ({string.Join(", ", paramNames)})";
        }

        // Single value (fallback)
        var singleParamName = $"@p{paramCounter++}";
        parameters.Add(new SqliteParameter(singleParamName, value));
        return $"{fieldExpression} = {singleParamName}";
    }
}