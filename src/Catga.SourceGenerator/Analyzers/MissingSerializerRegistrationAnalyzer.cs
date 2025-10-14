using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Catga.SourceGenerator.Analyzers;

/// <summary>
/// Analyzer that warns when AddCatga() is called without registering a serializer
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MissingSerializerRegistrationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "CATGA002";
    private const string Category = "Usage";

    private static readonly LocalizableString Title = "Serializer not registered after AddCatga()";
    private static readonly LocalizableString MessageFormat = "AddCatga() requires a serializer. Call .UseMemoryPack() or .UseJson() immediately after AddCatga().";
    private static readonly LocalizableString Description = "Catga requires an IMessageSerializer to be registered. Use .UseMemoryPack() (recommended for AOT) or .UseJson() for serialization.";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: "https://github.com/catga/docs/configuration");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Check if this is AddCatga() call
        if (!IsAddCatgaCall(invocation, context.SemanticModel))
            return;

        // Check if followed by serializer registration
        if (IsFollowedBySerializerRegistration(invocation))
            return;

        // Check if serializer is registered elsewhere in the method
        if (HasSerializerRegistrationInMethod(invocation, context.SemanticModel))
            return;

        // Report diagnostic
        var diagnostic = Diagnostic.Create(
            Rule,
            invocation.GetLocation());

        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsAddCatgaCall(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        var method = symbolInfo.Symbol as IMethodSymbol;

        return method?.Name == "AddCatga" &&
               method.ContainingType?.ToDisplayString().StartsWith("Catga.DependencyInjection") == true;
    }

    private static bool IsFollowedBySerializerRegistration(InvocationExpressionSyntax invocation)
    {
        // Check if this invocation is part of a chain like: AddCatga().UseMemoryPack()
        var parent = invocation.Parent;
        while (parent is MemberAccessExpressionSyntax memberAccess)
        {
            var methodName = memberAccess.Name.Identifier.Text;
            if (methodName == "UseMemoryPack" ||
                methodName == "UseJson" ||
                methodName == "UseMemoryPackSerializer" ||
                methodName == "UseJsonSerializer")
            {
                return true;
            }
            parent = memberAccess.Parent;
        }

        return false;
    }

    private static bool HasSerializerRegistrationInMethod(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        // Find the containing method/block
        var containingMethod = invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (containingMethod == null)
            return false;

        // Look for UseMemoryPack/UseJson/AddSingleton<IMessageSerializer> calls
        // Manual OfType implementation for AOT compatibility
        foreach (var node in containingMethod.DescendantNodes())
        {
            if (node is not InvocationExpressionSyntax inv)
                continue;

            var symbolInfo = semanticModel.GetSymbolInfo(inv);
            var method = symbolInfo.Symbol as IMethodSymbol;

            if (method == null)
                continue;

            // Check for serializer methods
            if (method.Name is "UseMemoryPack" or "UseJson" or "UseMemoryPackSerializer" or "UseJsonSerializer")
                return true;

            // Check for AddSingleton<IMessageSerializer>
            if (method.Name == "AddSingleton" && method.IsGenericMethod && method.TypeArguments.Length > 0)
            {
                var typeArg = method.TypeArguments[0];
                if (typeArg?.Name == "IMessageSerializer")
                    return true;
            }
        }

        return false;
    }
}

