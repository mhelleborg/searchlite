﻿using Microsoft.Data.Sqlite;

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
}