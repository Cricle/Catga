using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

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
        var compilation = context.Compilation;
        
        // Find all handler types
        var handlerTypes = compilation.GetSymbolsWithName(
            _ => true,
            SymbolFilter.Type)
            .OfType<INamedTypeSymbol>()
            .Where(t => t.AllInterfaces.Any(i => i.Name == "IRequestHandler"))
            .ToList();

        // Group by message type
        var handlersByMessage = handlerTypes
            .Select(t => new
            {
                Handler = t,
                MessageType = GetRequestMessageType(t)
            })
            .Where(x => x.MessageType != null)
            .GroupBy(x => x.MessageType, SymbolEqualityComparer.Default);

        foreach (var group in handlersByMessage)
        {
            var handlers = group.ToList();
            if (handlers.Count > 1)
            {
                // Report error for each duplicate handler
                foreach (var handler in handlers)
                {
                    var locations = handler.Handler.Locations;
                    if (locations.Length > 0)
                    {
                        var diagnostic = Diagnostic.Create(
                            CatgaAnalyzerRules.MultipleSyncHandlers,
                            locations[0],
                            group.Key?.Name ?? "Unknown");

                        context.ReportDiagnostic(diagnostic);
                    }
                }
            }
        }
    }

    private static ITypeSymbol? GetRequestMessageType(INamedTypeSymbol handlerType)
    {
        var requestHandlerInterface = handlerType.AllInterfaces
            .FirstOrDefault(i => i.Name == "IRequestHandler" && i.TypeArguments.Length >= 1);

        return requestHandlerInterface?.TypeArguments[0];
    }
}

