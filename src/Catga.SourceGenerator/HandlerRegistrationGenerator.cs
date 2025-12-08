#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Catga.SourceGenerator;

/// <summary>
/// Generates AddCatgaHandlers extension method that auto-registers all handlers.
/// Scans for: IRequestHandler, IEventHandler, IPipelineBehavior, IProjection, IEventUpgrader
/// </summary>
[Generator]
public sealed class HandlerRegistrationGenerator : IIncrementalGenerator
{
    // Interface patterns to scan
    private const string RequestHandler2 = "Catga.Abstractions.IRequestHandler<TRequest, TResponse>";
    private const string RequestHandler1 = "Catga.Abstractions.IRequestHandler<TRequest>";
    private const string EventHandlerGeneric = "Catga.Abstractions.IEventHandler<TEvent>";
    private const string EventHandlerNonGeneric = "Catga.Abstractions.IEventHandler";
    private const string PipelineBehavior = "Catga.Pipeline.IPipelineBehavior<TRequest, TResponse>";
    private const string Projection = "Catga.EventSourcing.IProjection";
    private const string EventUpgrader = "Catga.EventSourcing.IEventUpgrader";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Emit attributes
        context.RegisterPostInitializationOutput(ctx =>
        {
            ctx.AddSource("CatgaHandlerAttributes.g.cs", SourceText.From(AttributeSource, Encoding.UTF8));
        });

        // Discover all handler implementations (returns list per class)
        var handlers = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                static (ctx, _) => ExtractAllHandlerInfo(ctx))
            .Where(static h => h is not null && h.Count > 0)
            .Collect();

        context.RegisterSourceOutput(handlers, static (spc, items) =>
        {
            var list = items.Where(i => i is not null).SelectMany(i => i!).ToList();
            spc.AddSource("CatgaHandlerRegistrations.g.cs",
                SourceText.From(GenerateRegistrations(list!), Encoding.UTF8));
        });
    }

    /// <summary>
    /// Extracts ALL handler interfaces from a single class (supports multi-interface handlers).
    /// </summary>
    private static List<HandlerRegistration>? ExtractAllHandlerInfo(GeneratorSyntaxContext context)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol { IsAbstract: false } symbol)
            return null;

        // Check for [CatgaIgnore] attribute
        if (symbol.GetAttributes().Any(a => a.AttributeClass?.Name == "CatgaIgnoreAttribute"))
            return null;

        // Get lifetime from [CatgaLifetime] attribute (default: Singleton)
        var lifetime = "Singleton";
        var lifetimeAttr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "CatgaLifetimeAttribute");
        if (lifetimeAttr?.ConstructorArguments.Length > 0 && lifetimeAttr.ConstructorArguments[0].Value is int lt)
        {
            lifetime = lt switch { 1 => "Scoped", 2 => "Transient", _ => "Singleton" };
        }

        // Get order from [CatgaOrder] attribute (default: 0)
        var order = 0;
        var orderAttr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "CatgaOrderAttribute");
        if (orderAttr?.ConstructorArguments.Length > 0 && orderAttr.ConstructorArguments[0].Value is int o)
        {
            order = o;
        }

        var registrations = new List<HandlerRegistration>();
        var implType = symbol.ToDisplayString();

        // Check ALL interfaces (not just first match)
        foreach (var iface in symbol.AllInterfaces)
        {
            var ifaceName = iface.OriginalDefinition.ToDisplayString();

            // IRequestHandler<TRequest, TResponse>
            if (ifaceName == RequestHandler2 && iface.TypeArguments.Length == 2)
            {
                registrations.Add(new HandlerRegistration(
                    HandlerCategory.RequestHandler,
                    implType,
                    $"Catga.Abstractions.IRequestHandler<{iface.TypeArguments[0].ToDisplayString()}, {iface.TypeArguments[1].ToDisplayString()}>",
                    lifetime, order, false));
                continue;
            }

            // IRequestHandler<TRequest> (no response)
            if (ifaceName == RequestHandler1 && iface.TypeArguments.Length == 1)
            {
                registrations.Add(new HandlerRegistration(
                    HandlerCategory.RequestHandler,
                    implType,
                    $"Catga.Abstractions.IRequestHandler<{iface.TypeArguments[0].ToDisplayString()}>",
                    lifetime, order, false));
                continue;
            }

            // IEventHandler<TEvent>
            if (ifaceName == EventHandlerGeneric && iface.TypeArguments.Length == 1)
            {
                registrations.Add(new HandlerRegistration(
                    HandlerCategory.EventHandler,
                    implType,
                    $"Catga.Abstractions.IEventHandler<{iface.TypeArguments[0].ToDisplayString()}>",
                    lifetime, order, false));
                continue;
            }

            // IEventHandler (non-generic)
            if (ifaceName == EventHandlerNonGeneric)
            {
                registrations.Add(new HandlerRegistration(
                    HandlerCategory.EventHandler,
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
                registrations.Add(new HandlerRegistration(
                    HandlerCategory.PipelineBehavior,
                    implTypeName,
                    "Catga.Pipeline.IPipelineBehavior<,>",
                    lifetime, order, isOpenGeneric));
                continue;
            }

            // IProjection
            if (ifaceName == Projection)
            {
                registrations.Add(new HandlerRegistration(
                    HandlerCategory.Projection,
                    implType,
                    "Catga.EventSourcing.IProjection",
                    lifetime, order, false));
                continue;
            }

            // IEventUpgrader
            if (ifaceName == EventUpgrader)
            {
                registrations.Add(new HandlerRegistration(
                    HandlerCategory.EventUpgrader,
                    implType,
                    "Catga.EventSourcing.IEventUpgrader",
                    lifetime, order, false));
                continue;
            }
        }

        return registrations.Count > 0 ? registrations : null;
    }

    private static string GenerateRegistrations(IReadOnlyList<HandlerRegistration> handlers)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""
            // <auto-generated/>
            #nullable enable
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

            namespace Catga.DependencyInjection;

            /// <summary>
            /// Auto-generated handler registrations.
            /// Call AddCatgaHandlers() to register all discovered handlers.
            /// </summary>
            public static class CatgaHandlerRegistrations
            {
                /// <summary>
                /// Registers all auto-discovered handlers, behaviors, projections, and upgraders.
                /// </summary>
                public static IServiceCollection AddCatgaHandlers(this IServiceCollection services)
                {
            """);

        // Group by category
        var requestHandlers = handlers.Where(h => h.Category == HandlerCategory.RequestHandler).ToList();
        var eventHandlers = handlers.Where(h => h.Category == HandlerCategory.EventHandler).ToList();
        var behaviors = handlers.Where(h => h.Category == HandlerCategory.PipelineBehavior).OrderBy(h => h.Order).ToList();
        var projections = handlers.Where(h => h.Category == HandlerCategory.Projection).ToList();
        var upgraders = handlers.Where(h => h.Category == HandlerCategory.EventUpgrader).ToList();

        // Request Handlers
        if (requestHandlers.Count > 0)
        {
            sb.AppendLine("        // Request Handlers");
            foreach (var h in requestHandlers)
            {
                var method = GetAddMethod(h.Lifetime);
                sb.AppendLine($"        services.{method}<{h.ServiceType}, {h.ImplType}>();");
            }
            sb.AppendLine();
        }

        // Event Handlers (use Add, not TryAdd - multiple handlers per event allowed)
        if (eventHandlers.Count > 0)
        {
            sb.AppendLine("        // Event Handlers");
            foreach (var h in eventHandlers)
            {
                var method = GetAddMethod(h.Lifetime);
                sb.AppendLine($"        services.{method}<{h.ServiceType}, {h.ImplType}>();");
            }
            sb.AppendLine();
        }

        // Pipeline Behaviors (order matters)
        if (behaviors.Count > 0)
        {
            sb.AppendLine("        // Pipeline Behaviors (ordered)");
            foreach (var h in behaviors)
            {
                var method = GetAddMethod(h.Lifetime);
                if (h.IsOpenGeneric)
                {
                    // Open generic registration
                    sb.AppendLine($"        services.{method}(typeof(Catga.Pipeline.IPipelineBehavior<,>), typeof({h.ImplType}));");
                }
                else
                {
                    sb.AppendLine($"        services.{method}<{h.ServiceType}, {h.ImplType}>();");
                }
            }
            sb.AppendLine();
        }

        // Projections
        if (projections.Count > 0)
        {
            sb.AppendLine("        // Projections");
            foreach (var h in projections)
            {
                var method = GetAddMethod(h.Lifetime);
                sb.AppendLine($"        services.{method}<{h.ImplType}>();");
            }
            sb.AppendLine();
        }

        // Event Upgraders
        if (upgraders.Count > 0)
        {
            sb.AppendLine("        // Event Upgraders");
            foreach (var h in upgraders)
            {
                var method = GetAddMethod(h.Lifetime);
                sb.AppendLine($"        services.{method}<{h.ServiceType}, {h.ImplType}>();");
            }
            sb.AppendLine();
        }

        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
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
        """;

    private enum HandlerCategory { RequestHandler, EventHandler, PipelineBehavior, Projection, EventUpgrader }

    private sealed class HandlerRegistration
    {
        public HandlerCategory Category { get; }
        public string ImplType { get; }
        public string ServiceType { get; }
        public string Lifetime { get; }
        public int Order { get; }
        public bool IsOpenGeneric { get; }

        public HandlerRegistration(HandlerCategory category, string implType, string serviceType, string lifetime, int order, bool isOpenGeneric)
        {
            Category = category;
            ImplType = implType;
            ServiceType = serviceType;
            Lifetime = lifetime;
            Order = order;
            IsOpenGeneric = isOpenGeneric;
        }
    }
}
