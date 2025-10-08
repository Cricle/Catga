using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Catga.Analyzers;

/// <summary>
/// Performance analyzers for Catga handlers
/// Detects performance issues and suggests optimizations
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class CatgaPerformanceAnalyzers : DiagnosticAnalyzer
{
    // CATGA005: Avoid blocking calls in async handlers
    private static readonly DiagnosticDescriptor BlockingCallRule = new(
        id: "CATGA005",
        title: "Avoid blocking calls in async handlers",
        messageFormat: "Method '{0}' performs blocking call '{1}'. Use async version instead to avoid thread pool starvation.",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Blocking calls like .Result, .Wait(), or .GetAwaiter().GetResult() can cause deadlocks and thread pool starvation. Use await instead.");

    // CATGA006: Use ValueTask for hot paths
    private static readonly DiagnosticDescriptor ValueTaskRule = new(
        id: "CATGA006",
        title: "Consider using ValueTask for frequently called handlers",
        messageFormat: "Handler '{0}' is frequently called. Consider using ValueTask<T> instead of Task<T> to reduce allocations.",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "ValueTask<T> avoids heap allocation when completing synchronously, improving performance for hot paths.");

    // CATGA007: Missing ConfigureAwait(false)
    private static readonly DiagnosticDescriptor ConfigureAwaitRule = new(
        id: "CATGA007",
        title: "Missing ConfigureAwait(false) in library code",
        messageFormat: "Await expression should use ConfigureAwait(false) to avoid unnecessary context captures in library code",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Library code should use ConfigureAwait(false) to avoid capturing and marshaling back to the original context.");

    // CATGA008: Potential memory leak in event handlers
    private static readonly DiagnosticDescriptor MemoryLeakRule = new(
        id: "CATGA008",
        title: "Potential memory leak detected",
        messageFormat: "Event handler '{0}' may cause memory leak. Ensure proper cleanup of resources and event subscriptions.",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Event handlers should properly dispose resources and unsubscribe from events to prevent memory leaks.");

    // CATGA009: Inefficient LINQ usage
    private static readonly DiagnosticDescriptor LinqPerformanceRule = new(
        id: "CATGA009",
        title: "Inefficient LINQ usage detected",
        messageFormat: "LINQ operation '{0}' creates unnecessary allocations. Consider using for/foreach loops instead.",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "LINQ operations create iterators and intermediate collections. Direct loops are more efficient in performance-critical code.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            BlockingCallRule,
            ValueTaskRule,
            ConfigureAwaitRule,
            MemoryLeakRule,
            LinqPerformanceRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // CATGA005: Detect blocking calls
        context.RegisterOperationAction(AnalyzeBlockingCalls, OperationKind.Invocation);

        // CATGA006: Suggest ValueTask
        context.RegisterSymbolAction(AnalyzeMethodReturnType, SymbolKind.Method);

        // CATGA007: Check ConfigureAwait
        context.RegisterSyntaxNodeAction(AnalyzeAwaitExpression, SyntaxKind.AwaitExpression);

        // CATGA008: Check for memory leaks
        context.RegisterSymbolAction(AnalyzeEventHandlerForLeaks, SymbolKind.Method);

        // CATGA009: Check LINQ performance
        context.RegisterSyntaxNodeAction(AnalyzeLinqUsage,
            SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeBlockingCalls(OperationAnalysisContext context)
    {
        if (context.Operation is not IInvocationOperation invocation)
            return;

        var method = invocation.TargetMethod;
        var methodName = method.Name;

        // Check for blocking patterns
        var isBlockingCall = methodName switch
        {
            "Wait" => method.ContainingType.Name == "Task",
            "Result" => method.ContainingType.Name == "Task",
            "GetResult" => method.ContainingType.Name.Contains("TaskAwaiter"),
            "WaitAll" or "WaitAny" => method.ContainingType.Name == "Task",
            _ => false
        };

        if (!isBlockingCall)
            return;

        // Check if we're in an async method
        var containingMethod = context.ContainingSymbol as IMethodSymbol;
        if (containingMethod?.IsAsync != true)
            return;

        var diagnostic = Diagnostic.Create(
            BlockingCallRule,
            invocation.Syntax.GetLocation(),
            containingMethod.Name,
            methodName);

        context.ReportDiagnostic(diagnostic);
    }

    private static void AnalyzeMethodReturnType(SymbolAnalysisContext context)
    {
        if (context.Symbol is not IMethodSymbol method)
            return;

        // Only check handler methods
        if (!method.Name.EndsWith("Handler") && !method.Name.Contains("Handle"))
            return;

        // Check if returns Task<T>
        if (method.ReturnType is not INamedTypeSymbol returnType)
            return;

        if (returnType.Name != "Task" || !returnType.IsGenericType)
            return;

        // Suggest ValueTask for frequently called handlers
        var diagnostic = Diagnostic.Create(
            ValueTaskRule,
            method.Locations[0],
            method.Name);

        context.ReportDiagnostic(diagnostic);
    }

    private static void AnalyzeAwaitExpression(SyntaxNodeAnalysisContext context)
    {
        var awaitExpr = (AwaitExpressionSyntax)context.Node;

        // Check if it's in a library (not application code)
        var containingMethod = context.SemanticModel.GetEnclosingSymbol(awaitExpr.SpanStart) as IMethodSymbol;
        if (containingMethod == null)
            return;

        // Check if await has ConfigureAwait
        if (awaitExpr.Expression is InvocationExpressionSyntax invocation)
        {
            var memberAccess = invocation.Expression as MemberAccessExpressionSyntax;
            if (memberAccess?.Name.Identifier.Text == "ConfigureAwait")
                return; // Already has ConfigureAwait
        }

        // Check if this is handler/behavior code (library code)
        var containingType = containingMethod.ContainingType;
        var isLibraryCode = containingType.AllInterfaces.Any(i =>
            i.Name.Contains("IRequestHandler") ||
            i.Name.Contains("IEventHandler") ||
            i.Name.Contains("IPipelineBehavior"));

        if (!isLibraryCode)
            return;

        var diagnostic = Diagnostic.Create(
            ConfigureAwaitRule,
            awaitExpr.GetLocation());

        context.ReportDiagnostic(diagnostic);
    }

    private static void AnalyzeEventHandlerForLeaks(SymbolAnalysisContext context)
    {
        if (context.Symbol is not IMethodSymbol method)
            return;

        // Check if it's an event handler
        var implementsEventHandler = method.ContainingType.AllInterfaces
            .Any(i => i.Name.Contains("IEventHandler"));

        if (!implementsEventHandler)
            return;

        // Check for potential leaks (simplified check)
        // In real implementation, would analyze for:
        // - Field references to event sources
        // - Missing IDisposable implementation
        // - Unclosed streams/connections

        // For now, just warn about event handlers without proper cleanup
        var hasDispose = method.ContainingType.AllInterfaces
            .Any(i => i.Name == "IDisposable");

        if (!hasDispose)
        {
            // Only warn if handler has fields (state)
            if (method.ContainingType.GetMembers().OfType<IFieldSymbol>().Any())
            {
                var diagnostic = Diagnostic.Create(
                    MemoryLeakRule,
                    method.Locations[0],
                    method.Name);

                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private static void AnalyzeLinqUsage(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var methodName = memberAccess.Name.Identifier.Text;

        // Check for LINQ methods that create allocations
        var allocatingLinqMethods = new[]
        {
            "Select", "Where", "OrderBy", "OrderByDescending",
            "GroupBy", "ToList", "ToArray", "ToDictionary"
        };

        if (!allocatingLinqMethods.Contains(methodName))
            return;

        // Check if in hot path (handler method)
        var containingMethod = context.SemanticModel.GetEnclosingSymbol(invocation.SpanStart) as IMethodSymbol;
        if (containingMethod?.Name.Contains("Handle") != true)
            return;

        var diagnostic = Diagnostic.Create(
            LinqPerformanceRule,
            invocation.GetLocation(),
            methodName);

        context.ReportDiagnostic(diagnostic);
    }
}

