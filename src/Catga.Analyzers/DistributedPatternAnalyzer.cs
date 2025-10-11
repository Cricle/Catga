using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Catga.Analyzers;

/// <summary>
/// Analyzer for detecting distributed system pattern issues
/// Ensures proper use of Outbox, Inbox, Idempotency, and other distributed patterns
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class DistributedPatternAnalyzer : DiagnosticAnalyzer
{
    // CATGA401: Outbox pattern not used for external calls
    private static readonly DiagnosticDescriptor MissingOutboxRule = new(
        id: "CATGA401",
        title: "External call should use Outbox pattern",
        messageFormat: "Handler '{0}' makes external calls but doesn't use Outbox pattern. Consider using OutboxBehavior for reliability.",
        category: "Distributed",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Outbox pattern ensures message delivery even if the handler fails after the external call.");

    // CATGA402: Missing idempotency for commands
    private static readonly DiagnosticDescriptor MissingIdempotencyRule = new(
        id: "CATGA402",
        title: "Command handler should be idempotent",
        messageFormat: "Command handler '{0}' is not idempotent. Add [Idempotent] attribute or implement idempotency checking.",
        category: "Distributed",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "In distributed systems, commands may be retried. Handlers must be idempotent to prevent duplicate operations.");

    // CATGA403: Message could be lost without persistence
    private static readonly DiagnosticDescriptor MessageLossRiskRule = new(
        id: "CATGA403",
        title: "Message publishing without persistence may lose messages",
        messageFormat: "Publishing events without Outbox pattern may lose messages if the process crashes",
        category: "Distributed",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Messages published directly (without Outbox) are lost if the process crashes before they're sent.");

    // CATGA404: Consider using distributed lock
    private static readonly DiagnosticDescriptor ConsiderDistributedLockRule = new(
        id: "CATGA404",
        title: "Consider using distributed lock for critical section",
        messageFormat: "Critical section in handler '{0}' may need distributed lock in multi-instance deployment",
        category: "Distributed",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "In-memory locks don't work across multiple instances. Use distributed lock for critical sections.");

    // CATGA405: Missing retry policy
    private static readonly DiagnosticDescriptor MissingRetryRule = new(
        id: "CATGA405",
        title: "External call should have retry policy",
        messageFormat: "External call to '{0}' should use RetryBehavior or have explicit retry logic",
        category: "Distributed",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "External calls can fail transiently. Use retry policies to handle temporary failures.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            MissingOutboxRule,
            MissingIdempotencyRule,
            MessageLossRiskRule,
            ConsiderDistributedLockRule,
            MissingRetryRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSymbolAction(AnalyzeHandler, SymbolKind.NamedType);
        context.RegisterSyntaxNodeAction(AnalyzeLockStatement, SyntaxKind.LockStatement);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private void AnalyzeHandler(SymbolAnalysisContext context)
    {
        var namedType = (INamedTypeSymbol)context.Symbol;

        // Check if type is a handler
        if (!IsHandler(namedType))
            return;

        // Check for idempotency attribute on command handlers
        if (IsCommandHandler(namedType))
        {
            var hasIdempotentAttribute = namedType.GetAttributes()
                .Any(a => a.AttributeClass?.Name == "IdempotentAttribute");

            // Check if handler implements idempotency checking
            var hasIdempotencyLogic = HasIdempotencyCheck(namedType);

            if (!hasIdempotentAttribute && !hasIdempotencyLogic)
            {
                var diagnostic = Diagnostic.Create(
                    MissingIdempotencyRule,
                    namedType.Locations.FirstOrDefault(),
                    namedType.Name);
                context.ReportDiagnostic(diagnostic);
            }
        }

        // Check for Outbox usage when making external calls
        if (MakesExternalCalls(namedType) && !UsesOutbox(namedType))
        {
            var diagnostic = Diagnostic.Create(
                MissingOutboxRule,
                namedType.Locations.FirstOrDefault(),
                namedType.Name);
            context.ReportDiagnostic(diagnostic);
        }
    }

    private void AnalyzeLockStatement(SyntaxNodeAnalysisContext context)
    {
        var lockStatement = (LockStatementSyntax)context.Node;

        // Check if we're in a handler
        var containingType = context.SemanticModel.GetEnclosingSymbol(lockStatement.SpanStart)?.ContainingType;
        if (containingType == null || !IsHandler(containingType))
            return;

        // Warn about potential need for distributed lock
        var diagnostic = Diagnostic.Create(
            ConsiderDistributedLockRule,
            lockStatement.GetLocation(),
            containingType.Name);
        context.ReportDiagnostic(diagnostic);
    }

    private void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);

        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return;

        // Check for event publishing without Outbox
        if (IsPublishMethod(methodSymbol))
        {
            var containingMethod = context.SemanticModel.GetEnclosingSymbol(invocation.SpanStart) as IMethodSymbol;
            if (containingMethod != null && !IsWithinOutboxContext(context, invocation))
            {
                var diagnostic = Diagnostic.Create(
                    MessageLossRiskRule,
                    invocation.GetLocation());
                context.ReportDiagnostic(diagnostic);
            }
        }

        // Check for external HTTP calls without retry
        if (IsExternalHttpCall(methodSymbol))
        {
            var containingMethod = context.SemanticModel.GetEnclosingSymbol(invocation.SpanStart) as IMethodSymbol;
            if (containingMethod != null && !HasRetryPolicy(containingMethod))
            {
                var diagnostic = Diagnostic.Create(
                    MissingRetryRule,
                    invocation.GetLocation(),
                    methodSymbol.Name);
                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private static bool IsHandler(INamedTypeSymbol type)
    {
        foreach (var @interface in type.AllInterfaces)
        {
            var interfaceName = @interface.ToDisplayString();
            if (interfaceName.Contains("IRequestHandler") || interfaceName.Contains("IEventHandler"))
                return true;
        }
        return false;
    }

    private static bool IsCommandHandler(INamedTypeSymbol type)
    {
        // Command handlers implement IRequestHandler where request doesn't implement IQuery
        foreach (var @interface in type.AllInterfaces)
        {
            var interfaceName = @interface.ToDisplayString();
            if (interfaceName.Contains("IRequestHandler"))
            {
                // Check if it's not a query
                if (@interface.TypeArguments.Length > 0)
                {
                    var requestType = @interface.TypeArguments[0];
                    var isQuery = requestType.AllInterfaces.Any(i => i.Name == "IQuery");
                    if (!isQuery)
                        return true;
                }
            }
        }
        return false;
    }

    private static bool HasIdempotencyCheck(INamedTypeSymbol type)
    {
        // Check if type uses IIdempotencyStore or similar
        var members = type.GetMembers();
        foreach (var member in members)
        {
            if (member is IFieldSymbol field)
            {
                var fieldType = field.Type.ToDisplayString();
                if (fieldType.Contains("IdempotencyStore") || fieldType.Contains("IIdempotency"))
                    return true;
            }
        }
        return false;
    }

    private static bool MakesExternalCalls(INamedTypeSymbol type)
    {
        // Check for fields like HttpClient, IMessageTransport, etc.
        var members = type.GetMembers();
        foreach (var member in members)
        {
            if (member is IFieldSymbol or IPropertySymbol)
            {
                var memberType = member switch
                {
                    IFieldSymbol f => f.Type.ToDisplayString(),
                    IPropertySymbol p => p.Type.ToDisplayString(),
                    _ => ""
                };

                if (memberType.Contains("HttpClient") ||
                    memberType.Contains("IMessageTransport") ||
                    memberType.Contains("IEventBus"))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool UsesOutbox(INamedTypeSymbol type)
    {
        // Check if type has OutboxBehavior or IOutboxStore
        var members = type.GetMembers();
        foreach (var member in members)
        {
            if (member is IFieldSymbol or IPropertySymbol)
            {
                var memberType = member switch
                {
                    IFieldSymbol f => f.Type.ToDisplayString(),
                    IPropertySymbol p => p.Type.ToDisplayString(),
                    _ => ""
                };

                if (memberType.Contains("IOutboxStore") || memberType.Contains("OutboxBehavior"))
                    return true;
            }
        }

        // Check attributes
        return type.GetAttributes().Any(a => a.AttributeClass?.Name == "UseOutboxAttribute");
    }

    private static bool IsPublishMethod(IMethodSymbol method)
    {
        return method.Name is "PublishAsync" or "Publish" &&
               method.ContainingType?.ToDisplayString().Contains("Mediator") == true;
    }

    private static bool IsWithinOutboxContext(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation)
    {
        // Check if we're within a using statement with OutboxScope or similar
        var usingStatement = invocation.Ancestors().OfType<UsingStatementSyntax>().FirstOrDefault();
        if (usingStatement?.Expression != null)
        {
            var symbolInfo = context.SemanticModel.GetSymbolInfo(usingStatement.Expression);
            if (symbolInfo.Symbol?.ContainingType?.Name.Contains("Outbox") == true)
                return true;
        }
        return false;
    }

    private static bool IsExternalHttpCall(IMethodSymbol method)
    {
        var containingType = method.ContainingType?.ToDisplayString();
        return containingType?.StartsWith("System.Net.Http.HttpClient") == true;
    }

    private static bool HasRetryPolicy(IMethodSymbol method)
    {
        // Check for [Retry] attribute
        return method.GetAttributes().Any(a => a.AttributeClass?.Name == "RetryAttribute");
    }
}

