using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Catga.Abstractions;

namespace Catga.SourceGenerator.Analyzers;

/// <summary>Detects multiple handlers for the same IRequest (only allowed for INotification)</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MultipleHandlersAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(CatgaAnalyzerRules.MultipleSyncHandlers);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var handlersByMessage = context.Compilation.GetSymbolsWithName(_ => true, SymbolFilter.Type)
            .OfType<INamedTypeSymbol>()
            .Where(t => t.AllInterfaces.Any(i => i.Name == "IRequestHandler"))
            .Select(t => (Handler: t, MessageType: t.AllInterfaces
                .FirstOrDefault(i => i.Name == "IRequestHandler" && i.TypeArguments.Length >= 1)
                ?.TypeArguments[0]))
            .Where(x => x.MessageType != null)
            .GroupBy(x => x.MessageType, SymbolEqualityComparer.Default);

        foreach (var group in handlersByMessage.Where(g => g.Count() > 1))
            foreach (var handler in group)
                if (handler.Handler.Locations.Length > 0)
                    context.ReportDiagnostic(Diagnostic.Create(
                        CatgaAnalyzerRules.MultipleSyncHandlers,
                        handler.Handler.Locations[0],
                        group.Key?.Name ?? "Unknown"));
    }
}

