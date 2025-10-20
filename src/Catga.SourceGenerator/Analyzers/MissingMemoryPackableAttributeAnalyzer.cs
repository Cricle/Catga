using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Catga.SourceGenerator.Analyzers;

/// <summary>
/// Analyzer that warns when message types don't have [MemoryPackable] attribute for AOT
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MissingMemoryPackableAttributeAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "CATGA001";
    private const string Category = "Usage";

    private static readonly LocalizableString Title = "Message type should be marked with [MemoryPackable] for AOT compatibility";
    private static readonly LocalizableString MessageFormat = "Type '{0}' implements {1} but is not marked with [MemoryPackable]. Add [MemoryPackable] and make it partial for best AOT performance.";
    private static readonly LocalizableString Description = "For Native AOT compatibility and best performance, message types should be annotated with [MemoryPackable] attribute and declared as partial.";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: "https://github.com/Cysharp/MemoryPack");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var namedType = (INamedTypeSymbol)context.Symbol;

        // Skip if already has MemoryPackable attribute
        if (HasMemoryPackableAttribute(namedType))
            return;

        // Check if implements Catga message interfaces
        var implementedInterface = GetCatgaMessageInterface(namedType);
        if (implementedInterface == null)
            return;

        // Skip if type is not accessible (internal/private nested types)
        if (namedType.DeclaredAccessibility != Accessibility.Public &&
            namedType.DeclaredAccessibility != Accessibility.Internal)
            return;

        // Report diagnostic
        var diagnostic = Diagnostic.Create(
            Rule,
            namedType.Locations[0],
            namedType.Name,
            implementedInterface);

        context.ReportDiagnostic(diagnostic);
    }

    private static bool HasMemoryPackableAttribute(INamedTypeSymbol namedType)
    {
        // Manual iteration instead of LINQ for AOT compatibility
        var attributes = namedType.GetAttributes();
        for (int i = 0; i < attributes.Length; i++)
        {
            var attr = attributes[i];
            if (attr.AttributeClass?.Name == "MemoryPackableAttribute" ||
                attr.AttributeClass?.ToDisplayString() == "MemoryPack.MemoryPackableAttribute")
            {
                return true;
            }
        }
        return false;
    }

    private static string? GetCatgaMessageInterface(INamedTypeSymbol namedType)
    {
        foreach (var iface in namedType.AllInterfaces)
        {
            var ifaceName = iface.ToDisplayString();

            if (ifaceName.StartsWith("Catga.Messages.IRequest<"))
                return "IRequest";
            if (ifaceName == "Catga.Messages.IRequest")
                return "IRequest";
            if (ifaceName.StartsWith("Catga.Messages.IEvent"))
                return "IEvent";
        }

        return null;
    }
}

