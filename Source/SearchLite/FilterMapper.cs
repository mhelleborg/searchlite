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
                BinaryExpression { NodeType: ExpressionType.AndAlso or ExpressionType.OrElse } binary => 
                    VisitLogicalBinary(binary),
                BinaryExpression binary when IsComparisonOperator(binary.NodeType) => 
                    VisitComparisonBinary(binary),
                MemberExpression member when member.Type == typeof(bool) =>
                    VisitBooleanMember(member),
                MethodCallExpression method when IsStringNullOrEmptyMethod(method) =>
                    VisitStringNullOrEmptyMethod(method),
                UnaryExpression { NodeType: ExpressionType.Not, Operand: MethodCallExpression method } when IsStringNullOrEmptyMethod(method) =>
                    VisitNegatedStringNullOrEmptyMethod(method),
                MethodCallExpression method when IsSetOperatorMethod(method) =>
                    VisitSetOperatorMethod(method),
                UnaryExpression { NodeType: ExpressionType.Not, Operand: MethodCallExpression method } when IsSetOperatorMethod(method) =>
                    VisitNegatedSetOperatorMethod(method),
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
                   method.Method.Name is nameof(string.IsNullOrEmpty) or nameof(string.IsNullOrWhiteSpace);
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

        private bool IsSetOperatorMethod(MethodCallExpression method)
        {
            // Handle string.Contains
            if (method.Method.DeclaringType == typeof(string) && method.Method.Name == nameof(string.Contains))
                return true;

            // Handle collection.Contains (for IEnumerable<T>.Contains extension method)
            if (method.Method.Name == nameof(Enumerable.Contains) && method.Method.DeclaringType == typeof(Enumerable))
                return true;

            // Handle list/collection.Contains (instance method) - check for Contains method name
            if (method.Method.Name == "Contains")
                return true;

            return false;
        }

        private FilterNode<T> VisitSetOperatorMethod(MethodCallExpression method)
        {
            // Handle string.Contains
            if (method.Method.DeclaringType == typeof(string) && method.Method.Name == nameof(string.Contains))
            {
                if (method.Object is not MemberExpression memberExpression)
                {
                    throw new NotSupportedException("Unsupported string.Contains usage - property must be on the left side");
                }

                var propertyInfo = (PropertyInfo)memberExpression.Member;
                var value = Expression.Lambda(method.Arguments[0]).Compile().DynamicInvoke();

                return new FilterNode<T>.Condition
                {
                    PropertyName = propertyInfo.Name,
                    PropertyType = propertyInfo.PropertyType,
                    Operator = Operator.Contains,
                    Value = value!
                };
            }

            // Handle collection.Contains (both static Enumerable.Contains and instance Contains)
            MemberExpression? targetMember = null;
            object? collectionValue = null;

            if (method.Method.Name == nameof(Enumerable.Contains) && method.Method.DeclaringType == typeof(Enumerable))
            {
                // Static Enumerable.Contains(collection, item)
                if (method.Arguments.Count == 2 && method.Arguments[1] is MemberExpression member)
                {
                    targetMember = member;
                    collectionValue = Expression.Lambda(method.Arguments[0]).Compile().DynamicInvoke();
                }
            }
            else if (method.Method.Name == "Contains" && method.Method.DeclaringType?.FullName == "System.MemoryExtensions")
            {
                // MemoryExtensions.Contains (for Span<T>/ReadOnlySpan<T> operations on arrays)
                if (method.Arguments.Count == 2 && method.Arguments[1] is MemberExpression member)
                {
                    targetMember = member;
                    collectionValue = GetCollectionValue(method.Arguments[0]);
                }
            }
            else if (method.Method.Name == "Contains" && method.Object != null)
            {
                // Instance collection.Contains(item)
                if (method.Arguments.Count == 1 && method.Arguments[0] is MemberExpression member)
                {
                    targetMember = member;
                    collectionValue = Expression.Lambda(method.Object).Compile().DynamicInvoke();
                }
            }
            else if (method.Method.Name == "Contains")
            {
                // Handle extension method calls like array.Contains(x.Property)
                // This is compiled as static method call: Contains(array, x.Property)
                if (method.Arguments.Count == 2 && method.Arguments[1] is MemberExpression member)
                {
                    targetMember = member;
                    collectionValue = GetCollectionValue(method.Arguments[0]);
                }
                // Handle instance method calls like list.Contains(x.Property) 
                else if (method.Arguments.Count == 1 && method.Arguments[0] is MemberExpression member2 && method.Object != null)
                {
                    targetMember = member2;
                    collectionValue = Expression.Lambda(method.Object).Compile().DynamicInvoke();
                }
            }

            if (targetMember == null || collectionValue == null)
            {
                throw new NotSupportedException("Unsupported Contains usage - unable to extract property and collection");
            }

            var targetPropertyInfo = (PropertyInfo)targetMember.Member;

            return new FilterNode<T>.Condition
            {
                PropertyName = targetPropertyInfo.Name,
                PropertyType = targetPropertyInfo.PropertyType,
                Operator = Operator.In,
                Value = collectionValue
            };
        }

        private FilterNode<T> VisitNegatedSetOperatorMethod(MethodCallExpression method)
        {
            var positiveResult = VisitSetOperatorMethod(method);
            if (positiveResult is FilterNode<T>.Condition condition)
            {
                var negatedOperator = condition.Operator switch
                {
                    Operator.Contains => Operator.NotContains,
                    Operator.In => Operator.NotIn,
                    _ => throw new NotSupportedException($"Cannot negate operator {condition.Operator}")
                };

                return new FilterNode<T>.Condition
                {
                    PropertyName = condition.PropertyName,
                    PropertyType = condition.PropertyType,
                    Operator = negatedOperator,
                    Value = condition.Value
                };
            }

            throw new NotSupportedException("Cannot negate non-condition result");
        }

        private static object? GetCollectionValue(Expression expression)
        {
            // Handle MethodCallExpression like op_Implicit(array)
            if (expression is MethodCallExpression methodCall)
            {
                // If it's an implicit conversion from array to span, get the underlying array
                if (methodCall.Method.Name == "op_Implicit" && methodCall.Arguments.Count == 1)
                {
                    return Expression.Lambda(methodCall.Arguments[0]).Compile().DynamicInvoke();
                }
            }
            
            // Default case - try to compile the expression directly
            return Expression.Lambda(expression).Compile().DynamicInvoke();
        }
    }
}