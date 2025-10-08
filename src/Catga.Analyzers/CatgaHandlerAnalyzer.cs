using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Catga.Analyzers;

/// <summary>
/// Analyzer for Catga handlers - detects common issues
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class CatgaHandlerAnalyzer : DiagnosticAnalyzer
{
    // Diagnostic IDs
    public const string HandlerNotRegisteredDiagnosticId = "CATGA001";
    public const string InvalidHandlerSignatureDiagnosticId = "CATGA002";
    public const string MissingAsyncSuffixDiagnosticId = "CATGA003";
    public const string MissingCancellationTokenDiagnosticId = "CATGA004";

    // Diagnostic descriptors
    private static readonly DiagnosticDescriptor HandlerNotRegisteredRule = new DiagnosticDescriptor(
        id: HandlerNotRegisteredDiagnosticId,
        title: "Handler not registered",
        messageFormat: "Handler '{0}' implements IRequestHandler or IEventHandler but may not be registered. Ensure AddGeneratedHandlers() is called or register manually.",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Handlers should be registered either via source generator or manually.");

    private static readonly DiagnosticDescriptor InvalidHandlerSignatureRule = new DiagnosticDescriptor(
        id: InvalidHandlerSignatureDiagnosticId,
        title: "Invalid handler method signature",
        messageFormat: "Handler method '{0}' has invalid signature. Must be: Task<CatgaResult<TResponse>> HandleAsync(TRequest, CancellationToken)",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Handler methods must follow the correct signature pattern.");

    private static readonly DiagnosticDescriptor MissingAsyncSuffixRule = new DiagnosticDescriptor(
        id: MissingAsyncSuffixDiagnosticId,
        title: "Async method should end with 'Async'",
        messageFormat: "Handler method '{0}' returns Task but doesn't end with 'Async'",
        category: "Naming",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Async methods should end with 'Async' suffix for clarity.");

    private static readonly DiagnosticDescriptor MissingCancellationTokenRule = new DiagnosticDescriptor(
        id: MissingCancellationTokenDiagnosticId,
        title: "Handler missing CancellationToken parameter",
        messageFormat: "Handler method '{0}' should accept CancellationToken as last parameter",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Handlers should support cancellation for better async control.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            HandlerNotRegisteredRule,
            InvalidHandlerSignatureRule,
            MissingAsyncSuffixRule,
            MissingCancellationTokenRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Analyze class declarations for handler implementation
        context.RegisterSyntaxNodeAction(AnalyzeClassDeclaration, SyntaxKind.ClassDeclaration);
        
        // Analyze method declarations for handler methods
        context.RegisterSyntaxNodeAction(AnalyzeMethodDeclaration, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeClassDeclaration(SyntaxNodeAnalysisContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration);

        if (classSymbol == null)
            return;

        // Check if class implements IRequestHandler or IEventHandler
        var implementsHandler = classSymbol.AllInterfaces.Any(i =>
        {
            var name = i.OriginalDefinition.ToDisplayString();
            return name == "Catga.Handlers.IRequestHandler<TRequest, TResponse>" ||
                   name == "Catga.Handlers.IEventHandler<TEvent>";
        });

        if (!implementsHandler)
            return;

        // Info: Handler detected (could check if registered, but that's complex)
        // For now, just inform that source generator should handle registration
        var diagnostic = Diagnostic.Create(
            HandlerNotRegisteredRule,
            classDeclaration.Identifier.GetLocation(),
            classSymbol.Name);

        context.ReportDiagnostic(diagnostic);
    }

    private static void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration);

        if (methodSymbol == null)
            return;

        // Check if this is a HandleAsync method
        if (methodSymbol.Name != "HandleAsync")
            return;

        // Check if containing class implements IRequestHandler or IEventHandler
        var containingType = methodSymbol.ContainingType;
        var implementsHandler = containingType.AllInterfaces.Any(i =>
        {
            var name = i.OriginalDefinition.ToDisplayString();
            return name == "Catga.Handlers.IRequestHandler<TRequest, TResponse>" ||
                   name == "Catga.Handlers.IEventHandler<TEvent>";
        });

        if (!implementsHandler)
            return;

        // Check method signature
        AnalyzeHandlerMethod(context, methodDeclaration, methodSymbol);
    }

    private static void AnalyzeHandlerMethod(
        SyntaxNodeAnalysisContext context,
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol)
    {
        // Check 1: Return type should be Task or Task<CatgaResult<T>>
        var returnType = methodSymbol.ReturnType;
        var isTask = returnType.Name == "Task";
        
        if (!isTask)
        {
            var diagnostic = Diagnostic.Create(
                InvalidHandlerSignatureRule,
                methodDeclaration.Identifier.GetLocation(),
                methodSymbol.Name);
            context.ReportDiagnostic(diagnostic);
            return;
        }

        // Check 2: Method should end with 'Async'
        if (!methodSymbol.Name.EndsWith("Async"))
        {
            var diagnostic = Diagnostic.Create(
                MissingAsyncSuffixRule,
                methodDeclaration.Identifier.GetLocation(),
                methodSymbol.Name);
            context.ReportDiagnostic(diagnostic);
        }

        // Check 3: Should have CancellationToken parameter
        var parameters = methodSymbol.Parameters;
        var hasCancellationToken = parameters.Any(p =>
            p.Type.Name == "CancellationToken");

        if (!hasCancellationToken && parameters.Length < 2)
        {
            var diagnostic = Diagnostic.Create(
                MissingCancellationTokenRule,
                methodDeclaration.Identifier.GetLocation(),
                methodSymbol.Name);
            context.ReportDiagnostic(diagnostic);
        }
    }
}
