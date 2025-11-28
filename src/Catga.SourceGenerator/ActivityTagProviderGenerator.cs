using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Catga.SourceGenerator;

/// <summary>
/// Generates zero-GC IActivityTagProvider implementations for IRequest/Query types.
/// Developers annotate properties with [TraceTag] and this generator emits a partial type
/// that implements Catga.Abstractions.IActivityTagProvider to set Activity tags.
/// </summary>
[Generator]
public sealed class ActivityTagProviderGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidates = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is TypeDeclarationSyntax,
            static (ctx, _) => GetInfo(ctx)
        ).Where(static x => x is not null).Collect();

        context.RegisterSourceOutput(candidates, static (spc, items) =>
        {
            var list = items!.OfType<TypeInfo>().ToList();
            foreach (var info in list)
            {
                var src = GenerateSource(info);
                if (!string.IsNullOrEmpty(src))
                {
                    var hint = $"CatgaGenerated.ActivityTags.{info.HintName}.g.cs";
                    spc.AddSource(hint, SourceText.From(src!, Encoding.UTF8));
                }
            }
        });
    }

    private static TypeInfo? GetInfo(GeneratorSyntaxContext context)
    {
        if (context.Node is not TypeDeclarationSyntax typeDecl)
            return null;
        if (context.SemanticModel.GetDeclaredSymbol(typeDecl) is not INamedTypeSymbol symbol)
            return null;
        if (symbol.IsAbstract)
            return null;

        // Only generate if the original type is declared as 'partial' to avoid merge errors
        if (!typeDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
            return null;

        // Avoid generics for simplicity (can be added later)
        if (symbol.TypeParameters.Length > 0)
            return null;

        // Collect properties with [TraceTag]
        var props = new List<PropertyInfo>();
        foreach (var member in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            foreach (var attr in member.GetAttributes())
            {
                if (attr.AttributeClass?.Name == "TraceTagAttribute")
                {
                    string? explicitName = null;
                    if (attr.ConstructorArguments.Length == 1 && attr.ConstructorArguments[0].Value is string s && !string.IsNullOrWhiteSpace(s))
                        explicitName = s;

                    props.Add(new PropertyInfo(member.Name, member.Type, explicitName));
                    break;
                }
            }
        }

        // Type-level [TraceTags] support
        var typeAttr = symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "TraceTagsAttribute");
        if (typeAttr is not null)
        {
            // Defaults
            string? prefix = null;
            bool allPublic = true;
            var include = new List<string>();
            var exclude = new HashSet<string>();

            // Constructor: (string? prefix)
            if (typeAttr.ConstructorArguments.Length >= 1 && typeAttr.ConstructorArguments[0].Value is string pfx && !string.IsNullOrWhiteSpace(pfx))
                prefix = pfx;

            // Named arguments: Prefix, AllPublic, Include, Exclude
            foreach (var (key, value) in typeAttr.NamedArguments)
            {
                if (key == "Prefix" && value.Value is string pv)
                    prefix = pv;
                else if (key == "AllPublic" && value.Value is bool b)
                    allPublic = b;
                else if (key == "Include" && value.Values is { Count: > 0 })
                {
                    foreach (var v in value.Values)
                        if (v.Value is string sv && !string.IsNullOrWhiteSpace(sv)) include.Add(sv);
                }
                else if (key == "Exclude" && value.Values is { Count: > 0 })
                {
                    foreach (var v in value.Values)
                        if (v.Value is string sv && !string.IsNullOrWhiteSpace(sv)) exclude.Add(sv);
                }
            }

            // Infer default prefix when not specified
            bool ImplementsIRequest(INamedTypeSymbol i)
            {
                var name = i.Name;
                var ns = i.ContainingNamespace?.ToDisplayString();
                return ns == "Catga.Abstractions" && name == "IRequest" && (i.TypeArguments.Length == 0 || i.TypeArguments.Length == 1);
            }
            if (string.IsNullOrWhiteSpace(prefix))
                prefix = symbol.AllInterfaces.Any(ImplementsIRequest) ? "catga.req." : "catga.res.";

            var properties = symbol.GetMembers().OfType<IPropertySymbol>().Where(p => !p.IsStatic);
            IEnumerable<IPropertySymbol> selected;
            if (include.Count > 0)
                selected = properties.Where(p => include.Contains(p.Name));
            else if (allPublic)
                selected = properties;
            else
                selected = Array.Empty<IPropertySymbol>();

            if (exclude.Count > 0)
                selected = selected.Where(p => !exclude.Contains(p.Name));

            // Deduplicate by property name: explicit property-level tags win
            var taken = new HashSet<string>(props.Select(p => p.Name));
            foreach (var p in selected)
            {
                if (!taken.Contains(p.Name))
                    props.Add(new PropertyInfo(p.Name, p.Type, prefix + p.Name));
            }
        }

        if (props.Count == 0)
            return null; // Only generate for types that actually opt-in (property-level or type-level)

        var nsName = symbol.ContainingNamespace?.ToDisplayString() ?? "";
        var isStruct = symbol.IsValueType;
        var typeName = symbol.Name; // no generics supported here
        var hintName = (string.IsNullOrEmpty(nsName) ? typeName : nsName + "." + typeName).Replace('<', '_').Replace('>', '_');
        return new TypeInfo(nsName, typeName, isStruct, hintName, props);
    }

    private static string GenerateSource(TypeInfo info)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        if (!string.IsNullOrEmpty(info.Namespace))
        {
            sb.Append("namespace ").Append(info.Namespace).AppendLine(";");
            sb.AppendLine();
        }

        var typeKeyword = info.IsStruct ? "struct" : "class";
        sb.Append("partial ").Append(typeKeyword).Append(' ').Append(info.TypeName)
          .Append(" : global::Catga.Abstractions.IActivityTagProvider")
          .AppendLine()
          .AppendLine("{")
          .AppendLine("    void global::Catga.Abstractions.IActivityTagProvider.Enrich(global::System.Diagnostics.Activity activity)")
          .AppendLine("    {");

        foreach (var p in info.Properties)
        {
            var tagName = !string.IsNullOrEmpty(p.ExplicitName) ? p.ExplicitName : $"catga.req.{p.Name}";
            // Generate minimally allocating SetTag
            sb.Append("        activity?.SetTag(\"").Append(Escape(tagName)).Append("\", ");
            // Use typed literal for common primitives to avoid boxing where possible is not necessary since Activity.SetTag accepts object?;
            sb.Append("this.").Append(p.Name).AppendLine(");");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private sealed record TypeInfo(string Namespace, string TypeName, bool IsStruct, string HintName, List<PropertyInfo> Properties)
    {
        public string Namespace { get; } = Namespace;
        public string TypeName { get; } = TypeName;
        public bool IsStruct { get; } = IsStruct;
        public string HintName { get; } = HintName;
        public List<PropertyInfo> Properties { get; } = Properties;
    }

    private sealed record PropertyInfo(string Name, ITypeSymbol Type, string? ExplicitName)
    {
        public string Name { get; } = Name;
        public ITypeSymbol Type { get; } = Type;
        public string? ExplicitName { get; } = ExplicitName;
    }
}
