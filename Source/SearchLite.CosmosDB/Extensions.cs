using Microsoft.Azure.Cosmos;

namespace SearchLite.CosmosDB;

internal static class Extensions
{
    /// <summary>
    /// Joins clause fragments with <c>AND</c> into a <c>WHERE ...</c> string (empty when there are
    /// no clauses).
    /// </summary>
    public static string ToWhereClause(this IReadOnlyList<Clause> clauses)
    {
        if (clauses.Count == 0)
        {
            return string.Empty;
        }

        return "WHERE " + string.Join(" AND ", clauses.Select(c => c.Sql));
    }

    /// <summary>
    /// Binds every parameter referenced by the given clauses onto the query definition.
    /// </summary>
    public static QueryDefinition AddParameters(this QueryDefinition query, IReadOnlyCollection<Clause> clauses)
    {
        foreach (var clause in clauses)
        {
            foreach (var parameter in clause.Parameters)
            {
                query = query.WithParameter(parameter.Name, parameter.Value);
            }
        }

        return query;
    }
}
