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
        return visitor.Result ?? throw new InvalidOperationException("Failed to map expression");
    }

    class FilterExpressionVisitor<T> : ExpressionVisitor
    {
        public FilterNode<T>? Result { get; private set; }

        protected override Expression VisitLambda<TDelegate>(Expression<TDelegate> node)
        {
            Result = VisitExpression(node.Body);
            return node;
        }

        private FilterNode<T> VisitExpression(Expression expression)
        {
            return expression switch
            {
                BinaryExpression binary when binary.NodeType is ExpressionType.AndAlso or ExpressionType.OrElse => 
                    VisitLogicalBinary(binary),
                BinaryExpression binary when IsComparisonOperator(binary.NodeType) => 
                    VisitComparisonBinary(binary),
                MemberExpression member when member.Type == typeof(bool) =>
                    VisitBooleanMember(member),
                MethodCallExpression method when IsStringNullOrEmptyMethod(method) =>
                    VisitStringNullOrEmptyMethod(method),
                UnaryExpression { NodeType: ExpressionType.Not, Operand: MethodCallExpression method } when IsStringNullOrEmptyMethod(method) =>
                    VisitNegatedStringNullOrEmptyMethod(method),
                ConstantExpression { Value: true } => 
                    new FilterNode<T>.Group { Operator = LogicalOperator.And, Conditions = new List<FilterNode<T>>() },
                _ => throw new NotSupportedException($"Expression type {expression.NodeType} is not supported")
            };
        }

        private FilterNode<T> VisitBooleanMember(MemberExpression member)
        {
            var propertyInfo = (PropertyInfo)member.Member;
            return new FilterNode<T>.Condition
            {
                PropertyName = propertyInfo.Name,
                PropertyType = propertyInfo.PropertyType,
                Operator = Operator.Equal,
                Value = true
            };
        }

        private FilterNode<T> VisitLogicalBinary(BinaryExpression node)
        {
            var left = VisitExpression(node.Left);
            var right = VisitExpression(node.Right);

            return new FilterNode<T>.Group
            {
                Operator = node.NodeType == ExpressionType.AndAlso ? LogicalOperator.And : LogicalOperator.Or,
                Conditions = [left, right]
            };
        }

        private FilterNode<T> VisitComparisonBinary(BinaryExpression node)
        {
            var memberExpression = GetMemberExpression(node);
            if (memberExpression == null)
            {
                CheckForUnsupportedOperations(node.Left);
                throw new NotSupportedException($"Unable to extract member from expression");
            }

            var propertyInfo = (PropertyInfo)memberExpression.Member;
            var value = Expression.Lambda(node.Right).Compile().DynamicInvoke();

            // Handle null comparisons specially
            if (value == null)
            {
                var nullOperator = node.NodeType switch
                {
                    ExpressionType.Equal => Operator.IsNull,
                    ExpressionType.NotEqual => Operator.IsNotNull,
                    _ => throw new NotSupportedException($"Null comparison with operator {node.NodeType} is not supported")
                };

                return new FilterNode<T>.Condition
                {
                    PropertyName = propertyInfo.Name,
                    PropertyType = propertyInfo.PropertyType,
                    Operator = nullOperator,
                    Value = true // Use true as a placeholder since we don't need the actual value for null checks
                };
            }

            return new FilterNode<T>.Condition
            {
                PropertyName = propertyInfo.Name,
                PropertyType = propertyInfo.PropertyType,
                Operator = GetOperator(node.NodeType),
                Value = value!
            };
        }

        private static bool IsComparisonOperator(ExpressionType nodeType)
        {
            return nodeType is ExpressionType.Equal or ExpressionType.NotEqual or 
                   ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual or
                   ExpressionType.LessThan or ExpressionType.LessThanOrEqual;
        }

        private static void CheckForUnsupportedOperations(Expression expression)
        {
            if (expression is BinaryExpression binaryExpr && 
                binaryExpr.NodeType is ExpressionType.Add or ExpressionType.Subtract or 
                ExpressionType.Multiply or ExpressionType.Divide)
            {
                throw new NotSupportedException($"Operator {binaryExpr.NodeType} is not supported");
            }
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

        private bool IsStringNullOrEmptyMethod(MethodCallExpression method)
        {
            return method.Method.DeclaringType == typeof(string) &&
                   (method.Method.Name == nameof(string.IsNullOrEmpty) ||
                    method.Method.Name == nameof(string.IsNullOrWhiteSpace));
        }

        private FilterNode<T> VisitStringNullOrEmptyMethod(MethodCallExpression method)
        {
            if (method.Arguments.Count != 1 || method.Arguments[0] is not MemberExpression memberExpression)
            {
                throw new NotSupportedException($"Unsupported {method.Method.Name} usage");
            }

            var propertyInfo = (PropertyInfo)memberExpression.Member;
            var operatorType = method.Method.Name == nameof(string.IsNullOrEmpty) 
                ? Operator.IsNullOrEmpty 
                : Operator.IsNullOrWhiteSpace;

            return new FilterNode<T>.Condition
            {
                PropertyName = propertyInfo.Name,
                PropertyType = propertyInfo.PropertyType,
                Operator = operatorType,
                Value = true
            };
        }

        private FilterNode<T> VisitNegatedStringNullOrEmptyMethod(MethodCallExpression method)
        {
            if (method.Arguments.Count != 1 || method.Arguments[0] is not MemberExpression memberExpression)
            {
                throw new NotSupportedException($"Unsupported negated {method.Method.Name} usage");
            }

            var propertyInfo = (PropertyInfo)memberExpression.Member;
            var operatorType = method.Method.Name == nameof(string.IsNullOrEmpty) 
                ? Operator.IsNotNullOrEmpty 
                : Operator.IsNotNullOrWhiteSpace;

            return new FilterNode<T>.Condition
            {
                PropertyName = propertyInfo.Name,
                PropertyType = propertyInfo.PropertyType,
                Operator = operatorType,
                Value = true
            };
        }
    }
}