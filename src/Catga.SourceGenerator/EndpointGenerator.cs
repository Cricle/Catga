using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Catga.SourceGenerator;

/// <summary>
/// Generates MapCatgaEndpoints() extension method that auto-maps all handlers to API endpoints.
/// Scans specified assemblies for [CatgaHandler] classes implementing IRequestHandler.
/// </summary>
[Generator]
public class EndpointGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Register the attribute
        context.RegisterPostInitializationOutput(ctx =>
        {
            ctx.AddSource("CatgaEndpointsAttribute.g.cs", SourceText.From(AttributeSource, Encoding.UTF8));
        });

        // Find all handlers with [CatgaHandler]
        var handlerProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Catga.CatgaHandlerAttribute",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => GetHandlerEndpointInfo(ctx))
            .Where(static h => h is not null)
            .Collect();

        // Generate endpoints for all handlers
        context.RegisterSourceOutput(handlerProvider, static (spc, handlers) =>
        {
            var validHandlers = handlers.Where(h => h is not null).ToList()!;
            if (validHandlers.Count == 0) return;

            var config = new EndpointConfig { RoutePrefix = "/api" };
            var source = GenerateEndpointExtensions(config, validHandlers);
            spc.AddSource("CatgaEndpointExtensions.g.cs", SourceText.From(source, Encoding.UTF8));
        });
    }

    private static EndpointConfig? GetEndpointConfig(GeneratorAttributeSyntaxContext ctx)
    {
        var attr = ctx.Attributes.FirstOrDefault();
        if (attr is null) return null;

        string? routePrefix = null;
        var assemblies = new List<string>();

        foreach (var arg in attr.NamedArguments)
        {
            if (arg.Key == "RoutePrefix")
                routePrefix = arg.Value.Value as string;
            else if (arg.Key == "ScanAssemblies" && arg.Value.Values.Length > 0)
                assemblies.AddRange(arg.Value.Values.Select(v => v.Value?.ToString() ?? ""));
        }

        return new EndpointConfig
        {
            RoutePrefix = routePrefix ?? "/api",
            ScanAssemblies = assemblies
        };
    }

    private static HandlerEndpointInfo? GetHandlerEndpointInfo(GeneratorAttributeSyntaxContext ctx)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.TargetNode;
        var classSymbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
        if (classSymbol is null) return null;

        // Find IRequestHandler interface
        var handlerInterface = classSymbol.AllInterfaces
            .FirstOrDefault(i =>
                i.OriginalDefinition.ToDisplayString().StartsWith("Catga.Handlers.IRequestHandler") ||
                i.OriginalDefinition.ToDisplayString().StartsWith("Catga.Abstractions.IRequestHandler"));

        if (handlerInterface is null) return null;

        var requestType = handlerInterface.TypeArguments[0];
        var responseType = handlerInterface.TypeArguments.Length > 1
            ? handlerInterface.TypeArguments[1]
            : null;

        // Check for [Route] attribute or infer from handler name
        string? route = null;
        string httpMethod = "POST";

        var routeAttr = classSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "RouteAttribute");
        if (routeAttr != null)
        {
            route = routeAttr.ConstructorArguments.FirstOrDefault().Value?.ToString();
            var methodArg = routeAttr.NamedArguments.FirstOrDefault(a => a.Key == "Method");
            if (methodArg.Value.Value is string m)
                httpMethod = m.ToUpperInvariant();
        }

        // Infer route from handler name if not specified
        if (string.IsNullOrEmpty(route))
        {
            route = InferRouteFromHandlerName(classSymbol.Name, requestType.Name);
            httpMethod = InferHttpMethod(requestType.Name);
        }

        return new HandlerEndpointInfo
        {
            Namespace = classSymbol.ContainingNamespace.ToDisplayString(),
            ClassName = classSymbol.Name,
            FullName = classSymbol.ToDisplayString(),
            RequestType = requestType.ToDisplayString(),
            ResponseType = responseType?.ToDisplayString(),
            HasResponse = responseType != null,
            Route = route!,
            HttpMethod = httpMethod,
            AssemblyName = classSymbol.ContainingAssembly.Name
        };
    }

    private static string InferRouteFromHandlerName(string handlerName, string requestName)
    {
        // CreateOrderHandler -> /orders
        // GetOrderByIdQuery -> /orders/{id}
        // UpdateUserCommand -> /users

        var name = handlerName
            .Replace("Handler", "")
            .Replace("QueryHandler", "")
            .Replace("CommandHandler", "");

        // Remove action prefixes
        var actions = new[] { "Create", "Get", "Update", "Delete", "List", "Find", "Search" };
        foreach (var action in actions)
        {
            if (name.StartsWith(action))
            {
                name = name.Substring(action.Length);
                break;
            }
        }

        // Convert to route: OrderFlow -> orders, UserProfile -> user-profiles
        var route = ToKebabCase(name);

        // Pluralize simple cases
        if (!route.EndsWith("s") && !route.EndsWith("y"))
            route += "s";

        return "/" + route;
    }

    private static string InferHttpMethod(string requestName)
    {
        if (requestName.Contains("Query") || requestName.StartsWith("Get") || requestName.StartsWith("List") || requestName.StartsWith("Find"))
            return "GET";
        if (requestName.StartsWith("Delete") || requestName.Contains("Delete"))
            return "DELETE";
        if (requestName.StartsWith("Update") || requestName.Contains("Update"))
            return "PUT";
        return "POST";
    }

    private static string ToKebabCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return Regex.Replace(name, "(?<!^)([A-Z])", "-$1").ToLowerInvariant();
    }

    private static string GenerateEndpointExtensions(EndpointConfig config, List<HandlerEndpointInfo> handlers)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using Microsoft.AspNetCore.Builder;");
        sb.AppendLine("using Microsoft.AspNetCore.Http;");
        sb.AppendLine("using Microsoft.AspNetCore.Routing;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using Catga;");
        sb.AppendLine("using Catga.Abstractions;");
        sb.AppendLine("using Catga.Core;");
        sb.AppendLine();
        sb.AppendLine("namespace Catga.Generated;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Auto-generated endpoint mapping extensions.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public static class CatgaEndpointExtensions");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Maps all Catga handlers to API endpoints.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static IEndpointRouteBuilder MapCatgaEndpoints(this IEndpointRouteBuilder app)");
        sb.AppendLine("    {");

        foreach (var handler in handlers)
        {
            var route = config.RoutePrefix.TrimEnd('/') + handler.Route;
            var tags = ExtractTags(handler.ClassName);

            if (handler.HasResponse)
            {
                if (handler.HttpMethod == "GET")
                {
                    sb.AppendLine($"        app.MapCatgaQuery<{handler.RequestType}, {handler.ResponseType}>(\"{route}\")");
                }
                else
                {
                    sb.AppendLine($"        app.MapCatgaRequest<{handler.RequestType}, {handler.ResponseType}>(\"{route}\")");
                }
            }
            else
            {
                sb.AppendLine($"        app.MapCatgaRequest<{handler.RequestType}>(\"{route}\")");
            }

            sb.AppendLine($"            .WithName(\"{handler.ClassName.Replace("Handler", "")}\")");
            sb.AppendLine($"            .WithTags(\"{tags}\");");
            sb.AppendLine();
        }

        sb.AppendLine("        return app;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string ExtractTags(string handlerName)
    {
        // CreateOrderFlowHandler -> Orders
        var name = handlerName
            .Replace("Handler", "")
            .Replace("Flow", "")
            .Replace("Query", "")
            .Replace("Command", "");

        var actions = new[] { "Create", "Get", "Update", "Delete", "List", "Find", "Search", "Process" };
        foreach (var action in actions)
        {
            if (name.StartsWith(action))
            {
                name = name.Substring(action.Length);
                break;
            }
        }

        return name + "s";
    }

    private const string AttributeSource = """
        // <auto-generated/>
        #nullable enable

        namespace Catga;

        /// <summary>
        /// Enables automatic endpoint generation for all handlers in the assembly.
        /// Place on assembly: [assembly: CatgaEndpoints]
        /// </summary>
        [System.AttributeUsage(System.AttributeTargets.Assembly)]
        public sealed class CatgaEndpointsAttribute : System.Attribute
        {
            /// <summary>
            /// Route prefix for all endpoints. Default: "/api"
            /// </summary>
            public string RoutePrefix { get; set; } = "/api";

            /// <summary>
            /// Additional assemblies to scan for handlers.
            /// By default, only the current assembly is scanned.
            /// </summary>
            public string[]? ScanAssemblies { get; set; }
        }

        /// <summary>
        /// Specifies custom route for a handler.
        /// </summary>
        [System.AttributeUsage(System.AttributeTargets.Class)]
        public sealed class RouteAttribute : System.Attribute
        {
            public string Path { get; }
            public string Method { get; set; } = "POST";

            public RouteAttribute(string path) => Path = path;
        }
        """;

    private class EndpointConfig
    {
        public string RoutePrefix { get; set; } = "/api";
        public List<string> ScanAssemblies { get; set; } = new();
    }

    private class HandlerEndpointInfo
    {
        public string Namespace { get; set; } = "";
        public string ClassName { get; set; } = "";
        public string FullName { get; set; } = "";
        public string RequestType { get; set; } = "";
        public string? ResponseType { get; set; }
        public bool HasResponse { get; set; }
        public string Route { get; set; } = "";
        public string HttpMethod { get; set; } = "POST";
        public string AssemblyName { get; set; } = "";
    }
}
