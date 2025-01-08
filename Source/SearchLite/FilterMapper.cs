using System.Linq.Expressions;
using System.Reflection;

namespace SearchLite;

public static class FilterMapper
{
    /// <summary>
    /// Maps a predicate expression to a FilterNode
    /// </summary>
    /// <param name="predicate"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static FilterNode<T> Map<T>(Expression<Func<T, bool>> predicate)
    {
        var visitor = new FilterExpressionVisitor<T>();
        visitor.Visit(predicate);
        return visitor.Result;
    }

    class FilterExpressionVisitor<T> : ExpressionVisitor
    {
        private readonly Stack<FilterNode<T>> _nodes = new();

        public FilterNode<T> Result => _nodes.Count == 1
            ? _nodes.Peek()
            : new FilterNode<T>.Group { Operator = LogicalOperator.And, Conditions = _nodes.Reverse().ToList() };

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType is ExpressionType.AndAlso or ExpressionType.OrElse)
            {
                Visit(node.Left);
                Visit(node.Right);

                var right = _nodes.Pop();
                var left = _nodes.Pop();

                _nodes.Push(new FilterNode<T>.Group
                    {
                        Operator = node.NodeType == ExpressionType.AndAlso ? LogicalOperator.And : LogicalOperator.Or,
                        Conditions = [left, right]
                    }
                );
            }
            else
            {
                var memberExpression = GetMemberExpression(node);
                if (memberExpression == null) return node;

                var propertyInfo = (PropertyInfo)memberExpression.Member;
                var value = Expression.Lambda(node.Right).Compile().DynamicInvoke();

                _nodes.Push(new FilterNode<T>.Condition
                    {
                        PropertyName = propertyInfo.Name,
                        PropertyType = propertyInfo.PropertyType,
                        Operator = GetOperator(node.NodeType),
                        Value = value!
                    }
                );
            }

            return node;
        }

        private static MemberExpression? GetMemberExpression(BinaryExpression node)
        {
            if (node.Left is MemberExpression memberExpression)
                return memberExpression;

            if (node.Left is MethodCallExpression methodCall &&
                methodCall.Object is MemberExpression memberExpr)
                return memberExpr;

            return null;
        }

        private static Operator GetOperator(ExpressionType type) => type switch
        {
            ExpressionType.Equal => Operator.Equal,
            ExpressionType.NotEqual => Operator.NotEqual,
            ExpressionType.GreaterThan => Operator.GreaterThan,
            ExpressionType.GreaterThanOrEqual => Operator.GreaterThanOrEqual,
            ExpressionType.LessThan => Operator.LessThan,
            ExpressionType.LessThanOrEqual => Operator.LessThanOrEqual,
            _ => throw new NotSupportedException($"Operator {type} is not supported")
        };
    }
}