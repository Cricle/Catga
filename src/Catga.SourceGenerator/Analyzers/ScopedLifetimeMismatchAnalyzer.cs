using System;
using System.Linq;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Catga.SourceGenerator.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ScopedLifetimeMismatchAnalyzer : DiagnosticAnalyzer
{
    private const string AddSingletonMethod = "AddSingleton";
    private const string TryAddSingletonMethod = "TryAddSingleton";
    private const string CatgaLifetimeAttributeName = "CatgaLifetimeAttribute";
    private const string ServiceLifetimeTypeName = "Microsoft.Extensions.DependencyInjection.ServiceLifetime";
    private const string CatgaMediatorTypeName = "Catga.ICatgaMediator";
    private const string FlowExecutorTypeName = "Catga.Flow.Dsl.IFlowExecutor";
    private const string FlowTypeName = "Catga.Flow.IFlow<TState>";
    private const string RequestHandlerTypeName = "Catga.Abstractions.IRequestHandler<TRequest, TResponse>";
    private const string RequestHandlerWithoutResponseTypeName = "Catga.Abstractions.IRequestHandler<TRequest>";
    private const string EventHandlerTypeName = "Catga.Abstractions.IEventHandler<TEvent>";
    private const string PipelineBehaviorWithResponseTypeName = "Catga.Pipeline.IPipelineBehavior<TRequest, TResponse>";
    private const string PipelineBehaviorWithoutResponseTypeName = "Catga.Pipeline.IPipelineBehavior<TRequest>";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(CatgaAnalyzerRules.SingletonDependsOnScopedCatgaService);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            syntaxContext => AnalyzeInvocation(syntaxContext),
            Microsoft.CodeAnalysis.CSharp.SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var method = context.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (method is null || !IsSingletonRegistrationMethod(method))
            return;

        if (!TryResolveRegisteredTypes(invocation, method, context.SemanticModel, out var serviceType, out var implementationType))
            return;

        if (implementationType is null || !IsCatgaRegistrationRoot(serviceType, implementationType))
            return;

        ReportIfScopedDependencyFound(implementationType, invocation.GetLocation(), context.ReportDiagnostic);
    }

    private static bool IsSingletonRegistrationMethod(IMethodSymbol method)
        => method.Name is AddSingletonMethod or TryAddSingletonMethod;

    private static bool TryResolveRegisteredTypes(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SemanticModel semanticModel,
        out INamedTypeSymbol? serviceType,
        out INamedTypeSymbol? implementationType)
    {
        serviceType = null;
        implementationType = null;

        if (method.TypeArguments.Length == 2)
        {
            serviceType = method.TypeArguments[0] as INamedTypeSymbol;
            implementationType = method.TypeArguments[1] as INamedTypeSymbol;
            return implementationType is not null;
        }

        if (method.TypeArguments.Length == 1 && invocation.ArgumentList.Arguments.Count == 0)
        {
            serviceType = method.TypeArguments[0] as INamedTypeSymbol;
            implementationType = serviceType;
            return implementationType is not null;
        }

        if (invocation.ArgumentList.Arguments.Count >= 2 &&
            TryGetTypeOf(invocation.ArgumentList.Arguments[0].Expression, semanticModel, out serviceType) &&
            TryGetTypeOf(invocation.ArgumentList.Arguments[1].Expression, semanticModel, out implementationType))
        {
            return implementationType is not null;
        }

        return false;
    }

    private static bool TryGetTypeOf(ExpressionSyntax expression, SemanticModel semanticModel, out INamedTypeSymbol? typeSymbol)
    {
        typeSymbol = null;
        if (expression is not TypeOfExpressionSyntax typeOfExpression)
            return false;

        typeSymbol = semanticModel.GetTypeInfo(typeOfExpression.Type).Type as INamedTypeSymbol;
        return typeSymbol is not null;
    }

    private static bool IsCatgaRegistrationRoot(INamedTypeSymbol? serviceType, INamedTypeSymbol implementationType)
        => (serviceType is not null && IsCatgaManagedType(serviceType)) || IsCatgaManagedType(implementationType);

    private static bool IsCatgaManagedType(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.ToDisplayString() == FlowExecutorTypeName)
            return true;

        return typeSymbol.AllInterfaces.Any(IsCatgaManagedInterface) || IsCatgaManagedInterface(typeSymbol);
    }

    private static bool IsCatgaManagedInterface(INamedTypeSymbol typeSymbol)
    {
        var display = typeSymbol.OriginalDefinition.ToDisplayString();
        return display == RequestHandlerTypeName ||
               display == RequestHandlerWithoutResponseTypeName ||
               display == EventHandlerTypeName ||
               display == PipelineBehaviorWithResponseTypeName ||
               display == PipelineBehaviorWithoutResponseTypeName ||
               display == FlowExecutorTypeName;
    }

    private static void ReportIfScopedDependencyFound(
        INamedTypeSymbol implementationType,
        Location? location,
        Action<Diagnostic> reportDiagnostic)
    {
        if (location is null)
            return;

        var constructors = implementationType.InstanceConstructors
            .Where(static ctor => ctor.DeclaredAccessibility == Accessibility.Public && !ctor.IsImplicitlyDeclared)
            .ToArray();

        if (constructors.Length == 0)
            return;

        var riskyConstructors = constructors
            .Select(static ctor => new
            {
                Constructor = ctor,
                RiskyParameter = ctor.Parameters.FirstOrDefault(IsScopedCatgaDependency)
            })
            .Where(static item => item.RiskyParameter is not null)
            .ToArray();

        if (riskyConstructors.Length == 0)
            return;

        var safeConstructorExists = constructors.Any(static ctor => ctor.Parameters.All(parameter => !IsScopedCatgaDependency(parameter)));
        if (safeConstructorExists)
            return;

        var riskyParameter = riskyConstructors[0].RiskyParameter!;
        reportDiagnostic(Diagnostic.Create(
            CatgaAnalyzerRules.SingletonDependsOnScopedCatgaService,
            location,
            implementationType.ToDisplayString(),
            riskyParameter.Type.ToDisplayString()));
    }

    private static bool IsScopedCatgaDependency(IParameterSymbol parameter)
    {
        var type = parameter.Type as INamedTypeSymbol;
        if (type is null)
            return false;

        var display = type.OriginalDefinition.ToDisplayString();
        return display == CatgaMediatorTypeName ||
               display == FlowExecutorTypeName ||
               display == FlowTypeName;
    }
}
