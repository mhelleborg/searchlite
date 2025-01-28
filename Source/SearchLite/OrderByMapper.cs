using System.Linq.Expressions;
using System.Reflection;

namespace SearchLite;

public static class OrderByMapper
{
    /// <summary>
    /// Maps a key selector expression to an OrderByNode
    /// </summary>
    public static OrderByNode<T> Map<T, TKey>(Expression<Func<T, TKey>> keySelector, SortDirection direction)
    {
        if (keySelector.Body is not MemberExpression memberExpression)
        {
            throw new ArgumentException("Only simple property access expressions are supported", nameof(keySelector));
        }

        var propertyInfo = (PropertyInfo)memberExpression.Member;

        return new OrderByNode<T>
        {
            PropertyName = propertyInfo.Name,
            Direction = direction
        };
    }
}