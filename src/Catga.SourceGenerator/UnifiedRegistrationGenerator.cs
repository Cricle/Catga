#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Catga.SourceGenerator.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Catga.SourceGenerator;

/// <summary>
/// Unified generator that combines Handler and Service registration.
/// Generates AddCatgaServices() extension method that auto-registers all handlers and services.
/// Scans for: IRequestHandler, IEventHandler, IPipelineBehavior, IProjection, IEventUpgrader, [CatgaService]
/// </summary>
[Generator]
public sealed class UnifiedRegistrationGenerator : IIncrementalGenerator
{
    // Interface patterns to scan for handlers
    private const string RequestHandler2 = "Catga.Abstractions.IRequestHandler<TRequest, TResponse>";
    private const string RequestHandler1 = "Catga.Abstractions.IRequestHandler<TRequest>";
    private const string EventHandlerGeneric = "Catga.Abstractions.IEventHandler<TEvent>";
    private const string EventHandlerNonGeneric = "Catga.Abstractions.IEventHandler";
    private const string PipelineBehavior = "Catga.Pipeline.IPipelineBehavior<TRequest, TResponse>";
    private const string Projection = "Catga.EventSourcing.IProjection";
    private const string EventUpgrader = "Catga.EventSourcing.IEventUpgrader";
    private const string SingletonLifetime = "Singleton";
    private const string CatgaMediatorTypeName = "Catga.ICatgaMediator";
    private const string FlowExecutorTypeName = "Catga.Flow.Dsl.IFlowExecutor";
    private const string FlowTypeName = "Catga.Flow.IFlow<TState>";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Emit attributes (old generators have been removed)
        context.RegisterPostInitializationOutput(ctx =>
        {
            ctx.AddSource("CatgaUnifiedAttributes.g.cs", SourceText.From(AttributeSource, Encoding.UTF8));
        });

        // Discover all registrations (handlers + services)
        var discoveries = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                static (ctx, _) => ExtractAllRegistrations(ctx))
            .Where(static r => r is not null && (r.Registrations.Count > 0 || r.Diagnostics.Count > 0))
            .Collect();

        context.RegisterSourceOutput(discoveries, static (spc, items) =>
        {
            foreach (var diagnostic in items.Where(static i => i is not null).SelectMany(static i => i!.Diagnostics))
            {
                spc.ReportDiagnostic(diagnostic);
            }

            var list = items.Where(static i => i is not null).SelectMany(static i => i!.Registrations).ToList();
            spc.AddSource("CatgaUnifiedRegistrations.g.cs",
                SourceText.From(GenerateRegistrations(list), Encoding.UTF8));
        });
    }

    /// <summary>
    /// Extracts ALL registrations from a single class (handlers + services).
    /// </summary>
    private static DiscoveryResult? ExtractAllRegistrations(GeneratorSyntaxContext context)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol { IsAbstract: false } symbol)
            return null;

        var registrations = new List<RegistrationInfo>();
        var diagnostics = new List<Diagnostic>();

        // Try to extract handler registrations
        var handlerRegs = ExtractHandlerRegistrations(symbol);
        if (handlerRegs != null)
        {
            registrations.AddRange(handlerRegs);
            var handlerDiagnostic = CreateSingletonScopedDependencyDiagnostic(symbol, GetHandlerLifetime(symbol));
            if (handlerDiagnostic != null)
                diagnostics.Add(handlerDiagnostic);
        }

        // Try to extract service registrations
        var serviceReg = ExtractServiceRegistration(symbol, out var implementationSymbol, out var serviceLifetime);
        if (serviceReg != null)
        {
            registrations.Add(serviceReg);
            var serviceDiagnostic = CreateSingletonScopedDependencyDiagnostic(implementationSymbol ?? symbol, serviceLifetime);
            if (serviceDiagnostic != null)
                diagnostics.Add(serviceDiagnostic);
        }

        return registrations.Count > 0 || diagnostics.Count > 0
            ? new DiscoveryResult(registrations, diagnostics)
            : null;
    }

    /// <summary>
    /// Extract handler registrations (from HandlerRegistrationGenerator logic).
    /// </summary>
    private static List<RegistrationInfo>? ExtractHandlerRegistrations(INamedTypeSymbol symbol)
    {
        // Check for [CatgaIgnore] attribute
        if (symbol.GetAttributes().Any(a => a.AttributeClass?.Name == "CatgaIgnoreAttribute"))
            return null;

        // Get lifetime from [CatgaLifetime] attribute (default: Scoped)
        var lifetime = GetHandlerLifetime(symbol);

        // Get order from [CatgaOrder] attribute (default: 0)
        var order = 0;
        var orderAttr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "CatgaOrderAttribute");
        if (orderAttr?.ConstructorArguments.Length > 0 && orderAttr.ConstructorArguments[0].Value is int o)
        {
            order = o;
        }

        var registrations = new List<RegistrationInfo>();
        var implType = symbol.ToDisplayString();

        // Check ALL interfaces (not just first match)
        foreach (var iface in symbol.AllInterfaces)
        {
            var ifaceName = iface.OriginalDefinition.ToDisplayString();

            // IRequestHandler<TRequest, TResponse>
            if (ifaceName == RequestHandler2 && iface.TypeArguments.Length == 2)
            {
                registrations.Add(new RegistrationInfo(
                    RegistrationKind.RequestHandler,
                    implType,
                    $"Catga.Abstractions.IRequestHandler<{iface.TypeArguments[0].ToDisplayString()}, {iface.TypeArguments[1].ToDisplayString()}>",
                    lifetime, order, false));
                continue;
            }

            // IRequestHandler<TRequest> (no response)
            if (ifaceName == RequestHandler1 && iface.TypeArguments.Length == 1)
            {
                registrations.Add(new RegistrationInfo(
                    RegistrationKind.RequestHandler,
                    implType,
                    $"Catga.Abstractions.IRequestHandler<{iface.TypeArguments[0].ToDisplayString()}>",
                    lifetime, order, false));
                continue;
            }

            // IEventHandler<TEvent>
            if (ifaceName == EventHandlerGeneric && iface.TypeArguments.Length == 1)
            {
                registrations.Add(new RegistrationInfo(
                    RegistrationKind.EventHandler,
                    implType,
                    $"Catga.Abstractions.IEventHandler<{iface.TypeArguments[0].ToDisplayString()}>",
                    lifetime, order, false));
                continue;
            }

            // IEventHandler (non-generic)
            if (ifaceName == EventHandlerNonGeneric)
            {
                registrations.Add(new RegistrationInfo(
                    RegistrationKind.EventHandler,
                    implType,
                    "Catga.Abstractions.IEventHandler",
                    lifetime, order, false));
                continue;
            }

            // IPipelineBehavior<,>
            if (ifaceName == PipelineBehavior && iface.TypeArguments.Length == 2)
            {
                var isOpenGeneric = symbol.TypeParameters.Length > 0;
                var implTypeName = isOpenGeneric
                    ? symbol.ConstructUnboundGenericType().ToDisplayString()
                    : implType;
                registrations.Add(new RegistrationInfo(
                    RegistrationKind.PipelineBehavior,
                    implTypeName,
                    "Catga.Pipeline.IPipelineBehavior<,>",
                    lifetime, order, isOpenGeneric));
                continue;
            }

            // IProjection
            if (ifaceName == Projection)
            {
                registrations.Add(new RegistrationInfo(
                    RegistrationKind.Projection,
                    implType,
                    "Catga.EventSourcing.IProjection",
                    lifetime, order, false));
                continue;
            }

            // IEventUpgrader
            if (ifaceName == EventUpgrader)
            {
                registrations.Add(new RegistrationInfo(
                    RegistrationKind.EventUpgrader,
                    implType,
                    "Catga.EventSourcing.IEventUpgrader",
                    lifetime, order, false));
                continue;
            }
        }

        return registrations.Count > 0 ? registrations : null;
    }

    /// <summary>
    /// Extract service registration (from ServiceRegistrationGenerator logic).
    /// </summary>
    private static RegistrationInfo? ExtractServiceRegistration(
        INamedTypeSymbol symbol,
        out INamedTypeSymbol? implementationSymbol,
        out string lifetime)
    {
        implementationSymbol = null;
        lifetime = "Scoped";

        // Check for [CatgaService] attribute
        var attribute = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "CatgaServiceAttribute");

        if (attribute == null) return null;

        var autoRegister = true;
        string? serviceType = null;
        string? implType = null;

        // Read Lifetime
        var lifetimeArg = attribute.NamedArguments
            .FirstOrDefault(kvp => kvp.Key == "Lifetime");
        if (lifetimeArg.Value.Value is int lifetimeValue)
        {
            lifetime = lifetimeValue switch
            {
                0 => "Singleton",
                1 => "Scoped",
                2 => "Transient",
                _ => "Scoped"
            };
        }

        // Read ServiceType (interface)
        var serviceTypeArg = attribute.NamedArguments
            .FirstOrDefault(kvp => kvp.Key == "ServiceType");
        if (serviceTypeArg.Value.Value is INamedTypeSymbol serviceTypeSymbol)
        {
            serviceType = serviceTypeSymbol.ToDisplayString();
        }

        // Read ImplType (implementation)
        var implTypeArg = attribute.NamedArguments
            .FirstOrDefault(kvp => kvp.Key == "ImplType");
        if (implTypeArg.Value.Value is INamedTypeSymbol implTypeSymbol)
        {
            implType = implTypeSymbol.ToDisplayString();
            implementationSymbol = implTypeSymbol;
        }

        // Read AutoRegister
        var autoRegisterArg = attribute.NamedArguments
            .FirstOrDefault(kvp => kvp.Key == "AutoRegister");
        if (autoRegisterArg.Value.Value is bool autoRegValue)
        {
            autoRegister = autoRegValue;
        }

        if (!autoRegister) return null;

        // Determine implementation type (defaults to marked class)
        var implementationType = implType ?? symbol.ToDisplayString();
        implementationSymbol ??= symbol;

        // Determine service type
        var finalServiceType = serviceType ?? implementationType;

        return new RegistrationInfo(
            RegistrationKind.Service,
            implementationType,
            finalServiceType,
            lifetime,
            0, // Services don't have order
            false
        );
    }

    private static string GetHandlerLifetime(INamedTypeSymbol symbol)
    {
        var lifetime = "Scoped";
        var lifetimeAttr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "CatgaLifetimeAttribute");
        if (lifetimeAttr?.ConstructorArguments.Length > 0 && lifetimeAttr.ConstructorArguments[0].Value is int lt)
        {
            lifetime = lt switch { 0 => SingletonLifetime, 2 => "Transient", _ => "Scoped" };
        }

        return lifetime;
    }

    private static Diagnostic? CreateSingletonScopedDependencyDiagnostic(INamedTypeSymbol symbol, string lifetime)
    {
        if (lifetime != SingletonLifetime)
            return null;

        var constructors = symbol.InstanceConstructors
            .Where(static ctor => ctor.DeclaredAccessibility == Accessibility.Public && !ctor.IsImplicitlyDeclared)
            .ToArray();

        if (constructors.Length == 0)
            return null;

        var safeConstructorExists = constructors.Any(static ctor => ctor.Parameters.All(parameter => !IsScopedCatgaDependency(parameter.Type)));
        if (safeConstructorExists)
            return null;

        foreach (var constructor in constructors)
        {
            var riskyParameter = constructor.Parameters.FirstOrDefault(static parameter => IsScopedCatgaDependency(parameter.Type));
            if (riskyParameter != null)
            {
                return Diagnostic.Create(
                    CatgaAnalyzerRules.SingletonDependsOnScopedCatgaService,
                    symbol.Locations.FirstOrDefault(),
                    symbol.ToDisplayString(),
                    riskyParameter.Type.ToDisplayString());
            }
        }

        return null;
    }

    private static bool IsScopedCatgaDependency(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is not INamedTypeSymbol namedTypeSymbol)
            return false;

        var display = namedTypeSymbol.OriginalDefinition.ToDisplayString();
        return display == CatgaMediatorTypeName ||
               display == FlowExecutorTypeName ||
               display == FlowTypeName;
    }

    private static string GenerateRegistrations(IReadOnlyList<RegistrationInfo> registrations)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""
            // <auto-generated/>
            #nullable enable
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

            namespace Catga.DependencyInjection;

            /// <summary>
            /// Auto-generated unified registrations for handlers and services.
            /// Call AddCatgaServices() to register all discovered handlers and services.
            /// </summary>
            public static class CatgaUnifiedRegistrations
            {
                /// <summary>
                /// Registers all auto-discovered handlers, behaviors, projections, upgraders, and services.
                /// </summary>
                public static IServiceCollection AddCatgaServices(this IServiceCollection services)
                {
            """);

        // Group by kind
        var requestHandlers = registrations.Where(r => r.Kind == RegistrationKind.RequestHandler).ToList();
        var eventHandlers = registrations.Where(r => r.Kind == RegistrationKind.EventHandler).ToList();
        var behaviors = registrations.Where(r => r.Kind == RegistrationKind.PipelineBehavior).OrderBy(r => r.Order).ToList();
        var projections = registrations.Where(r => r.Kind == RegistrationKind.Projection).ToList();
        var upgraders = registrations.Where(r => r.Kind == RegistrationKind.EventUpgrader).ToList();
        var services = registrations.Where(r => r.Kind == RegistrationKind.Service).ToList();

        // Request Handlers
        if (requestHandlers.Count > 0)
        {
            sb.AppendLine("        // Request Handlers");
            foreach (var r in requestHandlers)
            {
                var method = GetAddMethod(r.Lifetime);
                sb.AppendLine($"        services.{method}<{r.ServiceType}, {r.ImplType}>();");
            }
            sb.AppendLine();
        }

        // Event Handlers
        if (eventHandlers.Count > 0)
        {
            sb.AppendLine("        // Event Handlers");
            foreach (var r in eventHandlers)
            {
                var method = GetAddMethod(r.Lifetime);
                sb.AppendLine($"        services.{method}<{r.ServiceType}, {r.ImplType}>();");
            }
            sb.AppendLine();
        }

        // Pipeline Behaviors (order matters)
        if (behaviors.Count > 0)
        {
            sb.AppendLine("        // Pipeline Behaviors (ordered)");
            foreach (var r in behaviors)
            {
                var method = GetAddMethod(r.Lifetime);
                if (r.IsOpenGeneric)
                {
                    sb.AppendLine($"        services.{method}(typeof(Catga.Pipeline.IPipelineBehavior<,>), typeof({r.ImplType}));");
                }
                else
                {
                    sb.AppendLine($"        services.{method}<{r.ServiceType}, {r.ImplType}>();");
                }
            }
            sb.AppendLine();
        }

        // Projections
        if (projections.Count > 0)
        {
            sb.AppendLine("        // Projections");
            foreach (var r in projections)
            {
                var method = GetAddMethod(r.Lifetime);
                sb.AppendLine($"        services.{method}<{r.ImplType}>();");
            }
            sb.AppendLine();
        }

        // Event Upgraders
        if (upgraders.Count > 0)
        {
            sb.AppendLine("        // Event Upgraders");
            foreach (var r in upgraders)
            {
                var method = GetAddMethod(r.Lifetime);
                sb.AppendLine($"        services.{method}<{r.ServiceType}, {r.ImplType}>();");
            }
            sb.AppendLine();
        }

        // Services
        if (services.Count > 0)
        {
            sb.AppendLine("        // Services");
            var grouped = services.GroupBy(s => s.Lifetime);
            foreach (var group in grouped.OrderBy(g => g.Key))
            {
                sb.AppendLine($"        // {group.Key} lifetime services");
                foreach (var r in group)
                {
                    var method = GetAddMethod(r.Lifetime);
                    if (r.ServiceType != r.ImplType)
                    {
                        // Has interface: Register as ServiceType -> ImplType
                        sb.AppendLine($"        services.{method}<{r.ServiceType}, {r.ImplType}>();");
                        // Also register concrete type
                        sb.AppendLine($"        services.{method}<{r.ImplType}>();");
                    }
                    else
                    {
                        // No interface: Only register concrete type
                        sb.AppendLine($"        services.{method}<{r.ImplType}>();");
                    }
                }
            }
            sb.AppendLine();
        }

        sb.AppendLine("        return services.ValidateCatgaLifetimes();");
        sb.AppendLine("    }");
        sb.AppendLine();

        // Add backward compatibility method
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// [Obsolete] Use AddCatgaServices() instead.");
        sb.AppendLine("    /// This method is provided for backward compatibility.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    [System.Obsolete(\"Use AddCatgaServices() instead. This method will be removed in a future version.\")]");
        sb.AppendLine("    public static IServiceCollection AddCatgaHandlers(this IServiceCollection services)");
        sb.AppendLine("        => AddCatgaServices(services);");
        sb.AppendLine();

        // Add backward compatibility method for services
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// [Obsolete] Use AddCatgaServices() instead.");
        sb.AppendLine("    /// This method is provided for backward compatibility.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    [System.Obsolete(\"Use AddCatgaServices() instead. This method will be removed in a future version.\")]");
        sb.AppendLine("    public static IServiceCollection AddGeneratedServices(this IServiceCollection services)");
        sb.AppendLine("        => AddCatgaServices(services);");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string GetAddMethod(string lifetime) => lifetime switch
    {
        "Scoped" => "AddScoped",
        "Transient" => "AddTransient",
        _ => "AddSingleton"
    };

    private const string AttributeSource = """
        // <auto-generated/>
        #nullable enable

        namespace Catga;

        /// <summary>
        /// Excludes a handler from auto-registration.
        /// </summary>
        [System.AttributeUsage(System.AttributeTargets.Class, Inherited = false)]
        public sealed class CatgaIgnoreAttribute : System.Attribute { }

        /// <summary>
        /// Specifies the service lifetime for auto-registration.
        /// </summary>
        [System.AttributeUsage(System.AttributeTargets.Class, Inherited = false)]
        public sealed class CatgaLifetimeAttribute : System.Attribute
        {
            public ServiceLifetime Lifetime { get; }
            public CatgaLifetimeAttribute(ServiceLifetime lifetime) => Lifetime = lifetime;
        }

        /// <summary>
        /// Specifies the registration order for pipeline behaviors.
        /// Lower values are registered first (executed first in pipeline).
        /// </summary>
        [System.AttributeUsage(System.AttributeTargets.Class, Inherited = false)]
        public sealed class CatgaOrderAttribute : System.Attribute
        {
            public int Order { get; }
            public CatgaOrderAttribute(int order) => Order = order;
        }

        /// <summary>
        /// Service registration attribute.
        /// Rules:
        /// 1. If ServiceType specified: Register as ServiceType -> ImplType
        /// 2. If ServiceType not specified: Register only as ImplType (concrete type)
        /// 3. ImplType defaults to the marked class itself
        /// </summary>
        [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
        public sealed class CatgaServiceAttribute : System.Attribute
        {
            /// <summary>
            /// Service lifetime (default: Scoped)
            /// </summary>
            public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Scoped;

            /// <summary>
            /// Service type (interface) to register
            /// REQUIRED if you want interface registration
            /// If not specified, only registers concrete type
            /// </summary>
            public System.Type? ServiceType { get; set; }

            /// <summary>
            /// Implementation type
            /// If not specified, uses the marked class itself
            /// </summary>
            public System.Type? ImplType { get; set; }

            /// <summary>
            /// Whether to auto-register this service (default: true)
            /// </summary>
            public bool AutoRegister { get; set; } = true;

            public CatgaServiceAttribute() { }

            public CatgaServiceAttribute(ServiceLifetime lifetime)
            {
                Lifetime = lifetime;
            }
        }

        /// <summary>
        /// Service lifetime
        /// </summary>
        public enum ServiceLifetime
        {
            Singleton = 0,
            Scoped = 1,
            Transient = 2
        }
        """;

    internal enum RegistrationKind
    {
        RequestHandler,
        EventHandler,
        PipelineBehavior,
        Projection,
        EventUpgrader,
        Service
    }

    internal sealed class RegistrationInfo
    {
        public RegistrationKind Kind { get; }
        public string ImplType { get; }
        public string ServiceType { get; }
        public string Lifetime { get; }
        public int Order { get; }
        public bool IsOpenGeneric { get; }

        public RegistrationInfo(RegistrationKind kind, string implType, string serviceType, string lifetime, int order, bool isOpenGeneric)
        {
            Kind = kind;
            ImplType = implType;
            ServiceType = serviceType;
            Lifetime = lifetime;
            Order = order;
            IsOpenGeneric = isOpenGeneric;
        }
    }

    internal sealed class DiscoveryResult
    {
        public IReadOnlyList<RegistrationInfo> Registrations { get; }
        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        public DiscoveryResult(IReadOnlyList<RegistrationInfo> registrations, IReadOnlyList<Diagnostic> diagnostics)
        {
            Registrations = registrations;
            Diagnostics = diagnostics;
        }
    }
}
