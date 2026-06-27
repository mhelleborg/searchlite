using System.Linq.Expressions;
using System.Reflection;

namespace SearchLite;

/// <summary>
/// Helpers for turning a parameter-rooted member access expression (e.g. <c>d =&gt; d.Author.Name</c>)
/// into a dot-separated field path ("Author.Name"), and for splitting such paths back into segments.
/// </summary>
public static class FieldPath
{
    /// <summary>
    /// Builds a dot-separated path from a member access chain rooted at the lambda parameter.
    /// Throws if the expression is not rooted at the parameter (e.g. a captured variable).
    /// </summary>
    public static string From(MemberExpression member)
    {
        if (!TryFrom(member, out var path))
        {
            throw new NotSupportedException(
                $"Expression '{member}' is not a document field access. Only member access rooted at the document parameter is supported.");
        }

        return path;
    }

    /// <summary>
    /// Attempts to build a dot-separated path from a member access chain rooted at the lambda
    /// parameter. Returns false when the chain is rooted in something other than the parameter
    /// (for example a captured local/field), which callers use to distinguish a document field
    /// from a value to evaluate.
    /// </summary>
    public static bool TryFrom(Expression? expression, out string path)
    {
        path = string.Empty;

        if (expression is UnaryExpression { NodeType: ExpressionType.Convert } convert)
        {
            expression = convert.Operand;
        }

        if (expression is not MemberExpression member)
        {
            return false;
        }

        var segments = new Stack<string>();
        Expression? current = member;
        while (current is MemberExpression m && m.Member is PropertyInfo property)
        {
            segments.Push(property.Name);
            current = m.Expression;
        }

        if (current is not ParameterExpression)
        {
            return false;
        }

        path = string.Join(".", segments);
        return true;
    }

    /// <summary>
    /// Splits a dot-separated field path into its segments.
    /// </summary>
    public static string[] Split(string path) => path.Split('.');
}
