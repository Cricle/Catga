using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Catga.SourceGenerator.Analyzers;

/// <summary>Enforces naming conventions for Commands, Queries, and Events</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class NamingConventionAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            CatgaAnalyzerRules.CommandShouldNotReturnData,
            CatgaAnalyzerRules.EventShouldBePastTense);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static readonly string[] Verbs = { "Create", "Update", "Delete", "Send", "Process", "Execute", "Handle" };

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var typeSymbol = (INamedTypeSymbol)context.Symbol;
        var typeName = typeSymbol.Name;
        var isRequest = typeSymbol.AllInterfaces.Any(i => i.Name == "IRequest");
        var isNotification = typeSymbol.AllInterfaces.Any(i => i.Name == "INotification");

        if (!isRequest && !isNotification) return;

        // Check Event naming (past tense)
        if (isNotification && typeName.EndsWith("Event") &&
            Verbs.Any(v => typeName.StartsWith(v) && !typeName.StartsWith(v + "d") && !typeName.StartsWith(v + "ed")))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                CatgaAnalyzerRules.EventShouldBePastTense, typeSymbol.Locations[0], typeName));
        }

        // Check Command return type
        if (isRequest && typeName.EndsWith("Command"))
        {
            var returnType = typeSymbol.AllInterfaces
                .FirstOrDefault(i => i.Name == "IRequest" && i.TypeArguments.Length == 1)
                ?.TypeArguments[0];

            if (returnType != null &&
                returnType.SpecialType == SpecialType.None &&
                returnType.Name != "Guid" && returnType.Name != "DateTime" && returnType.Name != "TimeSpan" &&
                returnType.TypeKind == TypeKind.Class &&
                returnType.GetMembers().OfType<IPropertySymbol>().Count() > 3)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    CatgaAnalyzerRules.CommandShouldNotReturnData, typeSymbol.Locations[0], typeName));
            }
        }
    }
}

