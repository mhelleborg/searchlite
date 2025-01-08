using Npgsql;

namespace SearchLite.Postgres;

internal static class Extensions
{
    public static string ToWhereClause(this IReadOnlyList<Clause> clauses)
    {
        if (clauses.Count == 0)
        {
            return string.Empty;
        }

        return "WHERE " + string.Join(" AND ", clauses.Select(c => c.Sql));
    }
    
    public static void AddParameters(this NpgsqlCommand command, IReadOnlyCollection<Clause> clauses)
    {
        foreach (var clause in clauses)
        {
            foreach (var parameter in clause.Parameters)
            {
                command.Parameters.Add(parameter);
            }
        }
    }
}