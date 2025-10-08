using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Catga.Analyzers;

/// <summary>
/// Best practice analyzers for Catga framework
/// Enforces coding standards and best practices
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class CatgaBestPracticeAnalyzers : DiagnosticAnalyzer
{
    // CATGA010: Missing [CatgaHandler] attribute
    private static readonly DiagnosticDescriptor HandlerAttributeRule = new(
        id: "CATGA010",
        title: "Consider adding [CatgaHandler] attribute for clarity",
        messageFormat: "Handler '{0}' should have [CatgaHandler] attribute for explicit documentation.",
        category: "Style",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Explicit [CatgaHandler] attributes make handler registration more discoverable.");

    // CATGA011: Handler timeout too long
    private static readonly DiagnosticDescriptor HandlerTimeoutRule = new(
        id: "CATGA011",
        title: "Handler timeout may be too long",
        messageFormat: "Handler '{0}' has no timeout or a very long timeout. Consider adding a reasonable timeout to prevent hanging requests.",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Handlers should have reasonable timeouts to prevent resource exhaustion.");

    // CATGA012: Synchronous I/O detected
    private static readonly DiagnosticDescriptor SyncIORule = new(
        id: "CATGA012",
        title: "Synchronous I/O operation detected",
        messageFormat: "Synchronous I/O method '{0}' detected. Use async version to avoid blocking threads.",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Synchronous I/O blocks threads. Always use async I/O in async handlers.");

    // CATGA013: Missing idempotency for critical commands
    private static readonly DiagnosticDescriptor IdempotencyRule = new(
        id: "CATGA013",
        title: "Critical command should be idempotent",
        messageFormat: "Command '{0}' modifies state but lacks idempotency handling. Consider implementing IIdempotentCommand.",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Commands that modify state should be idempotent to handle retries safely.");

    // CATGA014: Saga state too large
    private static readonly DiagnosticDescriptor SagaStateSizeRule = new(
        id: "CATGA014",
        title: "Saga state may be too large",
        messageFormat: "Saga state '{0}' has many properties. Consider splitting into smaller sagas or using references instead of full data.",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Large saga states impact serialization performance and storage costs.");

    // CATGA015: Unhandled domain events
    private static readonly DiagnosticDescriptor UnhandledEventRule = new(
        id: "CATGA015",
        title: "Domain event may not have any handlers",
        messageFormat: "Event '{0}' is published but no handlers found. Ensure at least one event handler exists.",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Published events should have at least one handler, otherwise they serve no purpose.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            HandlerAttributeRule,
            HandlerTimeoutRule,
            SyncIORule,
            IdempotencyRule,
            SagaStateSizeRule,
            UnhandledEventRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // CATGA010: Check for missing attribute
        context.RegisterSymbolAction(AnalyzeHandlerAttribute, SymbolKind.NamedType);

        // CATGA011: Check timeout
        context.RegisterSymbolAction(AnalyzeHandlerTimeout, SymbolKind.Method);

        // CATGA012: Detect sync I/O
        context.RegisterSyntaxNodeAction(AnalyzeSyncIO, SyntaxKind.InvocationExpression);

        // CATGA013: Check idempotency
        context.RegisterSymbolAction(AnalyzeCommandIdempotency, SymbolKind.NamedType);

        // CATGA014: Check saga state size
        context.RegisterSymbolAction(AnalyzeSagaStateSize, SymbolKind.NamedType);

        // CATGA015: Check for unhandled events
        context.RegisterSymbolAction(AnalyzeUnhandledEvents, SymbolKind.NamedType);
    }

    private static void AnalyzeHandlerAttribute(SymbolAnalysisContext context)
    {
        if (context.Symbol is not INamedTypeSymbol type)
            return;

        // Check if it's a handler
        var isHandler = type.AllInterfaces.Any(i =>
            i.Name.Contains("IRequestHandler") || i.Name.Contains("IEventHandler"));

        if (!isHandler)
            return;

        // Check if has CatgaHandler attribute
        var hasAttribute = type.GetAttributes()
            .Any(a => a.AttributeClass?.Name == "CatgaHandlerAttribute");

        if (!hasAttribute)
        {
            var diagnostic = Diagnostic.Create(
                HandlerAttributeRule,
                type.Locations[0],
                type.Name);

            context.ReportDiagnostic(diagnostic);
        }
    }

    private static void AnalyzeHandlerTimeout(SymbolAnalysisContext context)
    {
        if (context.Symbol is not IMethodSymbol method)
            return;

        if (method.Name != "HandleAsync")
            return;

        // Check if handler has timeout logic
        // This is a simplified check - real implementation would analyze method body
        // for CancellationToken.ThrowIfCancellationRequested() calls or timeout setup

        // For now, just warn if no timeout attribute
        var hasTimeout = method.GetAttributes()
            .Any(a => a.AttributeClass?.Name.Contains("Timeout") == true);

        if (!hasTimeout)
        {
            var diagnostic = Diagnostic.Create(
                HandlerTimeoutRule,
                method.Locations[0],
                method.Name);

            context.ReportDiagnostic(diagnostic);
        }
    }

    private static void AnalyzeSyncIO(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is not IMethodSymbol method)
            return;

        // Check for sync I/O methods
        var syncIOMethods = new[]
        {
            "Read", "Write", "ReadAllText", "WriteAllText",
            "ReadAllBytes", "WriteAllBytes", "Copy",
            "ExecuteNonQuery", "ExecuteScalar", "ExecuteReader"
        };

        var isSyncIO = syncIOMethods.Contains(method.Name) &&
                       !method.Name.EndsWith("Async");

        if (!isSyncIO)
            return;

        // Check if in async method
        var containingMethod = context.SemanticModel.GetEnclosingSymbol(invocation.SpanStart) as IMethodSymbol;
        if (containingMethod?.IsAsync != true)
            return;

        var diagnostic = Diagnostic.Create(
            SyncIORule,
            invocation.GetLocation(),
            method.Name);

        context.ReportDiagnostic(diagnostic);
    }

    private static void AnalyzeCommandIdempotency(SymbolAnalysisContext context)
    {
        if (context.Symbol is not INamedTypeSymbol type)
            return;

        // Check if it's a command
        var isCommand = type.AllInterfaces.Any(i => i.Name.Contains("ICommand"));
        if (!isCommand)
            return;

        // Check for critical operations (Create, Update, Delete, Payment, etc.)
        var isCritical = type.Name.Contains("Create") ||
                        type.Name.Contains("Update") ||
                        type.Name.Contains("Delete") ||
                        type.Name.Contains("Payment") ||
                        type.Name.Contains("Process");

        if (!isCritical)
            return;

        // Check if implements idempotency
        var hasIdempotency = type.AllInterfaces.Any(i =>
            i.Name.Contains("IIdempotent"));

        if (!hasIdempotency)
        {
            var diagnostic = Diagnostic.Create(
                IdempotencyRule,
                type.Locations[0],
                type.Name);

            context.ReportDiagnostic(diagnostic);
        }
    }

    private static void AnalyzeSagaStateSize(SymbolAnalysisContext context)
    {
        if (context.Symbol is not INamedTypeSymbol type)
            return;

        // Check if it's a saga state
        if (!type.Name.EndsWith("SagaState") && !type.Name.EndsWith("State"))
            return;

        // Count properties
        var properties = type.GetMembers().OfType<IPropertySymbol>();
        var propertyCount = properties.Count();

        if (propertyCount > 20) // Threshold: 20 properties
        {
            var diagnostic = Diagnostic.Create(
                SagaStateSizeRule,
                type.Locations[0],
                type.Name);

            context.ReportDiagnostic(diagnostic);
        }
    }

    private static void AnalyzeUnhandledEvents(SymbolAnalysisContext context)
    {
        if (context.Symbol is not INamedTypeSymbol type)
            return;

        // Check if it's an event
        var isEvent = type.AllInterfaces.Any(i => i.Name.Contains("IEvent"));
        if (!isEvent)
            return;

        // This would require a full compilation analysis to check if handlers exist
        // Simplified: Just check if event name suggests it should have handlers
        if (type.Name.Contains("Created") ||
            type.Name.Contains("Updated") ||
            type.Name.Contains("Deleted") ||
            type.Name.Contains("Completed"))
        {
            // In real implementation, would check compilation for handlers
            // For now, just a placeholder for the analyzer structure

            // var diagnostic = Diagnostic.Create(
            //     UnhandledEventRule,
            //     type.Locations[0],
            //     type.Name);
            //
            // context.ReportDiagnostic(diagnostic);
        }
    }
}

