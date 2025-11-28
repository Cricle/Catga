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
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var info in list)
            {
                var hint = $"CatgaGenerated.ActivityTags.{info.HintName}.g.cs";
                if (!used.Add(hint))
                    continue; // skip duplicates from multiple partial declarations

                var src = GenerateSource(info);
                if (!string.IsNullOrEmpty(src))
                {
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
            foreach (var kv in typeAttr.NamedArguments)
            {
                var key = kv.Key;
                var value = kv.Value;
                if (key == "Prefix" && value.Value is string pv)
                    prefix = pv;
                else if (key == "AllPublic" && value.Value is bool b)
                    allPublic = b;
                else if (key == "Include" && value.Values.Length > 0)
                {
                    foreach (var v in value.Values)
                        if (v.Value is string sv && !string.IsNullOrWhiteSpace(sv)) include.Add(sv);
                }
                else if (key == "Exclude" && value.Values.Length > 0)
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

            var allProps = symbol.GetMembers().OfType<IPropertySymbol>().Where(p => !p.IsStatic);
            IEnumerable<IPropertySymbol> selected;
            if (include.Count > 0)
            {
                selected = allProps.Where(p => include.Contains(p.Name));
            }
            else if (allPublic)
            {
                selected = allProps.Where(p => p.DeclaredAccessibility == Accessibility.Public);
            }
            else
            {
                selected = Array.Empty<IPropertySymbol>();
            }

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
        var isRecord = symbol.IsRecord;
        var typeName = symbol.Name; // no generics supported here
        // Build containing type chain for nested types
        var containers = new List<ContainerInfo>();
        var ct = symbol.ContainingType;
        while (ct != null)
        {
            var acc = ct.DeclaredAccessibility switch
            {
                Accessibility.Public => "public ",
                Accessibility.Internal => "internal ",
                Accessibility.Protected => "protected ",
                Accessibility.Private => "private ",
                Accessibility.ProtectedOrInternal => "protected internal ",
                Accessibility.ProtectedAndInternal => "private protected ",
                _ => string.Empty
            };
            containers.Insert(0, new ContainerInfo(ct.Name, ct.IsValueType, ct.IsRecord, acc));
            ct = ct.ContainingType;
        }
        var namePath = containers.Count == 0 ? typeName : string.Join(".", containers.Select(c => c.Name)) + "." + typeName;
        var hintName = (string.IsNullOrEmpty(nsName) ? namePath : nsName + "." + namePath).Replace('<', '_').Replace('>', '_');
        var accessibility = symbol.DeclaredAccessibility switch
        {
            Accessibility.Public => "public ",
            Accessibility.Internal => "internal ",
            Accessibility.Protected => "protected ",
            Accessibility.Private => "private ",
            Accessibility.ProtectedOrInternal => "protected internal ",
            Accessibility.ProtectedAndInternal => "private protected ",
            _ => string.Empty
        };
        return new TypeInfo(nsName, typeName, isStruct, isRecord, hintName, props, accessibility, containers);
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

        // Ensure partial declaration matches original accessibility and kind (record/class, struct)
        // If nested, generate partial wrappers for containing types
        foreach (var c in info.Containers)
        {
            string ck = c.IsRecord ? (c.IsStruct ? "record struct" : "record") : (c.IsStruct ? "struct" : "class");
            sb.Append(c.Accessibility).Append("partial ").Append(ck).Append(' ').Append(c.Name).AppendLine()
              .AppendLine("{");
        }
        string accessibility = info.Accessibility;
        string kind = info.IsRecord
            ? (info.IsStruct ? "record struct" : "record")
            : (info.IsStruct ? "struct" : "class");
        sb.Append(accessibility).Append("partial ").Append(kind).Append(' ').Append(info.TypeName)
          .Append(" : global::Catga.Abstractions.IActivityTagProvider")
          .AppendLine()
          .AppendLine("{")
          .AppendLine("    void global::Catga.Abstractions.IActivityTagProvider.Enrich(global::System.Diagnostics.Activity activity)")
          .AppendLine("    {");

        foreach (var p in info.Properties)
        {
            var tn = p.ExplicitName;
            var tagName = string.IsNullOrEmpty(tn) ? $"catga.req.{p.Name}" : tn!;
            // Generate minimally allocating SetTag
            sb.Append("        activity?.SetTag(\"").Append(Escape(tagName)).Append("\", ");
            // Use typed literal for common primitives to avoid boxing where possible is not necessary since Activity.SetTag accepts object?;
            sb.Append("this.").Append(p.Name).AppendLine(");");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        for (int i = 0; i < info.Containers.Count; i++) sb.AppendLine("}");
        return sb.ToString();
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private sealed record TypeInfo(string Namespace, string TypeName, bool IsStruct, bool IsRecord, string HintName, List<PropertyInfo> Properties, string Accessibility, List<ContainerInfo> Containers)
    {
        public string Namespace { get; } = Namespace;
        public string TypeName { get; } = TypeName;
        public bool IsStruct { get; } = IsStruct;
        public bool IsRecord { get; } = IsRecord;
        public string HintName { get; } = HintName;
        public List<PropertyInfo> Properties { get; } = Properties;
        public string Accessibility { get; } = Accessibility;
        public List<ContainerInfo> Containers { get; } = Containers;
    }

    private sealed record PropertyInfo(string Name, ITypeSymbol Type, string? ExplicitName)
    {
        public string Name { get; } = Name;
        public ITypeSymbol Type { get; } = Type;
        public string? ExplicitName { get; } = ExplicitName;
    }

    private sealed record ContainerInfo(string Name, bool IsStruct, bool IsRecord, string Accessibility)
    {
        public string Name { get; } = Name;
        public bool IsStruct { get; } = IsStruct;
        public bool IsRecord { get; } = IsRecord;
        public string Accessibility { get; } = Accessibility;
    }
}
