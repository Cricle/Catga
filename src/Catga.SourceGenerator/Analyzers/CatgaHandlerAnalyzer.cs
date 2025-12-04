using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Catga.SourceGenerator.Analyzers;

/// <summary>
/// Analyzer for [CatgaHandler] attribute usage.
/// Reports diagnostics when:
/// - Handler is not partial (CAT5001)
/// - Handler is missing HandleAsyncCore method (CAT5002)
/// - HandleAsyncCore has wrong signature (CAT5003)
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class CatgaHandlerAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor MissingPartialModifier = new(
        id: "CAT5001",
        title: "Handler with [CatgaHandler] must be partial",
        messageFormat: "Class '{0}' has [CatgaHandler] but is not partial. Add 'partial' modifier.",
        category: "Catga.Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Classes with [CatgaHandler] must be partial to allow source generation.");

    public static readonly DiagnosticDescriptor MissingHandleAsyncCore = new(
        id: "CAT5002",
        title: "Handler with [CatgaHandler] must implement HandleAsyncCore",
        messageFormat: "Class '{0}' has [CatgaHandler] but is missing 'HandleAsyncCore' method. Add: private async Task<CatgaResult<TResponse>> HandleAsyncCore({1} request, CancellationToken ct)",
        category: "Catga.Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Classes with [CatgaHandler] must implement HandleAsyncCore method containing the business logic.");

    public static readonly DiagnosticDescriptor WrongHandleAsyncCoreSignature = new(
        id: "CAT5003",
        title: "HandleAsyncCore has wrong signature",
        messageFormat: "Method 'HandleAsyncCore' in '{0}' has wrong signature. Expected: private async Task<CatgaResult<{1}>> HandleAsyncCore({2} request, CancellationToken ct)",
        category: "Catga.Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "HandleAsyncCore must match the expected signature based on IRequestHandler interface.");

    public static readonly DiagnosticDescriptor HandlerNotImplementingInterface = new(
        id: "CAT5004",
        title: "Handler with [CatgaHandler] must implement IRequestHandler",
        messageFormat: "Class '{0}' has [CatgaHandler] but does not implement IRequestHandler<TRequest> or IRequestHandler<TRequest, TResponse>",
        category: "Catga.Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Classes with [CatgaHandler] must implement IRequestHandler interface.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            MissingPartialModifier,
            MissingHandleAsyncCore,
            WrongHandleAsyncCoreSignature,
            HandlerNotImplementingInterface);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeClass, SyntaxKind.ClassDeclaration);
    }

    private static void AnalyzeClass(SyntaxNodeAnalysisContext context)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDecl);
        if (classSymbol is null) return;

        // Check if has [CatgaHandler] attribute
        var hasCatgaHandler = classSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.Name == "CatgaHandlerAttribute");

        if (!hasCatgaHandler) return;

        // Check 1: Must be partial
        if (!classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                MissingPartialModifier,
                classDecl.Identifier.GetLocation(),
                classSymbol.Name));
            return;
        }

        // Check 2: Must implement IRequestHandler
        var handlerInterface = classSymbol.AllInterfaces
            .FirstOrDefault(i =>
                i.OriginalDefinition.ToDisplayString().StartsWith("Catga.Handlers.IRequestHandler") ||
                i.OriginalDefinition.ToDisplayString().StartsWith("Catga.Abstractions.IRequestHandler"));

        if (handlerInterface is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                HandlerNotImplementingInterface,
                classDecl.Identifier.GetLocation(),
                classSymbol.Name));
            return;
        }

        var requestType = handlerInterface.TypeArguments[0];
        var responseType = handlerInterface.TypeArguments.Length > 1
            ? handlerInterface.TypeArguments[1]
            : null;

        // Check 3: Must have HandleAsyncCore
        var coreMethod = classSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => m.Name == "HandleAsyncCore");

        if (coreMethod is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                MissingHandleAsyncCore,
                classDecl.Identifier.GetLocation(),
                classSymbol.Name,
                requestType.ToDisplayString()));
            return;
        }

        // Check 4: HandleAsyncCore signature
        var expectedReturnType = responseType is not null
            ? $"System.Threading.Tasks.Task<Catga.CatgaResult<{responseType.ToDisplayString()}>>"
            : "System.Threading.Tasks.Task<Catga.CatgaResult>";

        var actualReturnType = coreMethod.ReturnType.ToDisplayString();

        // Check parameters
        if (coreMethod.Parameters.Length < 1 ||
            !SymbolEqualityComparer.Default.Equals(coreMethod.Parameters[0].Type, requestType))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                WrongHandleAsyncCoreSignature,
                coreMethod.Locations.FirstOrDefault() ?? classDecl.Identifier.GetLocation(),
                classSymbol.Name,
                responseType?.ToDisplayString() ?? "void",
                requestType.ToDisplayString()));
        }
    }
}
