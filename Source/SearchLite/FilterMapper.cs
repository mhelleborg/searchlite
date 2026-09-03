using System.Collections;
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
    public static FilterNode<T> Map<T>(Expression<Func<T, bool>> predicate)
    {
        var visitor = new FilterExpressionVisitor<T>(predicate.Parameters[0]);
        return visitor.VisitExpression(predicate.Body);
    }

    /// <summary>
    /// A document field referenced by a filter expression: its dot-separated path relative to
    /// the current scope root (empty for the root itself), its CLR type, and the leaf
    /// <see cref="PropertyInfo"/> when the path ends in a property.
    /// </summary>
    private readonly record struct FieldRef(string Path, Type Type, PropertyInfo? Property);

    /// <summary>
    /// A <c>Count</c>/<c>Length</c> access on a collection field, with the optional element
    /// predicate of <c>Enumerable.Count(source, predicate)</c>.
    /// </summary>
    private readonly record struct CountRef(FieldRef Collection, LambdaExpression? Predicate);

    /// <summary>
    /// Translates the body of one lambda. The lambda parameter is the scope root: for the
    /// top-level predicate it is the document, inside a collection predicate it is the element.
    /// </summary>
    private sealed class FilterExpressionVisitor<T>
    {
        private readonly ParameterExpression _parameter;

        public FilterExpressionVisitor(ParameterExpression parameter)
        {
            _parameter = parameter;
        }

        public FilterNode<T> VisitExpression(Expression expression)
        {
            switch (expression)
            {
                case BinaryExpression { NodeType: ExpressionType.AndAlso or ExpressionType.OrElse } logical:
                    return VisitLogicalBinary(logical);
                case BinaryExpression binary when IsComparisonOperator(binary.NodeType):
                    return VisitComparisonBinary(binary);
                case UnaryExpression { NodeType: ExpressionType.Not } not:
                    return VisitNot(not.Operand);
                case ConstantExpression { Value: bool constant }:
                    return ConstantNode(constant);
            }

            if (TryVisitBoolean(expression, negated: false, out var boolean))
            {
                return boolean;
            }

            if (expression is MethodCallExpression method)
            {
                return VisitMethodCall(method);
            }

            // A boolean expression that does not touch the document at all (e.g. a captured flag
            // in `d => includeAll || d.Views > 10`) is evaluated once and folded to a constant.
            if (expression.Type == typeof(bool) && !ReferencesParameter(expression))
            {
                return ConstantNode((bool)Evaluate(expression)!);
            }

            throw new NotSupportedException($"Expression type {expression.NodeType} is not supported");
        }

        private FilterNode<T> VisitMethodCall(MethodCallExpression method)
        {
            if (IsStringNullOrEmptyMethod(method))
            {
                return VisitStringNullOrEmptyMethod(method);
            }

            if (IsQuantifierMethod(method))
            {
                return VisitQuantifierMethod(method);
            }

            if (IsSetOperatorMethod(method))
            {
                return VisitSetOperatorMethod(method);
            }

            if (IsStringOperatorMethod(method))
            {
                return VisitStringOperatorMethod(method);
            }

            if (IsComparisonMethod(method))
            {
                return VisitComparisonMethod(method);
            }

            throw new NotSupportedException($"Method {method.Method.Name} is not supported in filter expressions");
        }

        /// <summary>
        /// Boolean-valued field access: <c>d.Flag</c>, <c>d.Nullable.HasValue</c>, and the bare
        /// element parameter of a predicate over a boolean collection (<c>d.Flags.Any(f =&gt; f)</c>).
        /// </summary>
        private bool TryVisitBoolean(Expression expression, bool negated, out FilterNode<T> node)
        {
            node = null!;

            if (expression is MemberExpression { Member.Name: nameof(Nullable<int>.HasValue) } hasValue
                && hasValue.Expression != null
                && Nullable.GetUnderlyingType(hasValue.Expression.Type) != null
                && TryGetField(hasValue.Expression, out var nullableField))
            {
                node = new FilterNode<T>.Condition
                {
                    PropertyName = nullableField.Path,
                    PropertyType = nullableField.Type,
                    Operator = negated ? Operator.IsNull : Operator.IsNotNull,
                    Value = true
                };
                return true;
            }

            if (expression.Type == typeof(bool)
                && expression is MemberExpression or ParameterExpression
                && TryGetField(expression, out var field))
            {
                node = new FilterNode<T>.Condition
                {
                    PropertyName = field.Path,
                    PropertyType = field.Type,
                    Operator = Operator.Equal,
                    Value = !negated
                };
                return true;
            }

            return false;
        }

        private FilterNode<T> VisitNot(Expression operand)
        {
            if (TryVisitBoolean(operand, negated: true, out var boolean))
            {
                return boolean;
            }

            return Negate(VisitExpression(operand));
        }

        /// <summary>
        /// Negates a mapped node. Conditions and counts are negated by inverting the operator;
        /// everything else is wrapped in a <see cref="FilterNode{T}.Not"/> node.
        /// </summary>
        private static FilterNode<T> Negate(FilterNode<T> node)
        {
            return node switch
            {
                FilterNode<T>.Condition condition => condition with { Operator = InvertOperator(condition.Operator) },
                FilterNode<T>.CollectionCount count => count with { Operator = InvertOperator(count.Operator) },
                FilterNode<T>.Not not => not.Operand,
                _ => new FilterNode<T>.Not { Operand = node }
            };
        }

        private static Operator InvertOperator(Operator op) => op switch
        {
            Operator.Equal => Operator.NotEqual,
            Operator.NotEqual => Operator.Equal,
            Operator.GreaterThan => Operator.LessThanOrEqual,
            Operator.GreaterThanOrEqual => Operator.LessThan,
            Operator.LessThan => Operator.GreaterThanOrEqual,
            Operator.LessThanOrEqual => Operator.GreaterThan,
            Operator.IsNullOrEmpty => Operator.IsNotNullOrEmpty,
            Operator.IsNotNullOrEmpty => Operator.IsNullOrEmpty,
            Operator.IsNullOrWhiteSpace => Operator.IsNotNullOrWhiteSpace,
            Operator.IsNotNullOrWhiteSpace => Operator.IsNullOrWhiteSpace,
            Operator.IsNull => Operator.IsNotNull,
            Operator.IsNotNull => Operator.IsNull,
            Operator.Contains => Operator.NotContains,
            Operator.NotContains => Operator.Contains,
            Operator.ContainsIgnoreCase => Operator.NotContainsIgnoreCase,
            Operator.NotContainsIgnoreCase => Operator.ContainsIgnoreCase,
            Operator.StartsWith => Operator.NotStartsWith,
            Operator.NotStartsWith => Operator.StartsWith,
            Operator.StartsWithIgnoreCase => Operator.NotStartsWithIgnoreCase,
            Operator.NotStartsWithIgnoreCase => Operator.StartsWithIgnoreCase,
            Operator.EndsWith => Operator.NotEndsWith,
            Operator.NotEndsWith => Operator.EndsWith,
            Operator.EndsWithIgnoreCase => Operator.NotEndsWithIgnoreCase,
            Operator.NotEndsWithIgnoreCase => Operator.EndsWithIgnoreCase,
            Operator.In => Operator.NotIn,
            Operator.NotIn => Operator.In,
            Operator.CollectionContains => Operator.CollectionNotContains,
            Operator.CollectionNotContains => Operator.CollectionContains,
            _ => throw new NotSupportedException($"Cannot negate operator {op}")
        };

        private static FilterNode<T> ConstantNode(bool value) => new FilterNode<T>.Group
        {
            // An empty AND group is always true, an empty OR group is always false.
            Operator = value ? LogicalOperator.And : LogicalOperator.Or,
            Conditions = []
        };

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
            // Collection size: d.Tags.Count > 0, d.Scores.Length == 2, d.Items.Count(i => ...) >= 1
            if (TryGetCollectionCount(node.Left, out var count))
            {
                return BuildCollectionCount(count, GetOperator(node.NodeType), node.Right);
            }

            if (TryGetCollectionCount(node.Right, out count))
            {
                return BuildCollectionCount(count, GetOperator(node.NodeType, flipComparison: true), node.Left);
            }

            if (IsMethodCallWithResult(node.Right) && !IsMethodCallWithResult(node.Left))
            {
                // 0 < x.Name.CompareTo("a")  =>  x.Name.CompareTo("a") > 0
                node = Expression.MakeBinary(FlipComparison(node.NodeType), node.Right, node.Left);
            }

            if (IsMethodCallWithResult(node.Left))
            {
                return VisitMethodCallComparison(node);
            }

            var (field, isLeftSide) = GetField(node);
            if (field == null)
            {
                // `boolExpression == true/false` where the left side is not a plain field access
                // (e.g. d.Items.Any(...) == false): map the boolean expression and negate as needed.
                if (TryVisitBooleanComparison(node, out var booleanComparison))
                {
                    return booleanComparison;
                }

                CheckForUnsupportedOperations(node.Left);
                throw new NotSupportedException($"Unable to extract a document field from expression '{node}'");
            }

            // Extract value from the opposite side of where the field is
            var valueExpression = isLeftSide ? node.Right : node.Left;
            var value = NormalizeEnumValue(Evaluate(valueExpression), field.Value);

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
                    PropertyName = field.Value.Path,
                    PropertyType = field.Value.Type,
                    Operator = nullOperator,
                    Value = true // Use true as a placeholder since we don't need the actual value for null checks
                };
            }

            // When the field is on the right side and the constant on the left, we need to flip comparison operators
            var operatorType = GetOperator(node.NodeType, !isLeftSide);

            return new FilterNode<T>.Condition
            {
                PropertyName = field.Value.Path,
                PropertyType = field.Value.Type,
                Operator = operatorType,
                Value = value
            };
        }

        private bool TryVisitBooleanComparison(BinaryExpression node, out FilterNode<T> result)
        {
            result = null!;
            if (node.NodeType is not (ExpressionType.Equal or ExpressionType.NotEqual)
                || node.Left.Type != typeof(bool))
            {
                return false;
            }

            Expression? booleanSide = null;
            bool constant;
            if (!ReferencesParameter(node.Right) && Evaluate(node.Right) is bool rightConstant)
            {
                booleanSide = node.Left;
                constant = rightConstant;
            }
            else if (!ReferencesParameter(node.Left) && Evaluate(node.Left) is bool leftConstant)
            {
                booleanSide = node.Right;
                constant = leftConstant;
            }
            else
            {
                return false;
            }

            var mapped = VisitExpression(booleanSide);
            var negate = constant == (node.NodeType == ExpressionType.NotEqual);
            result = negate ? Negate(mapped) : mapped;
            return true;
        }

        private FilterNode<T> VisitMethodCallComparison(BinaryExpression node)
        {
            var methodCall = (MethodCallExpression)node.Left;

            if (!TryGetField(methodCall.Object, out var field))
            {
                throw new NotSupportedException($"{methodCall.Method.Name} method must be called on a property");
            }

            var comparisonResult = Evaluate(node.Right);

            return methodCall.Method.Name switch
            {
                nameof(IComparable.CompareTo) => HandleCompareToComparison(methodCall, field, node.NodeType, comparisonResult),
                nameof(object.ToString) => HandleToStringComparison(field, node.NodeType, comparisonResult),
                _ => throw new NotSupportedException($"Method {methodCall.Method.Name} is not supported in binary comparisons")
            };
        }

        private FilterNode<T> HandleCompareToComparison(MethodCallExpression methodCall, FieldRef field,
            ExpressionType nodeType, object? comparisonResult)
        {
            var compareValue = NormalizeEnumValue(Evaluate(methodCall.Arguments[0]), field);

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
                PropertyName = field.Path,
                PropertyType = field.Type,
                Operator = operatorType,
                Value = compareValue!
            };
        }

        private static FilterNode<T> HandleToStringComparison(FieldRef field, ExpressionType nodeType, object? comparisonResult)
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
                PropertyName = field.Path,
                PropertyType = field.Type,
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

        /// <summary>
        /// Finds the document field operand of a comparison, checking the left side first.
        /// A method call on a field (other than the CompareTo/ToString forms handled earlier)
        /// is rejected rather than silently ignored.
        /// </summary>
        private (FieldRef? field, bool isLeftSide) GetField(BinaryExpression node)
        {
            if (TryGetField(node.Left, out var left))
            {
                return (left, true);
            }

            if (TryGetField(node.Right, out var right))
            {
                return (right, false);
            }

            foreach (var side in new[] { node.Left, node.Right })
            {
                if (side is MethodCallExpression call && TryGetField(call.Object, out _))
                {
                    throw new NotSupportedException(
                        $"Method {call.Method.Name} is not supported in comparisons. Only CompareTo and ToString can be compared directly.");
                }
            }

            return (null, true);
        }

        private static ExpressionType FlipComparison(ExpressionType type) => type switch
        {
            ExpressionType.GreaterThan => ExpressionType.LessThan,
            ExpressionType.GreaterThanOrEqual => ExpressionType.LessThanOrEqual,
            ExpressionType.LessThan => ExpressionType.GreaterThan,
            ExpressionType.LessThanOrEqual => ExpressionType.GreaterThanOrEqual,
            _ => type
        };

        private static bool IsMethodCallWithResult(Expression expression)
        {
            return expression is MethodCallExpression { Method.Name: nameof(IComparable.CompareTo) or nameof(ToString) };
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

        // ------------------------------------------------------------------
        // Field resolution
        // ------------------------------------------------------------------

        private static Expression StripConvert(Expression expression)
        {
            while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert)
            {
                expression = convert.Operand;
            }

            return expression;
        }

        /// <summary>
        /// Resolves a document field reference rooted at the current scope parameter: either the
        /// bare parameter itself (the element of a scalar collection) or a chain of property
        /// accesses. Returns false when the expression is rooted elsewhere (a captured value, or a
        /// parameter of an enclosing scope). Throws when the chain is rooted at the parameter but
        /// reaches into a scalar or collection type (e.g. <c>d.Name.Length</c>, <c>d.Created.Year</c>),
        /// since those members have no JSON representation to query.
        /// </summary>
        private bool TryGetField(Expression? expression, out FieldRef field)
        {
            field = default;
            if (expression == null)
            {
                return false;
            }

            expression = StripConvert(expression);

            if (expression is ParameterExpression parameter)
            {
                if (parameter != _parameter)
                {
                    return false;
                }

                field = new FieldRef(string.Empty, parameter.Type, null);
                return true;
            }

            if (expression is not MemberExpression member)
            {
                return false;
            }

            // Walk to the root first: only chains rooted at our parameter are document fields.
            var chain = new List<MemberExpression>();
            Expression? current = member;
            while (current is MemberExpression m)
            {
                chain.Add(m);
                current = m.Expression;
            }

            if (current is not ParameterExpression root || root != _parameter)
            {
                return false;
            }

            // Then build the path from the root outwards, validating each segment.
            var segments = new List<string>();
            PropertyInfo? leaf = null;
            var leafType = root.Type;
            for (var i = chain.Count - 1; i >= 0; i--)
            {
                var m = chain[i];
                var ownerType = m.Expression!.Type;

                if (Nullable.GetUnderlyingType(ownerType) != null)
                {
                    if (m.Member.Name == nameof(Nullable<int>.Value))
                    {
                        // d.Nullable.Value reads the same JSON value as d.Nullable, typed as the
                        // underlying value type.
                        leafType = m.Type;
                        continue;
                    }

                    throw new NotSupportedException(
                        $"Member {m.Member.Name} of {ownerType.Name} is not supported in filter expressions. Compare the nullable field directly or use HasValue.");
                }

                if (IsScalarType(ownerType) || typeof(IEnumerable).IsAssignableFrom(ownerType))
                {
                    throw new NotSupportedException(
                        $"Member {m.Member.Name} of {ownerType.Name} is not supported in filter expressions. Only document fields, nested fields and collection Count/Length can be queried.");
                }

                if (m.Member is not PropertyInfo property)
                {
                    throw new NotSupportedException(
                        $"Member {m.Member.Name} is not a property. Only public properties are serialized and can be filtered on.");
                }

                segments.Add(property.Name);
                leaf = property;
                leafType = property.PropertyType;
            }

            field = new FieldRef(string.Join(".", segments), leafType, leaf);
            return true;
        }

        private static bool IsScalarType(Type type)
        {
            return type.IsPrimitive
                   || type.IsEnum
                   || type == typeof(string)
                   || type == typeof(decimal)
                   || type == typeof(DateTime)
                   || type == typeof(DateTimeOffset)
                   || type == typeof(DateOnly)
                   || type == typeof(TimeOnly)
                   || type == typeof(TimeSpan)
                   || type == typeof(Guid);
        }

        /// <summary>
        /// Recognizes <c>collection.Count</c>, <c>array.Length</c>, <c>collection.Count()</c>,
        /// <c>collection.LongCount()</c> and <c>collection.Count(predicate)</c> on a document field.
        /// </summary>
        private bool TryGetCollectionCount(Expression expression, out CountRef count)
        {
            count = default;
            expression = StripConvert(expression);

            if (expression is MemberExpression { Member.Name: "Count" or "Length" } member
                && member.Expression != null
                && CollectionTypes.IsCollection(member.Expression.Type)
                && TryGetField(member.Expression, out var memberField))
            {
                count = new CountRef(memberField, null);
                return true;
            }

            // array.Length compiles to a dedicated ArrayLength node rather than a member access.
            if (expression is UnaryExpression { NodeType: ExpressionType.ArrayLength } arrayLength
                && TryGetField(arrayLength.Operand, out var arrayField))
            {
                count = new CountRef(arrayField, null);
                return true;
            }

            if (expression is MethodCallExpression { Method.DeclaringType: { } declaringType } call
                && declaringType == typeof(Enumerable)
                && call.Method.Name is nameof(Enumerable.Count) or nameof(Enumerable.LongCount)
                && call.Arguments.Count is 1 or 2
                && TryGetField(call.Arguments[0], out var sourceField))
            {
                var predicate = call.Arguments.Count == 2 ? UnwrapLambda(call.Arguments[1]) : null;
                count = new CountRef(sourceField, predicate);
                return true;
            }

            return false;
        }

        private FilterNode<T> BuildCollectionCount(CountRef count, Operator op, Expression valueExpression)
        {
            var value = Evaluate(valueExpression)
                        ?? throw new NotSupportedException("A collection count cannot be compared to null");

            return new FilterNode<T>.CollectionCount
            {
                PropertyName = count.Collection.Path,
                ElementType = CollectionTypes.GetElementType(count.Collection.Type),
                Predicate = count.Predicate == null ? null : MapElementPredicate(count.Predicate),
                Operator = op,
                Value = Convert.ToInt64(value)
            };
        }

        private static LambdaExpression UnwrapLambda(Expression expression)
        {
            if (expression is UnaryExpression { NodeType: ExpressionType.Quote } quote)
            {
                expression = quote.Operand;
            }

            return expression as LambdaExpression
                   ?? throw new NotSupportedException($"Expected a lambda predicate but found '{expression}'");
        }

        /// <summary>
        /// Maps a predicate over collection elements in a fresh scope rooted at the lambda parameter.
        /// </summary>
        private static FilterNode<T> MapElementPredicate(LambdaExpression predicate)
        {
            if (predicate.Parameters.Count != 1)
            {
                throw new NotSupportedException("Collection predicates must take exactly one parameter");
            }

            return new FilterExpressionVisitor<T>(predicate.Parameters[0]).VisitExpression(predicate.Body);
        }

        // ------------------------------------------------------------------
        // Value evaluation
        // ------------------------------------------------------------------

        /// <summary>
        /// Evaluates a value-side expression (constants, captured variables, method calls on them).
        /// Rejects expressions that reference any lambda parameter: a document field cannot be
        /// used as a value, and a collection predicate cannot reach the enclosing document.
        /// </summary>
        private static object? Evaluate(Expression expression)
        {
            if (ReferencesParameter(expression))
            {
                throw new NotSupportedException(
                    $"Expression '{expression}' references a document parameter and cannot be evaluated as a value. " +
                    "Comparing two document fields, or referencing the outer document inside a collection predicate, is not supported.");
            }

            return Expression.Lambda(expression).Compile().DynamicInvoke();
        }

        private static bool ReferencesParameter(Expression expression)
        {
            var finder = new ParameterFinder();
            finder.Visit(expression);
            return finder.Found;
        }

        private sealed class ParameterFinder : ExpressionVisitor
        {
            public bool Found { get; private set; }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                Found = true;
                return node;
            }
        }

        private sealed class ParameterReplacer(ParameterExpression parameter, Expression replacement) : ExpressionVisitor
        {
            protected override Expression VisitParameter(ParameterExpression node) =>
                node == parameter ? replacement : node;
        }

        /// <summary>
        /// Converts an enum value to the representation the field is serialized with (its name
        /// for string-serialized enums, otherwise its underlying integer), so providers compare
        /// against what is stored in the JSON document.
        /// </summary>
        private static object? NormalizeEnumValue(object? value, FieldRef field)
        {
            if (value == null)
            {
                return null;
            }

            var underlyingType = Nullable.GetUnderlyingType(field.Type) ?? field.Type;
            if (!underlyingType.IsEnum)
            {
                return value;
            }

            var format = field.Property != null
                ? EnumSerializationAnalyzer.GetPropertyFormat(field.Property)
                : EnumSerializationAnalyzer.GetDefaultFormat(underlyingType);

            if (format == EnumSerializationFormat.String)
            {
                // The value may arrive as the enum's underlying integer (via a Convert node) or as
                // the enum itself; both round-trip through Enum.ToObject to the member name.
                return Enum.ToObject(underlyingType, value).ToString();
            }

            return Convert.ChangeType(value, underlyingType.GetEnumUnderlyingType());
        }

        // ------------------------------------------------------------------
        // Method calls
        // ------------------------------------------------------------------

        private static bool IsStringNullOrEmptyMethod(MethodCallExpression method)
        {
            return method.Method.DeclaringType == typeof(string) &&
                   method.Method.Name is nameof(string.IsNullOrEmpty) or nameof(string.IsNullOrWhiteSpace);
        }

        private FilterNode<T> VisitStringNullOrEmptyMethod(MethodCallExpression method)
        {
            if (method.Arguments.Count != 1 || !TryGetField(method.Arguments[0], out var field))
            {
                throw new NotSupportedException($"Unsupported {method.Method.Name} usage - the argument must be a document field");
            }

            var operatorType = method.Method.Name == nameof(string.IsNullOrEmpty)
                ? Operator.IsNullOrEmpty
                : Operator.IsNullOrWhiteSpace;

            return new FilterNode<T>.Condition
            {
                PropertyName = field.Path,
                PropertyType = field.Type,
                Operator = operatorType,
                Value = true
            };
        }

        private static bool IsQuantifierMethod(MethodCallExpression method)
        {
            if (method.Method.DeclaringType == typeof(Enumerable))
            {
                return method.Method.Name is nameof(Enumerable.Any) or nameof(Enumerable.All)
                       && method.Arguments.Count is 1 or 2;
            }

            // List<T>.Exists / List<T>.TrueForAll and the Array equivalents.
            return method.Method.Name is "Exists" or "TrueForAll"
                   && method.Arguments.Count == 1
                   && method.Object != null
                   && CollectionTypes.IsCollection(method.Object.Type);
        }

        private FilterNode<T> VisitQuantifierMethod(MethodCallExpression method)
        {
            var (source, predicateExpression) = method.Object == null
                ? (method.Arguments[0], method.Arguments.Count == 2 ? method.Arguments[1] : null)
                : (method.Object, method.Arguments[0]);

            var kind = method.Method.Name is nameof(Enumerable.Any) or "Exists"
                ? QuantifierKind.Any
                : QuantifierKind.All;

            var predicate = predicateExpression == null ? null : UnwrapLambda(predicateExpression);

            // Case 1: quantifier over a document collection field.
            if (TryGetField(source, out var field))
            {
                if (!CollectionTypes.IsCollection(field.Type))
                {
                    throw new NotSupportedException($"{method.Method.Name} can only be applied to a collection field");
                }

                return new FilterNode<T>.Quantifier
                {
                    PropertyName = field.Path,
                    ElementType = CollectionTypes.GetElementType(field.Type),
                    Kind = kind,
                    Predicate = predicate == null ? null : MapElementPredicate(predicate)
                };
            }

            // Case 2: quantifier over a constant collection, e.g. `wanted.Any(w => d.Tags.Contains(w))`
            // or `required.All(r => d.Tags.Contains(r))`. The set is known up front, so the
            // predicate is expanded once per element into an OR (Any) / AND (All) group.
            if (Evaluate(source) is not IEnumerable items)
            {
                throw new NotSupportedException(
                    $"Unsupported {method.Method.Name} usage - the source must be a document collection field or a constant collection");
            }

            var values = items.Cast<object?>().ToList();
            if (predicate == null)
            {
                return ConstantNode(values.Count > 0);
            }

            var parameter = predicate.Parameters[0];
            var conditions = values
                .Select(item => new ParameterReplacer(parameter, Expression.Constant(item, parameter.Type)).Visit(predicate.Body))
                .Select(VisitExpression)
                .ToList();

            return new FilterNode<T>.Group
            {
                Operator = kind == QuantifierKind.Any ? LogicalOperator.Or : LogicalOperator.And,
                Conditions = conditions
            };
        }

        private static bool IsSetOperatorMethod(MethodCallExpression method)
        {
            // Handle collection.Contains (for IEnumerable<T>.Contains extension method)
            if (method.Method.Name == nameof(Enumerable.Contains) && method.Method.DeclaringType == typeof(Enumerable))
                return true;

            // Handle list/collection.Contains (instance method) - but exclude string.Contains
            if (method.Method.Name == "Contains" && method.Method.DeclaringType != typeof(string))
                return true;

            return false;
        }

        private static bool IsStringOperatorMethod(MethodCallExpression method)
        {
            if (method.Method.DeclaringType != typeof(string))
                return false;

            return method.Method.Name is
                nameof(string.Contains) or
                nameof(string.StartsWith) or
                nameof(string.EndsWith);
        }

        private static bool IsComparisonMethod(MethodCallExpression method)
        {
            return method.Method.Name is
                nameof(object.Equals) or
                nameof(IComparable.CompareTo) or
                nameof(object.ToString);
        }

        private FilterNode<T> VisitSetOperatorMethod(MethodCallExpression method)
        {
            // Contains comes in two shapes that we map to different operators depending on which
            // operand is the document field:
            //   1. constantCollection.Contains(d.Field)  => In        (value set membership)
            //   2. d.Collection.Contains(constant)        => CollectionContains (array field holds value)
            var (collectionExpr, itemExpr) = ExtractContainsOperands(method);

            var isItemField = TryGetField(itemExpr, out var itemField);
            var isCollectionField = TryGetField(collectionExpr, out var collectionField);

            // Case 1: the item is the document field, the collection is a constant.
            if (isItemField && !isCollectionField)
            {
                var collectionValue = Evaluate(collectionExpr)
                    ?? throw new NotSupportedException("Unsupported Contains usage - unable to evaluate the collection");

                return new FilterNode<T>.Condition
                {
                    PropertyName = itemField.Path,
                    PropertyType = itemField.Type,
                    Operator = Operator.In,
                    Value = collectionValue
                };
            }

            // Case 2: the collection is the document field (an array/list), the item is a constant.
            if (isCollectionField && !isItemField)
            {
                if (!CollectionTypes.IsCollection(collectionField.Type))
                {
                    throw new NotSupportedException("Unsupported Contains usage - the document field must be a collection");
                }

                var itemValue = Evaluate(itemExpr)
                    ?? throw new NotSupportedException("Unsupported Contains usage - the searched value cannot be null");

                return new FilterNode<T>.Condition
                {
                    PropertyName = collectionField.Path,
                    PropertyType = collectionField.Type,
                    Operator = Operator.CollectionContains,
                    Value = itemValue
                };
            }

            throw new NotSupportedException("Unsupported Contains usage - exactly one operand must be a document field");
        }

        /// <summary>
        /// Extracts the (collection, item) operands from a Contains call, normalizing across the
        /// static <c>Enumerable.Contains(collection, item)</c> / <c>MemoryExtensions.Contains</c>
        /// forms and the instance <c>collection.Contains(item)</c> form. Implicit array-to-span
        /// conversions are unwrapped so the underlying array is visible.
        /// </summary>
        private static (Expression collection, Expression item) ExtractContainsOperands(MethodCallExpression method)
        {
            // Static form: Enumerable.Contains(collection, item) or the MemoryExtensions span
            // overloads Contains(span, value[, comparer]) — collection and item are the first two
            // arguments; any trailing comparer argument is ignored.
            if (method.Object == null && method.Arguments.Count >= 2)
            {
                return (UnwrapImplicitConversion(method.Arguments[0]), method.Arguments[1]);
            }

            // Instance form: collection.Contains(item[, comparer]).
            if (method.Object != null && method.Arguments.Count >= 1)
            {
                return (UnwrapImplicitConversion(method.Object), method.Arguments[0]);
            }

            throw new NotSupportedException("Unsupported Contains usage - unable to extract operands");
        }

        private static Expression UnwrapImplicitConversion(Expression expression)
        {
            return expression is MethodCallExpression { Method.Name: "op_Implicit", Arguments.Count: 1 } conversion
                ? conversion.Arguments[0]
                : expression;
        }

        private FilterNode<T> VisitStringOperatorMethod(MethodCallExpression method)
        {
            if (!TryGetField(method.Object, out var field))
            {
                throw new NotSupportedException($"Unsupported {method.Method.Name} usage - property must be on the left side");
            }

            var value = Evaluate(method.Arguments[0]);

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
                PropertyName = field.Path,
                PropertyType = field.Type,
                Operator = operatorType,
                Value = value!
            };
        }

        private FilterNode<T> VisitComparisonMethod(MethodCallExpression method)
        {
            switch (method.Method.Name)
            {
                case nameof(IComparable.CompareTo):
                    throw new NotSupportedException(
                        "CompareTo method should be used with comparison operators (e.g., x.CompareTo(y) == 0, x.CompareTo(y) > 0)");
                case nameof(object.ToString):
                    throw new NotSupportedException(
                        "ToString method should be used with comparison operators (e.g., x.ToString() == \"value\")");
            }

            FieldRef field;
            Expression valueExpression;

            // Case 1: property.Equals(constant) - x.Age.Equals(30)
            if (TryGetField(method.Object, out field) && method.Arguments.Count >= 1)
            {
                valueExpression = method.Arguments[0];
            }
            // Case 2: constant.Equals(property) - 30.Equals(x.Age)
            else if (method.Object != null && method.Arguments.Count >= 1 && TryGetField(method.Arguments[0], out field))
            {
                valueExpression = method.Object;
            }
            else
            {
                throw new NotSupportedException($"Unsupported {method.Method.Name} usage - one operand must be a property");
            }

            if (method.Arguments.Count > 1)
            {
                throw new NotSupportedException(
                    "Equals with a StringComparison is not supported. Use == for ordinal equality.");
            }

            var value = NormalizeEnumValue(Evaluate(valueExpression), field);

            return new FilterNode<T>.Condition
            {
                PropertyName = field.Path,
                PropertyType = field.Type,
                Operator = Operator.Equal,
                Value = value!
            };
        }
    }
}
