using System.Linq.Expressions;

namespace SearchLite;

public class SearchOptions
{
    /// <summary>
    /// Max number of results to return
    /// </summary>
    public int MaxResults { get; init; } = 100;

    /// <summary>
    /// Score threshold to include in the results
    /// The score for a document will not be the same across providers, as 
    /// </summary>
    public float MinScore { get; init; } = 0.0f;

    /// <summary>
    /// If set to false, will only return the document id and score
    /// </summary>
    public bool IncludeRawDocument { get; set; } = true;
    
    /// <summary>
    /// Flag if the search should include partial matches. If not, the query will only match on the full query
    /// </summary>
    public bool IncludePartialMatches { get; init; } = true;
}

public class SearchRequest<T>
{
    /// <summary>
    /// Full text query to search for
    /// </summary>
    public string? Query { get; init; }

    /// <summary>
    /// List of filters to apply to the search
    /// These are applied as an AND operation, meaning all filters must match for a document to be included in the results
    /// </summary>
    public List<FilterNode<T>> Filters { get; } = [];

    /// <summary>
    /// List of ordering clauses to apply to the search results
    /// Orders are applied in sequence, with later orders used as tiebreakers
    /// If empty, results are ordered by search score in descending order
    /// </summary>
    public List<OrderByNode<T>> OrderBys { get; } = [];

    /// <summary>
    /// Options for the search
    /// </summary>
    public SearchOptions Options { get; init; } = new();



    /// <summary>
    /// Add a filter from an expression
    /// </summary>
    public SearchRequest<T> Where(Expression<Func<T, bool>> predicate)
    {
        Filters.Add(FilterMapper.Map(predicate));
        return this;
    }

    public SearchRequest<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector, SortDirection direction)
    {
        OrderBys.Add(OrderByMapper.Map(keySelector, direction));
        return this;
    }

    /// <summary>
    /// Add an ascending order clause
    /// </summary>
    public SearchRequest<T> OrderByAscending<TKey>(Expression<Func<T, TKey>> keySelector) =>
        OrderBy(keySelector, SortDirection.Ascending);

    /// <summary>
    /// Add a descending order clause
    /// </summary>
    public SearchRequest<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector) =>
        OrderBy(keySelector, SortDirection.Descending);
}