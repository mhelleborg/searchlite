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
}