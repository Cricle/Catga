using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Catga.Analyzers;

/// <summary>
/// Analyzer for detecting AOT compatibility issues
/// Ensures code is compatible with Native AOT compilation
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class AotCompatibilityAnalyzer : DiagnosticAnalyzer
{
    // CATGA301: Reflection usage detected
    private static readonly DiagnosticDescriptor ReflectionUsageRule = new(
        id: "CATGA301",
        title: "Reflection usage may not work with AOT",
        messageFormat: "Reflection API '{0}' may not work correctly with Native AOT. Consider using source generators instead.",
        category: "AOT",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Reflection relies on runtime type information which may be trimmed in AOT compilation. Use source generators or static registration instead.");

    // CATGA302: Dynamic code generation detected
    private static readonly DiagnosticDescriptor DynamicCodeRule = new(
        id: "CATGA302",
        title: "Dynamic code generation not supported in AOT",
        messageFormat: "Dynamic code generation using '{0}' is not supported in Native AOT.",
        category: "AOT",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Dynamic code generation (Emit, Expression compilation) is not supported in AOT. Use compile-time code generation instead.");

    // CATGA303: JSON serialization without context
    private static readonly DiagnosticDescriptor JsonWithoutContextRule = new(
        id: "CATGA303",
        title: "JSON serialization should use JsonSerializerContext for AOT",
        messageFormat: "JsonSerializer.{0} should use source-generated JsonSerializerContext for AOT compatibility.",
        category: "AOT",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "JSON serialization without JsonSerializerContext uses reflection. Use source-generated context for AOT.");

    // CATGA304: Consider using MemoryPack
    private static readonly DiagnosticDescriptor PreferMemoryPackRule = new(
        id: "CATGA304",
        title: "Consider using MemoryPack for better AOT performance",
        messageFormat: "Type '{0}' uses JSON serialization. Consider MemoryPack for better AOT performance and zero allocation.",
        category: "AOT",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "MemoryPack is fully AOT-compatible and faster than JSON serialization.");

    // CATGA305: Unsupported API usage
    private static readonly DiagnosticDescriptor UnsupportedApiRule = new(
        id: "CATGA305",
        title: "API not supported in Native AOT",
        messageFormat: "API '{0}' is not supported or unreliable in Native AOT environments.",
        category: "AOT",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Some APIs don't work correctly with AOT. Check Microsoft's AOT compatibility documentation.");

    // CATGA306: Missing AOT attributes
    private static readonly DiagnosticDescriptor MissingAotAttributesRule = new(
        id: "CATGA306",
        title: "Method should have AOT compatibility attributes",
        messageFormat: "Method '{0}' uses {1} and should be marked with [RequiresUnreferencedCode] or [RequiresDynamicCode].",
        category: "AOT",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Methods using incompatible APIs should be marked with appropriate AOT attributes to warn consumers.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            ReflectionUsageRule,
            DynamicCodeRule,
            JsonWithoutContextRule,
            PreferMemoryPackRule,
            UnsupportedApiRule,
            MissingAotAttributesRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeTypeOf, SyntaxKind.TypeOfExpression);
    }

    private void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);
        
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return;

        var containingType = methodSymbol.ContainingType?.ToDisplayString();
        var methodName = methodSymbol.Name;

        // Check for reflection APIs
        if (IsReflectionApi(containingType, methodName))
        {
            var diagnostic = Diagnostic.Create(
                ReflectionUsageRule,
                invocation.GetLocation(),
                $"{containingType}.{methodName}");
            context.ReportDiagnostic(diagnostic);
            
            CheckForMissingAotAttributes(context, invocation, "reflection");
        }

        // Check for dynamic code generation
        if (IsDynamicCodeApi(containingType, methodName))
        {
            var diagnostic = Diagnostic.Create(
                DynamicCodeRule,
                invocation.GetLocation(),
                $"{containingType}.{methodName}");
            context.ReportDiagnostic(diagnostic);
        }

        // Check for JSON serialization without context
        if (IsJsonSerializerCall(containingType, methodName))
        {
            if (!HasJsonSerializerContextParameter(methodSymbol))
            {
                var diagnostic = Diagnostic.Create(
                    JsonWithoutContextRule,
                    invocation.GetLocation(),
                    methodName);
                context.ReportDiagnostic(diagnostic);
            }
        }

        // Check for unsupported APIs
        if (IsUnsupportedInAot(containingType, methodName))
        {
            var diagnostic = Diagnostic.Create(
                UnsupportedApiRule,
                invocation.GetLocation(),
                $"{containingType}.{methodName}");
            context.ReportDiagnostic(diagnostic);
        }
    }

    private void AnalyzeTypeOf(SyntaxNodeAnalysisContext context)
    {
        var typeOfExpr = (TypeOfExpressionSyntax)context.Node;
        
        // typeof() itself is OK, but check how it's used
        var parent = typeOfExpr.Parent;
        
        // Check if typeof is used with GetMethod, GetProperty, etc.
        if (parent is MemberAccessExpressionSyntax memberAccess)
        {
            if (memberAccess.Name.Identifier.ValueText is "GetMethod" or "GetProperty" or "GetField")
            {
                var diagnostic = Diagnostic.Create(
                    ReflectionUsageRule,
                    typeOfExpr.GetLocation(),
                    $"typeof(...).{memberAccess.Name}");
                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private static bool IsReflectionApi(string? containingType, string methodName)
    {
        if (containingType == null) return false;

        // System.Type reflection methods
        if (containingType == "System.Type")
        {
            return methodName is "GetMethod" or "GetProperty" or "GetField" or 
                   "GetConstructor" or "GetMethods" or "GetProperties" or 
                   "GetFields" or "GetConstructors" or "InvokeMember";
        }

        // System.Reflection APIs
        if (containingType.StartsWith("System.Reflection"))
        {
            return methodName is "Invoke" or "GetValue" or "SetValue";
        }

        return false;
    }

    private static bool IsDynamicCodeApi(string? containingType, string methodName)
    {
        if (containingType == null) return false;

        // Expression compilation
        if (containingType.StartsWith("System.Linq.Expressions.Expression"))
        {
            return methodName == "Compile";
        }

        // IL Emit
        if (containingType.StartsWith("System.Reflection.Emit"))
        {
            return true;
        }

        return false;
    }

    private static bool IsJsonSerializerCall(string? containingType, string methodName)
    {
        return containingType == "System.Text.Json.JsonSerializer" &&
               (methodName is "Serialize" or "Deserialize" or "SerializeAsync" or "DeserializeAsync");
    }

    private static bool HasJsonSerializerContextParameter(IMethodSymbol methodSymbol)
    {
        // Check if any parameter is JsonSerializerContext or JsonTypeInfo
        foreach (var parameter in methodSymbol.Parameters)
        {
            var paramType = parameter.Type.ToDisplayString();
            if (paramType.Contains("JsonSerializerContext") || paramType.Contains("JsonTypeInfo"))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsUnsupportedInAot(string? containingType, string methodName)
    {
        if (containingType == null) return false;

        // AppDomain methods
        if (containingType == "System.AppDomain")
        {
            return methodName is "CreateInstanceAndUnwrap" or "CreateInstance";
        }

        // Assembly loading
        if (containingType == "System.Reflection.Assembly")
        {
            return methodName is "LoadFile" or "LoadFrom";
        }

        return false;
    }

    private void CheckForMissingAotAttributes(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation, string apiType)
    {
        // Get the containing method
        var containingMethod = context.SemanticModel.GetEnclosingSymbol(invocation.SpanStart) as IMethodSymbol;
        if (containingMethod == null)
            return;

        // Check if method has appropriate AOT attributes
        var hasRequiresUnreferencedCode = containingMethod.GetAttributes()
            .Any(a => a.AttributeClass?.Name == "RequiresUnreferencedCodeAttribute");
        
        var hasRequiresDynamicCode = containingMethod.GetAttributes()
            .Any(a => a.AttributeClass?.Name == "RequiresDynamicCodeAttribute");

        if (!hasRequiresUnreferencedCode && !hasRequiresDynamicCode)
        {
            var diagnostic = Diagnostic.Create(
                MissingAotAttributesRule,
                invocation.GetLocation(),
                containingMethod.Name,
                apiType);
            context.ReportDiagnostic(diagnostic);
        }
    }
}

