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

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var typeSymbol = (INamedTypeSymbol)context.Symbol;
        
        // Check if type implements IRequest or INotification
        var isRequest = typeSymbol.AllInterfaces.Any(i => i.Name == "IRequest");
        var isNotification = typeSymbol.AllInterfaces.Any(i => i.Name == "INotification");

        if (!isRequest && !isNotification)
            return;

        var typeName = typeSymbol.Name;

        // Check Event naming (past tense)
        if (isNotification && typeName.EndsWith("Event"))
        {
            if (IsPresentTenseVerb(typeName))
            {
                var diagnostic = Diagnostic.Create(
                    CatgaAnalyzerRules.EventShouldBePastTense,
                    typeSymbol.Locations[0],
                    typeName);

                context.ReportDiagnostic(diagnostic);
            }
        }

        // Check Command return type
        if (isRequest && typeName.EndsWith("Command"))
        {
            var requestInterface = typeSymbol.AllInterfaces
                .FirstOrDefault(i => i.Name == "IRequest" && i.TypeArguments.Length == 1);

            if (requestInterface != null)
            {
                var returnType = requestInterface.TypeArguments[0];
                
                // Warn if returning complex domain objects
                if (IsComplexDomainType(returnType))
                {
                    var diagnostic = Diagnostic.Create(
                        CatgaAnalyzerRules.CommandShouldNotReturnData,
                        typeSymbol.Locations[0],
                        typeName);

                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
    }

    private static bool IsPresentTenseVerb(string name)
    {
        // Simple heuristic: check for common present tense patterns
        var verbs = new[] { "Create", "Update", "Delete", "Send", "Process", "Execute", "Handle" };
        
        foreach (var verb in verbs)
        {
            if (name.StartsWith(verb) && !name.StartsWith(verb + "d") && !name.StartsWith(verb + "ed"))
                return true;
        }

        return false;
    }

    private static bool IsComplexDomainType(ITypeSymbol type)
    {
        // Allow primitive types, strings, GUIDs, and simple value types
        if (type.SpecialType != SpecialType.None)
            return false;

        if (type.Name == "Guid" || type.Name == "DateTime" || type.Name == "TimeSpan")
            return false;

        if (type.Name == "String" || type.Name == "Boolean" || type.Name == "Int32" || type.Name == "Int64")
            return false;

        // If it's a class with many properties, it's likely a domain object
        if (type.TypeKind == TypeKind.Class)
        {
            var properties = type.GetMembers().OfType<IPropertySymbol>().Count();
            return properties > 3; // Heuristic: > 3 properties = complex
        }

        return false;
    }
}

