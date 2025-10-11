using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Catga.Analyzers;

/// <summary>
/// Analyzer for detecting GC pressure and allocation issues
/// Helps identify performance bottlenecks related to memory allocations
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class GCPressureAnalyzer : DiagnosticAnalyzer
{
    // CATGA101: ToArray() in hot path
    private static readonly DiagnosticDescriptor ToArrayInHotPathRule = new(
        id: "CATGA101",
        title: "Avoid ToArray() in hot path",
        messageFormat: "ToArray() call in hot path method '{0}' creates unnecessary allocation. Consider using Span<T>, ArrayPool, or foreach directly on IEnumerable.",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "ToArray() creates a new array allocation. In hot paths, use Span<T>, ArrayPool<T>, or iterate directly to avoid GC pressure.");

    // CATGA102: Missing ArrayPool usage
    private static readonly DiagnosticDescriptor MissingArrayPoolRule = new(
        id: "CATGA102",
        title: "Consider using ArrayPool for temporary arrays",
        messageFormat: "Temporary array allocation '{0}' of size {1} detected. Consider using ArrayPool<T>.Shared.Rent() to reduce GC pressure.",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "For temporary arrays that are short-lived, ArrayPool can significantly reduce allocations and GC pressure.");

    // CATGA103: String concatenation in loop
    private static readonly DiagnosticDescriptor StringConcatenationRule = new(
        id: "CATGA103",
        title: "Avoid string concatenation in loops",
        messageFormat: "String concatenation in loop detected. Use StringBuilder or string interpolation to avoid multiple allocations.",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "String concatenation in loops creates multiple intermediate string objects. Use StringBuilder for better performance.");

    // CATGA104: Can use Span<T>
    private static readonly DiagnosticDescriptor CanUseSpanRule = new(
        id: "CATGA104",
        title: "Consider using Span<T> for zero-allocation operations",
        messageFormat: "Method '{0}' operates on array/string. Consider using Span<T> or ReadOnlySpan<T> for zero-allocation slicing and manipulation.",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Span<T> enables zero-allocation slicing and manipulation of arrays and strings.");

    // CATGA105: Unnecessary boxing
    private static readonly DiagnosticDescriptor UnnecessaryBoxingRule = new(
        id: "CATGA105",
        title: "Unnecessary boxing allocation detected",
        messageFormat: "Value type '{0}' is being boxed. Consider using generic constraints or avoiding boxing to reduce allocations.",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Boxing converts value types to reference types, causing heap allocations. Avoid boxing in hot paths.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            ToArrayInHotPathRule,
            MissingArrayPoolRule,
            StringConcatenationRule,
            CanUseSpanRule,
            UnnecessaryBoxingRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeToArrayCall, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeArrayCreation, SyntaxKind.ArrayCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeStringConcatenation, SyntaxKind.AddExpression);
        context.RegisterSyntaxNodeAction(AnalyzeBoxing, SyntaxKind.CastExpression);
    }

    private void AnalyzeToArrayCall(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Check if it's a ToArray() call
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        if (memberAccess.Name.Identifier.ValueText != "ToArray")
            return;

        // Check if we're in a hot path (handler method)
        var method = context.SemanticModel.GetEnclosingSymbol(invocation.SpanStart) as IMethodSymbol;
        if (method == null)
            return;

        // Check if method is a handler or has [HotPath] attribute
        if (IsHotPathMethod(method))
        {
            var diagnostic = Diagnostic.Create(
                ToArrayInHotPathRule,
                invocation.GetLocation(),
                method.Name);
            context.ReportDiagnostic(diagnostic);
        }
    }

    private void AnalyzeArrayCreation(SyntaxNodeAnalysisContext context)
    {
        var arrayCreation = (ArrayCreationExpressionSyntax)context.Node;

        // Get array type and size
        var rankSpecifiers = arrayCreation.Type.RankSpecifiers;
        if (rankSpecifiers.Count == 0)
            return;

        var sizes = rankSpecifiers[0].Sizes;
        if (sizes.Count == 0)
            return;

        // Try to get constant size
        var firstSize = sizes[0];
        var constantValue = context.SemanticModel.GetConstantValue(firstSize);

        if (!constantValue.HasValue || constantValue.Value is not int size)
            return;

        // Suggest ArrayPool for arrays larger than threshold (e.g., 100 elements)
        if (size >= 100)
        {
            // Check if it's a temporary array (local variable)
            if (IsTemporaryArray(context, arrayCreation))
            {
                var diagnostic = Diagnostic.Create(
                    MissingArrayPoolRule,
                    arrayCreation.GetLocation(),
                    arrayCreation.Type.ElementType,
                    size);
                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private void AnalyzeStringConcatenation(SyntaxNodeAnalysisContext context)
    {
        var binaryExpression = (BinaryExpressionSyntax)context.Node;

        // Check if both operands are strings
        var leftType = context.SemanticModel.GetTypeInfo(binaryExpression.Left).Type;
        var rightType = context.SemanticModel.GetTypeInfo(binaryExpression.Right).Type;

        if (leftType?.SpecialType != SpecialType.System_String ||
            rightType?.SpecialType != SpecialType.System_String)
            return;

        // Check if we're inside a loop
        if (IsInsideLoop(binaryExpression))
        {
            var diagnostic = Diagnostic.Create(
                StringConcatenationRule,
                binaryExpression.GetLocation());
            context.ReportDiagnostic(diagnostic);
        }
    }

    private void AnalyzeBoxing(SyntaxNodeAnalysisContext context)
    {
        var castExpression = (CastExpressionSyntax)context.Node;

        var sourceType = context.SemanticModel.GetTypeInfo(castExpression.Expression).Type;
        var targetType = context.SemanticModel.GetTypeInfo(castExpression.Type).Type;

        if (sourceType == null || targetType == null)
            return;

        // Check if casting from value type to object/interface (boxing)
        if (sourceType.IsValueType && targetType.IsReferenceType)
        {
            var diagnostic = Diagnostic.Create(
                UnnecessaryBoxingRule,
                castExpression.GetLocation(),
                sourceType.Name);
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool IsHotPathMethod(IMethodSymbol method)
    {
        // Check if method is a handler
        if (method.Name.EndsWith("Handler") || method.Name.Contains("Handle"))
            return true;

        // Check for [HotPath] attribute
        foreach (var attribute in method.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == "HotPathAttribute")
                return true;
        }

        // Check if containing type implements handler interfaces
        var containingType = method.ContainingType;
        foreach (var @interface in containingType.AllInterfaces)
        {
            var interfaceName = @interface.ToDisplayString();
            if (interfaceName.Contains("IRequestHandler") || interfaceName.Contains("IEventHandler"))
                return true;
        }

        return false;
    }

    private static bool IsTemporaryArray(SyntaxNodeAnalysisContext context, ArrayCreationExpressionSyntax arrayCreation)
    {
        // Check if array is assigned to a local variable
        var parent = arrayCreation.Parent;

        // Variable declaration: var array = new int[100];
        if (parent is EqualsValueClauseSyntax equalsValue)
        {
            if (equalsValue.Parent is VariableDeclaratorSyntax)
                return true;
        }

        // Direct assignment: array = new int[100];
        if (parent is AssignmentExpressionSyntax)
            return true;

        return false;
    }

    private static bool IsInsideLoop(SyntaxNode node)
    {
        var current = node.Parent;
        while (current != null)
        {
            if (current is ForStatementSyntax or
                ForEachStatementSyntax or
                WhileStatementSyntax or
                DoStatementSyntax)
            {
                return true;
            }
            current = current.Parent;
        }
        return false;
    }
}

