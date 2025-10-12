using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Catga.SourceGenerator.Analyzers;

/// <summary>Detects blocking calls in async handlers (.Result, .Wait(), .GetAwaiter().GetResult())</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlockingCallAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(CatgaAnalyzerRules.BlockingCallInHandler);

    private static readonly string[] BlockingMembers = { "Result", "Wait", "GetResult" };

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var methodDecl = (MethodDeclarationSyntax)context.Node;
        if (!methodDecl.Modifiers.Any(SyntaxKind.AsyncKeyword)) return;

        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDecl);
        if (methodSymbol?.ContainingType == null ||
            !methodSymbol.ContainingType.AllInterfaces.Any(i => i.Name == "IRequestHandler" || i.Name == "INotificationHandler"))
            return;

        foreach (var call in methodDecl.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Where(ma => BlockingMembers.Contains(ma.Name.Identifier.Text)))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                CatgaAnalyzerRules.BlockingCallInHandler,
                call.GetLocation(),
                methodSymbol.ContainingType.Name,
                call.Name.Identifier.Text));
        }
    }
}

