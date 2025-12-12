using System.Linq.Expressions;
using System.Reflection;

namespace Catga.Flow.Dsl;

/// <summary>
/// Analyzes and optimizes Expression trees for Flow DSL.
/// </summary>
public static class ExpressionAnalyzer
{
    /// <summary>
    /// Extract property name from an expression like s => s.Property or s => s.Property.SubProperty.
    /// </summary>
    public static string ExtractPropertyName(Expression expression)
    {
        var chain = ExtractPropertyChain(expression);
        return string.Join(".", chain.Properties.Select(p => p.Name));
    }

    /// <summary>
    /// Extract property chain from an expression.
    /// </summary>
    public static PropertyChain ExtractPropertyChain(Expression expression)
    {
        var properties = new List<PropertyInfo>();
        var current = expression;

        // Unwrap lambda if needed
        if (current is LambdaExpression lambda)
        {
            current = lambda.Body;
        }

        // Traverse the member access chain
        while (current is MemberExpression memberExpr)
        {
            if (memberExpr.Member is PropertyInfo prop)
            {
                properties.Insert(0, prop);
            }
            current = memberExpr.Expression;
        }

        return new PropertyChain { Properties = properties };
    }

    /// <summary>
    /// Check if an expression has side effects (method calls, assignments, etc.).
    /// </summary>
    public static bool HasSideEffects(Expression expression)
    {
        var visitor = new SideEffectDetector();
        visitor.Visit(expression);
        return visitor.HasSideEffects;
    }

    /// <summary>
    /// Optimize an expression by performing constant folding and dead code elimination.
    /// </summary>
    public static Expression Optimize(Expression expression)
    {
        var optimizer = new ExpressionOptimizer();
        return optimizer.Visit(expression);
    }

    /// <summary>
    /// Get all constants used in an expression.
    /// </summary>
    public static IEnumerable<object?> ExtractConstants(Expression expression)
    {
        var visitor = new ConstantExtractor();
        visitor.Visit(expression);
        return visitor.Constants;
    }

    /// <summary>
    /// Check if an expression is a simple property access.
    /// </summary>
    public static bool IsSimplePropertyAccess(Expression expression)
    {
        if (expression is LambdaExpression lambda)
        {
            expression = lambda.Body;
        }

        return expression is MemberExpression memberExpr &&
               memberExpr.Expression is ParameterExpression;
    }

    /// <summary>
    /// Get the parameter used in an expression.
    /// </summary>
    public static ParameterExpression? GetParameter(Expression expression)
    {
        var visitor = new ParameterExtractor();
        visitor.Visit(expression);
        return visitor.Parameter;
    }

    /// <summary>
    /// Check if two expressions are equivalent.
    /// </summary>
    public static bool AreEquivalent(Expression expr1, Expression expr2)
    {
        var comparer = new ExpressionComparer();
        return comparer.Equals(expr1, expr2);
    }
}

/// <summary>
/// Represents a chain of property accesses.
/// </summary>
public class PropertyChain
{
    public List<PropertyInfo> Properties { get; set; } = new();

    public override string ToString() => string.Join(".", Properties.Select(p => p.Name));
}

/// <summary>
/// Detects side effects in expressions.
/// </summary>
internal class SideEffectDetector : ExpressionVisitor
{
    public bool HasSideEffects { get; private set; }

    public override Expression? Visit(Expression? node)
    {
        if (HasSideEffects) return node;

        return base.Visit(node);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        // Method calls are considered side effects unless they're pure
        if (!IsPureMethod(node.Method))
        {
            HasSideEffects = true;
        }
        return base.VisitMethodCall(node);
    }

    protected override Expression VisitInvocation(InvocationExpression node)
    {
        HasSideEffects = true;
        return base.VisitInvocation(node);
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
        if (node.NodeType == ExpressionType.Assign)
        {
            HasSideEffects = true;
        }
        return base.VisitBinary(node);
    }

    private static bool IsPureMethod(MethodInfo method)
    {
        // Check for common pure methods
        var declaringType = method.DeclaringType;
        if (declaringType == null) return false;

        // String methods are generally pure
        if (declaringType == typeof(string)) return true;

        // LINQ methods are pure
        if (declaringType.Namespace?.StartsWith("System.Linq") == true) return true;

        // Math methods are pure
        if (declaringType == typeof(Math)) return true;

        return false;
    }
}

/// <summary>
/// Optimizes expressions by performing constant folding and other optimizations.
/// </summary>
internal class ExpressionOptimizer : ExpressionVisitor
{
    protected override Expression VisitBinary(BinaryExpression node)
    {
        var left = Visit(node.Left);
        var right = Visit(node.Right);

        // Constant folding
        if (left is ConstantExpression leftConst && right is ConstantExpression rightConst)
        {
            try
            {
                var result = EvaluateBinary(node.NodeType, leftConst.Value, rightConst.Value);
                return Expression.Constant(result, node.Type);
            }
            catch
            {
                // If evaluation fails, return the original expression
            }
        }

        if (left != node.Left || right != node.Right)
        {
            return Expression.MakeBinary(node.NodeType, left, right, node.IsLiftedToNull, node.Method);
        }

        return node;
    }

    private static object? EvaluateBinary(ExpressionType nodeType, object? left, object? right)
    {
        return nodeType switch
        {
            ExpressionType.Add => (dynamic)left + (dynamic)right,
            ExpressionType.Subtract => (dynamic)left - (dynamic)right,
            ExpressionType.Multiply => (dynamic)left * (dynamic)right,
            ExpressionType.Divide => (dynamic)left / (dynamic)right,
            ExpressionType.Equal => Equals(left, right),
            ExpressionType.NotEqual => !Equals(left, right),
            ExpressionType.LessThan => (dynamic)left < (dynamic)right,
            ExpressionType.LessThanOrEqual => (dynamic)left <= (dynamic)right,
            ExpressionType.GreaterThan => (dynamic)left > (dynamic)right,
            ExpressionType.GreaterThanOrEqual => (dynamic)left >= (dynamic)right,
            ExpressionType.And => (dynamic)left & (dynamic)right,
            ExpressionType.Or => (dynamic)left | (dynamic)right,
            ExpressionType.AndAlso => (dynamic)left && (dynamic)right,
            ExpressionType.OrElse => (dynamic)left || (dynamic)right,
            _ => null
        };
    }
}

/// <summary>
/// Extracts all constants from an expression.
/// </summary>
internal class ConstantExtractor : ExpressionVisitor
{
    public List<object?> Constants { get; } = new();

    protected override Expression VisitConstant(ConstantExpression node)
    {
        Constants.Add(node.Value);
        return base.VisitConstant(node);
    }
}

/// <summary>
/// Extracts the parameter from an expression.
/// </summary>
internal class ParameterExtractor : ExpressionVisitor
{
    public ParameterExpression? Parameter { get; private set; }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        Parameter = node;
        return base.VisitParameter(node);
    }
}

/// <summary>
/// Compares two expressions for equivalence.
/// </summary>
internal class ExpressionComparer : ExpressionVisitor
{
    private Expression? _other;
    private bool _isEqual = true;

    public bool Equals(Expression expr1, Expression expr2)
    {
        _other = expr2;
        _isEqual = true;
        Visit(expr1);
        return _isEqual;
    }

    public override Expression? Visit(Expression? node)
    {
        if (!_isEqual) return node;

        if (node == null && _other == null) return node;
        if (node == null || _other == null)
        {
            _isEqual = false;
            return node;
        }

        if (node.NodeType != _other.NodeType || node.Type != _other.Type)
        {
            _isEqual = false;
            return node;
        }

        return base.Visit(node);
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        if (_other is ConstantExpression otherConst)
        {
            _isEqual = Equals(node.Value, otherConst.Value);
        }
        else
        {
            _isEqual = false;
        }
        return node;
    }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        if (_other is ParameterExpression otherParam)
        {
            _isEqual = node.Name == otherParam.Name;
        }
        else
        {
            _isEqual = false;
        }
        return node;
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        if (_other is MemberExpression otherMember)
        {
            _isEqual = node.Member.Name == otherMember.Member.Name;
            if (_isEqual)
            {
                var temp = _other;
                _other = otherMember.Expression;
                Visit(node.Expression);
                _other = temp;
            }
        }
        else
        {
            _isEqual = false;
        }
        return node;
    }
}
