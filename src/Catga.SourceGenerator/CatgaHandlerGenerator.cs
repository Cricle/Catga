using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Catga.Abstractions;

namespace Catga.SourceGenerator;

/// <summary>
/// Source generator for automatic handler registration in Catga framework
/// Generates extension methods to register all handlers marked with [CatgaHandler]
/// </summary>
[Generator]
public class CatgaHandlerGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Register attribute for IDE support
        context.RegisterPostInitializationOutput(ctx =>
        {
            ctx.AddSource("CatgaHandlerAttribute.g.cs", SourceText.From(AttributeSource, Encoding.UTF8));
        });

        // Find all classes that implement IRequestHandler or IEventHandler
        var handlerProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsCandidateNode(node),
                transform: static (ctx, _) => GetHandlerInfo(ctx))
            .Where(static m => m is not null)
            .Collect();

        // Generate registration code
        context.RegisterSourceOutput(handlerProvider, static (spc, handlers) =>
        {
            if (handlers.Length == 0)
            {
                // Generate empty registration even if no handlers found
                var emptySource = GenerateEmptyRegistrationCode();
                spc.AddSource("CatgaHandlerRegistration.g.cs", SourceText.From(emptySource, Encoding.UTF8));
                return;
            }

            var source = GenerateRegistrationCode(handlers!);
            spc.AddSource("CatgaHandlerRegistration.g.cs", SourceText.From(source, Encoding.UTF8));
        });
    }

    private static bool IsCandidateNode(SyntaxNode node)
    {
        // Look for class declarations
        if (node is not ClassDeclarationSyntax classDecl)
            return false;

        // Must have a base list (implements interfaces)
        if (classDecl.BaseList == null)
            return false;

        // Check if any base type contains "Handler"
        foreach (var baseType in classDecl.BaseList.Types)
        {
            var typeName = baseType.Type.ToString();
            if (typeName.Contains("IRequestHandler") || typeName.Contains("IEventHandler"))
                return true;
        }

        return false;
    }

    private static HandlerInfo? GetHandlerInfo(GeneratorSyntaxContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration);

        if (symbol is not INamedTypeSymbol classSymbol)
            return null;

        // Get attribute settings (if exists)
        var lifetime = "Scoped";
        var autoRegister = true;

        var attribute = classSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "CatgaHandlerAttribute");

        if (attribute != null)
        {
            // Read Lifetime property
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

            // Read AutoRegister property
            var autoRegisterArg = attribute.NamedArguments
                .FirstOrDefault(kvp => kvp.Key == "AutoRegister");
            if (autoRegisterArg.Value.Value is bool autoRegValue)
            {
                autoRegister = autoRegValue;
            }

            // Check constructor argument for lifetime
            if (attribute.ConstructorArguments.Length > 0 &&
                attribute.ConstructorArguments[0].Value is int ctorLifetime)
            {
                lifetime = ctorLifetime switch
                {
                    0 => "Singleton",
                    1 => "Scoped",
                    2 => "Transient",
                    _ => "Scoped"
                };
            }
        }

        // Check if class implements IRequestHandler<,> or IEventHandler<>
        foreach (var @interface in classSymbol.AllInterfaces)
        {
            var interfaceName = @interface.OriginalDefinition.ToDisplayString();

            if (interfaceName == "Catga.Handlers.IRequestHandler<TRequest, TResponse>")
            {
                var requestType = @interface.TypeArguments[0];
                var responseType = @interface.TypeArguments[1];

                return new HandlerInfo
                {
                    HandlerType = classSymbol.ToDisplayString(),
                    HandlerName = classSymbol.Name,
                    InterfaceType = $"Catga.Handlers.IRequestHandler<{requestType.ToDisplayString()}, {responseType.ToDisplayString()}>",
                    MessageType = requestType.ToDisplayString(),
                    IsRequestHandler = true,
                    Namespace = classSymbol.ContainingNamespace.ToDisplayString(),
                    Lifetime = lifetime,
                    AutoRegister = autoRegister
                };
            }

            if (interfaceName == "Catga.Handlers.IRequestHandler<TRequest>")
            {
                var requestType = @interface.TypeArguments[0];

                return new HandlerInfo
                {
                    HandlerType = classSymbol.ToDisplayString(),
                    HandlerName = classSymbol.Name,
                    InterfaceType = $"Catga.Handlers.IRequestHandler<{requestType.ToDisplayString()}>",
                    MessageType = requestType.ToDisplayString(),
                    IsRequestHandler = true,
                    Namespace = classSymbol.ContainingNamespace.ToDisplayString(),
                    Lifetime = lifetime,
                    AutoRegister = autoRegister
                };
            }

            if (interfaceName == "Catga.Handlers.IEventHandler<TEvent>")
            {
                var eventType = @interface.TypeArguments[0];

                return new HandlerInfo
                {
                    HandlerType = classSymbol.ToDisplayString(),
                    HandlerName = classSymbol.Name,
                    InterfaceType = $"Catga.Handlers.IEventHandler<{eventType.ToDisplayString()}>",
                    MessageType = eventType.ToDisplayString(),
                    IsRequestHandler = false,
                    Namespace = classSymbol.ContainingNamespace.ToDisplayString(),
                    Lifetime = lifetime,
                    AutoRegister = autoRegister
                };
            }
        }

        return null;
    }

    private static string GenerateEmptyRegistrationCode()
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine("namespace Catga.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Auto-generated extension methods for registering Catga handlers");
        sb.AppendLine("/// Generated by Catga.SourceGenerator");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public static partial class CatgaGeneratedHandlerRegistrations");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Registers all auto-discovered handlers from the current assembly");
        sb.AppendLine("    /// No handlers found in this project");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static IServiceCollection AddGeneratedHandlers(this IServiceCollection services)");
        sb.AppendLine("    {");
        sb.AppendLine("        // No handlers to register");
        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string GenerateRegistrationCode(IEnumerable<HandlerInfo> handlers)
    {
        // Filter out handlers with AutoRegister = false
        var handlersToRegister = handlers.Where(h => h.AutoRegister).ToList();

        if (handlersToRegister.Count == 0)
        {
            return GenerateEmptyRegistrationCode();
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine("namespace Catga.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Auto-generated handler registration - zero reflection, AOT-friendly");
        sb.AppendLine($"/// Found {handlersToRegister.Count} handler(s)");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public static partial class CatgaGeneratedHandlerRegistrations");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Registers all auto-discovered handlers");
        sb.AppendLine("    /// Supports: Singleton, Scoped, Transient lifetimes");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static IServiceCollection AddGeneratedHandlers(this IServiceCollection services)");
        sb.AppendLine("    {");

        // Group by lifetime for cleaner output
        var grouped = handlersToRegister.GroupBy(h => h.Lifetime);

        foreach (var group in grouped.OrderBy(g => g.Key))
        {
            if (group.Any())
            {
                sb.AppendLine($"        // {group.Key} lifetime handlers");
                foreach (var handler in group)
                {
                    var methodName = group.Key switch
                    {
                        "Singleton" => "AddSingleton",
                        "Transient" => "AddTransient",
                        _ => "AddScoped"
                    };

                    sb.AppendLine($"        services.{methodName}<{handler.InterfaceType}, {handler.HandlerType}>();");
                }
                sb.AppendLine();
            }
        }

        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private const string AttributeSource = @"// <auto-generated/>
#nullable enable

namespace Catga;

/// <summary>
/// Controls handler registration behavior
/// This attribute is optional - all handlers are auto-registered with Scoped lifetime by default
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CatgaHandlerAttribute : System.Attribute
{
    /// <summary>
    /// Service lifetime (default: Scoped)
    /// </summary>
    public HandlerLifetime Lifetime { get; set; } = HandlerLifetime.Scoped;

    /// <summary>
    /// Whether to auto-register this handler (default: true)
    /// </summary>
    public bool AutoRegister { get; set; } = true;

    public CatgaHandlerAttribute() { }

    public CatgaHandlerAttribute(HandlerLifetime lifetime)
    {
        Lifetime = lifetime;
    }
}

/// <summary>
/// Handler service lifetime
/// </summary>
public enum HandlerLifetime
{
    Singleton = 0,
    Scoped = 1,
    Transient = 2
}
";

    private class HandlerInfo
    {
        public string HandlerType { get; set; } = string.Empty;
        public string HandlerName { get; set; } = string.Empty;
        public string InterfaceType { get; set; } = string.Empty;
        public string MessageType { get; set; } = string.Empty;
        public bool IsRequestHandler { get; set; }
        public string Namespace { get; set; } = string.Empty;
        public string Lifetime { get; set; } = "Scoped";
        public bool AutoRegister { get; set; } = true;
    }
}
