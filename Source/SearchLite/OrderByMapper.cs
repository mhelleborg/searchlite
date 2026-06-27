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
        var body = keySelector.Body;
        // Unwrap conversions inserted for value-type key selectors (e.g. enums, nullable).
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } convert)
        {
            body = convert.Operand;
        }

        if (body is not MemberExpression memberExpression)
        {
            throw new ArgumentException("Only property access expressions are supported", nameof(keySelector));
        }

        return new OrderByNode<T>
        {
            PropertyName = FieldPath.From(memberExpression),
            Direction = direction
        };
    }
}