using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Catga.SourceGenerator;

/// <summary>
/// Source generator that automatically implements ICompensatable for commands
/// marked with [Compensation(typeof(...))] attribute.
/// Generates property mapping from result to compensation command.
/// </summary>
[Generator]
public class CompensationGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Find all types with [Compensation] attribute
        var compensatableProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Catga.Flow.CompensationAttribute",
                predicate: static (node, _) => node is RecordDeclarationSyntax or ClassDeclarationSyntax,
                transform: static (ctx, _) => GetCompensationInfo(ctx))
            .Where(static m => m is not null)
            .Collect();

        // Generate implementation code
        context.RegisterSourceOutput(compensatableProvider, static (spc, infos) =>
        {
            if (infos.Length == 0)
                return;

            foreach (var info in infos)
            {
                if (info is null) continue;

                var source = GenerateCompensatableImplementation(info);
                spc.AddSource($"{info.TypeName}.Compensatable.g.cs", SourceText.From(source, Encoding.UTF8));
            }
        });
    }

    private static CompensationInfo? GetCompensationInfo(GeneratorAttributeSyntaxContext context)
    {
        var symbol = context.TargetSymbol as INamedTypeSymbol;
        if (symbol is null)
            return null;

        // Get the [Compensation] attribute
        var attribute = context.Attributes.FirstOrDefault(a =>
            a.AttributeClass?.Name == "CompensationAttribute");

        if (attribute is null)
            return null;

        // Get compensation type from attribute constructor
        if (attribute.ConstructorArguments.Length == 0)
            return null;

        var compensationType = attribute.ConstructorArguments[0].Value as INamedTypeSymbol;
        if (compensationType is null)
            return null;

        // Get property mappings if specified
        var propertyMappings = new List<string>();
        var mappingsArg = attribute.NamedArguments
            .FirstOrDefault(kvp => kvp.Key == "PropertyMappings");
        if (!mappingsArg.Value.IsNull && mappingsArg.Value.Values.Length > 0)
        {
            foreach (var val in mappingsArg.Value.Values)
            {
                if (val.Value is string mapping)
                    propertyMappings.Add(mapping);
            }
        }

        // Find the result type (from IRequest<TResult>)
        INamedTypeSymbol? resultType = null;
        foreach (var iface in symbol.AllInterfaces)
        {
            if (iface.OriginalDefinition.ToDisplayString() == "Catga.Abstractions.IRequest<TResult>")
            {
                resultType = iface.TypeArguments[0] as INamedTypeSymbol;
                break;
            }
        }

        if (resultType is null)
            return null;

        // Get properties of result type and compensation type for auto-mapping
        var resultProperties = resultType.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public)
            .ToList();

        var compensationProperties = compensationType.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public && p.SetMethod != null)
            .ToList();

        // Find constructor parameters for compensation type
        var compensationConstructor = compensationType.Constructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public)
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault();

        return new CompensationInfo
        {
            Namespace = symbol.ContainingNamespace.ToDisplayString(),
            TypeName = symbol.Name,
            FullTypeName = symbol.ToDisplayString(),
            IsRecord = symbol.IsRecord,
            CompensationTypeName = compensationType.Name,
            CompensationFullTypeName = compensationType.ToDisplayString(),
            ResultTypeName = resultType.Name,
            ResultFullTypeName = resultType.ToDisplayString(),
            ResultProperties = resultProperties.Select(p => new PropertyInfo
            {
                Name = p.Name,
                TypeName = p.Type.ToDisplayString()
            }).ToList(),
            CompensationProperties = compensationProperties.Select(p => new PropertyInfo
            {
                Name = p.Name,
                TypeName = p.Type.ToDisplayString()
            }).ToList(),
            CompensationConstructorParams = compensationConstructor?.Parameters.Select(p => new PropertyInfo
            {
                Name = p.Name,
                TypeName = p.Type.ToDisplayString()
            }).ToList() ?? new List<PropertyInfo>(),
            ExplicitMappings = propertyMappings
        };
    }

    private static string GenerateCompensatableImplementation(CompensationInfo info)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using Catga.Abstractions;");
        sb.AppendLine("using Catga.Flow;");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(info.Namespace) && info.Namespace != "<global namespace>")
        {
            sb.AppendLine($"namespace {info.Namespace};");
            sb.AppendLine();
        }

        // Generate partial class/record implementing ICompensatable
        var typeKeyword = info.IsRecord ? "record" : "class";
        sb.AppendLine($"partial {typeKeyword} {info.TypeName} : ICompensatable<{info.ResultFullTypeName}, {info.CompensationFullTypeName}>");
        sb.AppendLine("{");

        // Generate CreateCompensation method
        sb.AppendLine($"    public {info.CompensationFullTypeName} CreateCompensation({info.ResultFullTypeName} result)");
        sb.AppendLine("    {");

        // Try to use constructor if available
        if (info.CompensationConstructorParams.Count > 0)
        {
            var args = new List<string>();
            foreach (var param in info.CompensationConstructorParams)
            {
                // Try to find matching property in result
                var mapping = FindMapping(param.Name, info);
                args.Add(mapping ?? "default!");
            }
            sb.AppendLine($"        return new {info.CompensationFullTypeName}({string.Join(", ", args)});");
        }
        else
        {
            // Use object initializer
            sb.AppendLine($"        return new {info.CompensationFullTypeName}");
            sb.AppendLine("        {");

            foreach (var prop in info.CompensationProperties)
            {
                var mapping = FindMapping(prop.Name, info);
                if (mapping != null)
                {
                    sb.AppendLine($"            {prop.Name} = {mapping},");
                }
            }

            sb.AppendLine("        };");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string? FindMapping(string targetPropertyName, CompensationInfo info)
    {
        // Check explicit mappings first
        foreach (var mapping in info.ExplicitMappings)
        {
            var parts = mapping.Split(':');
            if (parts.Length == 2 && parts[1].Equals(targetPropertyName, System.StringComparison.OrdinalIgnoreCase))
            {
                return $"result.{parts[0]}";
            }
            if (parts.Length == 1 && parts[0].Equals(targetPropertyName, System.StringComparison.OrdinalIgnoreCase))
            {
                return $"result.{parts[0]}";
            }
        }

        // Auto-map by name (case-insensitive)
        var resultProp = info.ResultProperties
            .FirstOrDefault(p => p.Name.Equals(targetPropertyName, System.StringComparison.OrdinalIgnoreCase));

        if (resultProp != null)
        {
            return $"result.{resultProp.Name}";
        }

        // Try common patterns: OrderId -> orderId, Id -> id
        var lowerName = targetPropertyName.ToLowerInvariant();
        resultProp = info.ResultProperties
            .FirstOrDefault(p => p.Name.ToLowerInvariant() == lowerName);

        if (resultProp != null)
        {
            return $"result.{resultProp.Name}";
        }

        return null;
    }

    private class CompensationInfo
    {
        public string Namespace { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public string FullTypeName { get; set; } = string.Empty;
        public bool IsRecord { get; set; }
        public string CompensationTypeName { get; set; } = string.Empty;
        public string CompensationFullTypeName { get; set; } = string.Empty;
        public string ResultTypeName { get; set; } = string.Empty;
        public string ResultFullTypeName { get; set; } = string.Empty;
        public List<PropertyInfo> ResultProperties { get; set; } = new();
        public List<PropertyInfo> CompensationProperties { get; set; } = new();
        public List<PropertyInfo> CompensationConstructorParams { get; set; } = new();
        public List<string> ExplicitMappings { get; set; } = new();
    }

    private class PropertyInfo
    {
        public string Name { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
    }
}
