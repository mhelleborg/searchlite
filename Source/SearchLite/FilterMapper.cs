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
                UnaryExpression { NodeType: ExpressionType.Not, Operand: MemberExpression member } when member.Type == typeof(bool) =>
                    VisitNegatedBooleanMember(member),
                MethodCallExpression method when IsStringNullOrEmptyMethod(method) =>
                    VisitStringNullOrEmptyMethod(method),
                UnaryExpression { NodeType: ExpressionType.Not, Operand: MethodCallExpression method } when IsStringNullOrEmptyMethod(method) =>
                    VisitNegatedStringNullOrEmptyMethod(method),
                MethodCallExpression method when IsSetOperatorMethod(method) =>
                    VisitSetOperatorMethod(method),
                UnaryExpression { NodeType: ExpressionType.Not, Operand: MethodCallExpression method } when IsSetOperatorMethod(method) =>
                    VisitNegatedSetOperatorMethod(method),
                MethodCallExpression method when IsStringOperatorMethod(method) =>
                    VisitStringOperatorMethod(method),
                UnaryExpression { NodeType: ExpressionType.Not, Operand: MethodCallExpression method } when IsStringOperatorMethod(method) =>
                    VisitNegatedStringOperatorMethod(method),
                MethodCallExpression method when IsComparisonMethod(method) =>
                    VisitComparisonMethod(method),
                UnaryExpression { NodeType: ExpressionType.Not, Operand: MethodCallExpression method } when IsComparisonMethod(method) =>
                    VisitNegatedComparisonMethod(method),
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
                PropertyType = propertyInfo.PropertyType!,
                Operator = Operator.Equal,
                Value = true
            };
        }

        private FilterNode<T> VisitNegatedBooleanMember(MemberExpression member)
        {
            var propertyInfo = (PropertyInfo)member.Member;
            return new FilterNode<T>.Condition
            {
                PropertyName = propertyInfo.Name,
                PropertyType = propertyInfo.PropertyType!,
                Operator = Operator.Equal,
                Value = false
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
            if (IsMethodCallWithResult(node.Left))
            {
                return VisitMethodCallComparison(node);
            }

            var (memberExpression, isLeftSide) = GetMemberExpression(node);
            if (memberExpression == null)
            {
                CheckForUnsupportedOperations(node.Left);
                throw new NotSupportedException($"Unable to extract member from expression");
            }

            var propertyInfo = (PropertyInfo)memberExpression.Member;
            // Extract value from the opposite side of where the member expression is
            var valueExpression = isLeftSide ? node.Right : node.Left;
            var value = Expression.Lambda(valueExpression).Compile().DynamicInvoke();

            // Handle enum serialization based on the property's type (not the value's type)
            if (value != null)
            {
                var propertyType = propertyInfo.PropertyType;
                var underlyingPropertyType = propertyType != null ? (Nullable.GetUnderlyingType(propertyType) ?? propertyType) : null;
                
                if (underlyingPropertyType != null && underlyingPropertyType.IsEnum)
                {
                    var format = EnumSerializationAnalyzer.GetPropertyFormat(propertyInfo);
                    
                    if (format == EnumSerializationFormat.String)
                    {
                        // Convert the integer value to enum then to string
                        var enumValue = Enum.ToObject(underlyingPropertyType, value);
                        value = enumValue.ToString();
                    }
                    else
                    {
                        // Keep as integer (ensure it's the right integer type)
                        value = Convert.ChangeType(value, underlyingPropertyType.GetEnumUnderlyingType());
                    }
                }
            }

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
                    PropertyType = propertyInfo.PropertyType!,
                    Operator = nullOperator,
                    Value = true // Use true as a placeholder since we don't need the actual value for null checks
                };
            }

            // When member is on the right side and constant on left, we need to flip comparison operators
            var operatorType = GetOperator(node.NodeType, !isLeftSide);

            return new FilterNode<T>.Condition
            {
                PropertyName = propertyInfo.Name,
                PropertyType = propertyInfo.PropertyType!,
                Operator = operatorType,
                Value = value!
            };
        }

        private FilterNode<T> VisitMethodCallComparison(BinaryExpression node)
        {
            var methodCall = (MethodCallExpression)node.Left;
            
            if (methodCall.Object is not MemberExpression memberExpression)
            {
                throw new NotSupportedException($"{methodCall.Method.Name} method must be called on a property");
            }

            var propertyInfo = (PropertyInfo)memberExpression.Member;
            var comparisonResult = Expression.Lambda(node.Right).Compile().DynamicInvoke();

            return methodCall.Method.Name switch
            {
                nameof(IComparable.CompareTo) or "CompareTo" => HandleCompareToComparison(methodCall, propertyInfo, node.NodeType, comparisonResult),
                nameof(object.ToString) => HandleToStringComparison(propertyInfo, node.NodeType, comparisonResult),
                _ => throw new NotSupportedException($"Method {methodCall.Method.Name} is not supported in binary comparisons")
            };
        }

        private FilterNode<T> HandleCompareToComparison(MethodCallExpression methodCall, PropertyInfo propertyInfo, 
            ExpressionType nodeType, object? comparisonResult)
        {
            var compareValue = Expression.Lambda(methodCall.Arguments[0]).Compile().DynamicInvoke();

            // Handle enum serialization based on the property's serialization format
            if (compareValue != null)
            {
                var valueType = compareValue.GetType();
                var underlyingType = Nullable.GetUnderlyingType(valueType) ?? valueType;
                
                if (underlyingType.IsEnum)
                {
                    var format = EnumSerializationAnalyzer.GetPropertyFormat(propertyInfo);
                    if (format == EnumSerializationFormat.String)
                    {
                        compareValue = compareValue.ToString();
                    }
                    else
                    {
                        compareValue = Convert.ChangeType(compareValue, underlyingType.GetEnumUnderlyingType());
                    }
                }
            }

            // CompareTo returns: < 0 if less, 0 if equal, > 0 if greater
            // We support: x.CompareTo(y) == 0, x.CompareTo(y) != 0, x.CompareTo(y) > 0, etc.
            
            if (comparisonResult is not int result)
            {
                throw new NotSupportedException("CompareTo comparisons must be against integer constants");
            }

            var operatorType = (nodeType, result) switch
            {
                (ExpressionType.Equal, 0) => Operator.Equal,
                (ExpressionType.NotEqual, 0) => Operator.NotEqual,
                (ExpressionType.GreaterThan, 0) => Operator.GreaterThan,
                (ExpressionType.GreaterThanOrEqual, 0) => Operator.GreaterThanOrEqual,
                (ExpressionType.LessThan, 0) => Operator.LessThan,
                (ExpressionType.LessThanOrEqual, 0) => Operator.LessThanOrEqual,
                _ => throw new NotSupportedException($"CompareTo with operator {nodeType} and result {result} is not supported. Use comparisons against 0.")
            };

            return new FilterNode<T>.Condition
            {
                PropertyName = propertyInfo.Name,
                PropertyType = propertyInfo.PropertyType!,
                Operator = operatorType,
                Value = compareValue!
            };
        }

        private FilterNode<T> HandleToStringComparison(PropertyInfo propertyInfo, ExpressionType nodeType, object? comparisonResult)
        {
            if (comparisonResult is not string stringValue)
            {
                throw new NotSupportedException("ToString comparisons must be against string constants");
            }

            var operatorType = nodeType switch
            {
                ExpressionType.Equal => Operator.Equal,
                ExpressionType.NotEqual => Operator.NotEqual,
                _ => throw new NotSupportedException($"ToString with operator {nodeType} is not supported. Use == or != only.")
            };

            return new FilterNode<T>.Condition
            {
                PropertyName = propertyInfo.Name,
                PropertyType = propertyInfo.PropertyType!,
                Operator = operatorType,
                Value = stringValue
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

        private static (MemberExpression? memberExpression, bool isLeftSide) GetMemberExpression(BinaryExpression node)
        {
            // Check left side first
            if (node.Left is MemberExpression leftMemberExpression)
                return (leftMemberExpression, true);

            // Handle conversion expressions (often used with enums and numeric types)
            if (node.Left is UnaryExpression leftUnary && leftUnary.NodeType == ExpressionType.Convert && 
                leftUnary.Operand is MemberExpression leftConvertedMember)
                return (leftConvertedMember, true);

            if (node.Left is MethodCallExpression leftMethodCall &&
                leftMethodCall.Object is MemberExpression leftMemberExpr)
                return (leftMemberExpr, true);

            // Check right side
            if (node.Right is MemberExpression rightMemberExpression)
                return (rightMemberExpression, false);

            // Handle conversion expressions (often used with enums and numeric types)
            if (node.Right is UnaryExpression rightUnary && rightUnary.NodeType == ExpressionType.Convert && 
                rightUnary.Operand is MemberExpression rightConvertedMember)
                return (rightConvertedMember, false);

            if (node.Right is MethodCallExpression rightMethodCall &&
                rightMethodCall.Object is MemberExpression rightMemberExpr)
                return (rightMemberExpr, false);

            return (null, true);
        }

        private static bool IsCompareToMethodCall(Expression expression)
        {
            return expression is MethodCallExpression { Method.Name: nameof(IComparable.CompareTo) };
        }

        private static bool IsMethodCallWithResult(Expression expression)
        {
            return expression is MethodCallExpression { Method.Name: nameof(IComparable.CompareTo) or
                nameof(ToString)
            };
        }

        private static Operator GetOperator(ExpressionType type, bool flipComparison = false) => (type, flipComparison) switch
        {
            (ExpressionType.Equal, _) => Operator.Equal,
            (ExpressionType.NotEqual, _) => Operator.NotEqual,
            (ExpressionType.GreaterThan, false) => Operator.GreaterThan,
            (ExpressionType.GreaterThan, true) => Operator.LessThan,
            (ExpressionType.GreaterThanOrEqual, false) => Operator.GreaterThanOrEqual,
            (ExpressionType.GreaterThanOrEqual, true) => Operator.LessThanOrEqual,
            (ExpressionType.LessThan, false) => Operator.LessThan,
            (ExpressionType.LessThan, true) => Operator.GreaterThan,
            (ExpressionType.LessThanOrEqual, false) => Operator.LessThanOrEqual,
            (ExpressionType.LessThanOrEqual, true) => Operator.GreaterThanOrEqual,
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
                PropertyType = propertyInfo.PropertyType!,
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
                PropertyType = propertyInfo.PropertyType!,
                Operator = operatorType,
                Value = true
            };
        }

        private bool IsSetOperatorMethod(MethodCallExpression method)
        {
            // Handle collection.Contains (for IEnumerable<T>.Contains extension method)
            if (method.Method.Name == nameof(Enumerable.Contains) && method.Method.DeclaringType == typeof(Enumerable))
                return true;

            // Handle list/collection.Contains (instance method) - but exclude string.Contains
            if (method.Method.Name == "Contains" && method.Method.DeclaringType != typeof(string))
                return true;

            return false;
        }

        private bool IsStringOperatorMethod(MethodCallExpression method)
        {
            if (method.Method.DeclaringType != typeof(string))
                return false;

            return method.Method.Name is 
                nameof(string.Contains) or
                nameof(string.StartsWith) or
                nameof(string.EndsWith);
        }

        private bool IsComparisonMethod(MethodCallExpression method)
        {
            return method.Method.Name is 
                nameof(object.Equals) or
                nameof(IComparable.CompareTo) or
                "CompareTo" or // Handle both generic and non-generic CompareTo
                nameof(object.ToString);
        }

        private FilterNode<T> VisitSetOperatorMethod(MethodCallExpression method)
        {
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

        private FilterNode<T> VisitStringOperatorMethod(MethodCallExpression method)
        {
            if (method.Object is not MemberExpression memberExpression)
            {
                throw new NotSupportedException($"Unsupported {method.Method.Name} usage - property must be on the left side");
            }

            var propertyInfo = (PropertyInfo)memberExpression.Member;
            var value = Expression.Lambda(method.Arguments[0]).Compile().DynamicInvoke();

            // Determine if case-insensitive by checking if StringComparison parameter is provided
            var isIgnoreCase = false;
            if (method.Arguments.Count > 1 && method.Arguments[1] is ConstantExpression constantExpr)
            {
                if (constantExpr.Value is StringComparison comparison)
                {
                    isIgnoreCase = comparison is StringComparison.OrdinalIgnoreCase or 
                                                StringComparison.CurrentCultureIgnoreCase or 
                                                StringComparison.InvariantCultureIgnoreCase;
                }
            }

            var operatorType = method.Method.Name switch
            {
                nameof(string.Contains) => isIgnoreCase ? Operator.ContainsIgnoreCase : Operator.Contains,
                nameof(string.StartsWith) => isIgnoreCase ? Operator.StartsWithIgnoreCase : Operator.StartsWith,
                nameof(string.EndsWith) => isIgnoreCase ? Operator.EndsWithIgnoreCase : Operator.EndsWith,
                _ => throw new NotSupportedException($"String operator {method.Method.Name} is not supported")
            };

            return new FilterNode<T>.Condition
            {
                PropertyName = propertyInfo.Name,
                PropertyType = propertyInfo.PropertyType!,
                Operator = operatorType,
                Value = value!
            };
        }

        private FilterNode<T> VisitNegatedStringOperatorMethod(MethodCallExpression method)
        {
            var positiveResult = VisitStringOperatorMethod(method);
            if (positiveResult is FilterNode<T>.Condition condition)
            {
                var negatedOperator = condition.Operator switch
                {
                    Operator.Contains => Operator.NotContains,
                    Operator.ContainsIgnoreCase => Operator.NotContainsIgnoreCase,
                    Operator.StartsWith => Operator.NotStartsWith,
                    Operator.StartsWithIgnoreCase => Operator.NotStartsWithIgnoreCase,
                    Operator.EndsWith => Operator.NotEndsWith,
                    Operator.EndsWithIgnoreCase => Operator.NotEndsWithIgnoreCase,
                    _ => throw new NotSupportedException($"Cannot negate string operator {condition.Operator}")
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

        private FilterNode<T> VisitComparisonMethod(MethodCallExpression method)
        {
            MemberExpression? memberExpression = null;
            object? value = null;

            // Case 1: property.Equals(constant) - x.Age.Equals(30)
            if (method.Object is MemberExpression objMember)
            {
                memberExpression = objMember;
                value = Expression.Lambda(method.Arguments[0]).Compile().DynamicInvoke();
            }
            // Case 2: constant.Equals(property) - 30.Equals(x.Age)
            else if (method.Arguments.Count > 0 && method.Arguments[0] is MemberExpression argMember)
            {
                memberExpression = argMember;
                value = Expression.Lambda(method.Object!).Compile().DynamicInvoke();
            }

            // Handle enum serialization based on the property's serialization format
            if (memberExpression != null && value != null)
            {
                var enumPropertyInfo = (PropertyInfo)memberExpression.Member;
                var valueType = value.GetType();
                var underlyingType = Nullable.GetUnderlyingType(valueType) ?? valueType;
                
                if (underlyingType.IsEnum)
                {
                    var format = EnumSerializationAnalyzer.GetPropertyFormat(enumPropertyInfo);
                    if (format == EnumSerializationFormat.String)
                    {
                        value = value.ToString();
                    }
                    else
                    {
                        value = Convert.ChangeType(value, underlyingType.GetEnumUnderlyingType());
                    }
                }
            }

            if (memberExpression == null)
            {
                throw new NotSupportedException($"Unsupported {method.Method.Name} usage - one operand must be a property");
            }

            var propertyInfo = (PropertyInfo)memberExpression.Member;

            return method.Method.Name switch
            {
                nameof(object.Equals) => new FilterNode<T>.Condition
                {
                    PropertyName = propertyInfo.Name,
                    PropertyType = propertyInfo.PropertyType!,
                    Operator = Operator.Equal,
                    Value = value!
                },
                nameof(IComparable.CompareTo) or "CompareTo" => throw new NotSupportedException(
                    "CompareTo method should be used with comparison operators (e.g., x.CompareTo(y) == 0, x.CompareTo(y) > 0)"),
                nameof(object.ToString) => throw new NotSupportedException(
                    "ToString method should be used with comparison operators (e.g., x.ToString() == \"value\")"),
                _ => throw new NotSupportedException($"Comparison method {method.Method.Name} is not supported")
            };
        }

        private FilterNode<T> VisitNegatedComparisonMethod(MethodCallExpression method)
        {
            if (method.Method.Name == nameof(object.Equals))
            {
                MemberExpression? memberExpression = null;
                object? value = null;

                // Case 1: property.Equals(constant) - x.Age.Equals(30)
                if (method.Object is MemberExpression objMember)
                {
                    memberExpression = objMember;
                    value = Expression.Lambda(method.Arguments[0]).Compile().DynamicInvoke();
                }
                // Case 2: constant.Equals(property) - 30.Equals(x.Age)
                else if (method.Arguments.Count > 0 && method.Arguments[0] is MemberExpression argMember)
                {
                    memberExpression = argMember;
                    value = Expression.Lambda(method.Object!).Compile().DynamicInvoke();
                }

                // Handle enum serialization based on the property's serialization format
                if (memberExpression != null && value != null)
                {
                    var enumPropertyInfo = (PropertyInfo)memberExpression.Member;
                    var valueType = value.GetType();
                    var underlyingType = Nullable.GetUnderlyingType(valueType) ?? valueType;
                    
                    if (underlyingType.IsEnum)
                    {
                        var format = EnumSerializationAnalyzer.GetPropertyFormat(enumPropertyInfo);
                        if (format == EnumSerializationFormat.String)
                        {
                            value = value.ToString();
                        }
                        else
                        {
                            value = Convert.ChangeType(value, underlyingType.GetEnumUnderlyingType());
                        }
                    }
                }

                if (memberExpression == null)
                {
                    throw new NotSupportedException($"Unsupported negated {method.Method.Name} usage - one operand must be a property");
                }

                var propertyInfo = (PropertyInfo)memberExpression.Member;

                return new FilterNode<T>.Condition
                {
                    PropertyName = propertyInfo.Name,
                    PropertyType = propertyInfo.PropertyType!,
                    Operator = Operator.NotEqual,
                    Value = value!
                };
            }

            throw new NotSupportedException($"Cannot negate comparison method {method.Method.Name}");
        }
    }
}