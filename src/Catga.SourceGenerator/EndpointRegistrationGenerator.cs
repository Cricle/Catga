#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Catga.SourceGenerator;

/// <summary>
/// Generates RegisterEndpoints method for classes with [CatgaEndpoint] marked methods.
/// Zero reflection, source-generated, AOT-compatible.
/// </summary>
[Generator]
public sealed class EndpointRegistrationGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var endpoints = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is MethodDeclarationSyntax,
                static (ctx, _) => ExtractEndpoint(ctx))
            .Where(static e => e is not null)
            .Collect();

        context.RegisterSourceOutput(endpoints, (spc, items) =>
        {
            var grouped = items
                .Where(e => e is not null)
                .GroupBy(e => e!.Value.className)
                .ToList();

            foreach (var group in grouped)
            {
                var endpoints = group.Select(e => (e!.Value.methodName, e.Value.httpMethod, e.Value.route, e.Value.name, e.Value.description, e.Value.requestType, e.Value.requestParamName)).ToList();
                var code = GeneratePartial(group.Key, endpoints);
                spc.AddSource($"{group.Key}.Endpoints.g.cs", code);
            }
        });
    }

    private static (string className, string methodName, string httpMethod, string route, string? name, string? description, string requestType, string requestParamName)? ExtractEndpoint(GeneratorSyntaxContext context)
    {
        var methodDecl = context.Node as MethodDeclarationSyntax;
        if (methodDecl == null) return null;

        var attr = methodDecl.AttributeLists
            .SelectMany(al => al.Attributes)
            .FirstOrDefault(a => a.Name.ToString().Contains("CatgaEndpoint"));

        if (attr == null) return null;

        var symbol = context.SemanticModel.GetDeclaredSymbol(methodDecl) as IMethodSymbol;
        if (symbol == null) return null;

        var classSymbol = symbol.ContainingType;

        // Extract attribute arguments
        var httpMethod = attr.ArgumentList?.Arguments[0].Expression.ToString() ?? "Post";
        var route = attr.ArgumentList?.Arguments[1].Expression.ToString()?.Trim('"') ?? "";
        var name = attr.ArgumentList?.Arguments.FirstOrDefault(a => a.NameEquals?.Name.Identifier.Text == "Name")
            ?.Expression.ToString()?.Trim('"') ?? methodDecl.Identifier.Text;
        var description = attr.ArgumentList?.Arguments.FirstOrDefault(a => a.NameEquals?.Name.Identifier.Text == "Description")
            ?.Expression.ToString()?.Trim('"') ?? "";

        // Extract request parameter (first parameter that is not ICatgaMediator or IEventStore)
        var requestParam = symbol.Parameters
            .FirstOrDefault(p => p.Type.Name != "ICatgaMediator" && p.Type.Name != "IEventStore");

        var requestType = requestParam?.Type.Name ?? "object";
        var requestParamName = requestParam?.Name ?? "request";

        return (classSymbol.Name, methodDecl.Identifier.Text, httpMethod, route, name, description, requestType, requestParamName);
    }

    private static string GeneratePartial(string className, List<(string methodName, string httpMethod, string route, string? name, string? description, string requestType, string requestParamName)> endpoints)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"public partial class {className}");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Source-generated method that registers all [CatgaEndpoint] marked methods as HTTP endpoints.");
        sb.AppendLine("    /// Zero reflection, AOT-compatible, hot-path friendly.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static void RegisterEndpoints(Microsoft.AspNetCore.Builder.WebApplication app)");
        sb.AppendLine("    {");

        foreach (var ep in endpoints)
        {
            var mapMethod = ep.httpMethod.ToLower() switch
            {
                "post" => "MapPost",
                "get" => "MapGet",
                "put" => "MapPut",
                "delete" => "MapDelete",
                "patch" => "MapPatch",
                _ => "MapPost"
            };

            sb.AppendLine($"        app.{mapMethod}(\"{ep.route}\", {ep.methodName})");
            sb.AppendLine($"            .WithName(\"{ep.name}\")");
            if (!string.IsNullOrEmpty(ep.description))
                sb.AppendLine($"            .WithDescription(\"{ep.description}\")");
            sb.AppendLine($"            .Produces(Microsoft.AspNetCore.Http.StatusCodes.Status200OK);");
            sb.AppendLine();
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
