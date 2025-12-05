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
        var typeCandidates = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => IsPotentiallyAnnotatedType(node),
            static (ctx, _) => GetInfoFromType(ctx)
        ).Where(static x => x is not null);

        var propertyCandidates = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is PropertyDeclarationSyntax p && p.AttributeLists.Count > 0,
            static (ctx, _) => GetInfoFromProperty(ctx)
        ).Where(static x => x is not null);

        // Handle record primary constructor parameters with `[property: TraceTag]`
        var parameterCandidates = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is ParameterSyntax ps && ps.AttributeLists.Count > 0,
            static (ctx, _) => GetInfoFromParameter(ctx)
        ).Where(static x => x is not null);

        var merged = typeCandidates.Collect().Combine(propertyCandidates.Collect()).Combine(parameterCandidates.Collect());

        context.RegisterSourceOutput(merged, static (spc, triple) =>
        {
            var list = new List<TypeInfo>();
            list.AddRange(triple.Left.Left!.OfType<TypeInfo>());
            list.AddRange(triple.Left.Right!.OfType<TypeInfo>());
            list.AddRange(triple.Right!.OfType<TypeInfo>());
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

    private static bool IsPotentiallyAnnotatedType(SyntaxNode node)
    {
        if (node is not TypeDeclarationSyntax t)
            return false;
        if (t.AttributeLists.Count > 0)
        {
            foreach (var list in t.AttributeLists)
                foreach (var a in list.Attributes)
                {
                    var n = a.Name.ToString();
                    if (IsTraceTagsAttributeName(n)) return true;
                }
        }
        foreach (var m in t.Members)
        {
            if (m is PropertyDeclarationSyntax p && p.AttributeLists.Count > 0)
            {
                foreach (var list in p.AttributeLists)
                    foreach (var a in list.Attributes)
                    {
                        var n = a.Name.ToString();
                        if (IsTraceTagAttributeName(n)) return true;
                    }
            }
        }
        return false;
    }

    private static bool IsTraceTagsAttributeName(string name)
        => name.EndsWith("TraceTags", StringComparison.Ordinal) || name.EndsWith("TraceTagsAttribute", StringComparison.Ordinal);

    private static bool IsTraceTagAttributeName(string name)
        => name.EndsWith("TraceTag", StringComparison.Ordinal) || name.EndsWith("TraceTagAttribute", StringComparison.Ordinal);

    private static TypeInfo? GetInfoFromType(GeneratorSyntaxContext context)
    {
        if (context.Node is not TypeDeclarationSyntax typeDecl)
            return null;
        if (context.SemanticModel.GetDeclaredSymbol(typeDecl) is not INamedTypeSymbol symbol)
            return null;
        return BuildInfo(symbol);
    }

    private static TypeInfo? GetInfoFromProperty(GeneratorSyntaxContext context)
    {
        if (context.Node is not PropertyDeclarationSyntax propDecl)
            return null;
        if (context.SemanticModel.GetDeclaredSymbol(propDecl) is not IPropertySymbol propSymbol)
            return null;
        var typeSymbol = propSymbol.ContainingType;
        return BuildInfo(typeSymbol);
    }

    private static TypeInfo? GetInfoFromParameter(GeneratorSyntaxContext context)
    {
        if (context.Node is not ParameterSyntax param)
            return null;
        if (context.SemanticModel.GetDeclaredSymbol(param) is not IParameterSymbol paramSymbol)
            return null;
        var typeSymbol = paramSymbol.ContainingType;
        return BuildInfo(typeSymbol);
    }

    private static TypeInfo? BuildInfo(INamedTypeSymbol symbol)
    {
        if (symbol is null || symbol.IsAbstract)
            return null;

        // Only generate for partial types
        var isPartial = false;
        foreach (var r in symbol.DeclaringSyntaxReferences)
        {
            if (r.GetSyntax() is TypeDeclarationSyntax t && t.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
            {
                isPartial = true;
                break;
            }
        }
        if (!isPartial)
            return null;

        if (symbol.TypeParameters.Length > 0)
            return null;

        // Collect properties with [TraceTag]
        var props = new List<PropertyInfo>();
        foreach (var member in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            foreach (var attr in member.GetAttributes())
            {
                var attrName = attr.AttributeClass?.Name;
                if (attrName == "TraceTagAttribute")
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
            string? prefix = null;
            bool allPublic = true;
            var include = new List<string>();
            var exclude = new HashSet<string>();

            if (typeAttr.ConstructorArguments.Length >= 1 && typeAttr.ConstructorArguments[0].Value is string pfx && !string.IsNullOrWhiteSpace(pfx))
                prefix = pfx;

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
                selected = allProps.Where(p => include.Contains(p.Name));
            else if (allPublic)
                selected = allProps.Where(p => p.DeclaredAccessibility == Accessibility.Public);
            else
                selected = Array.Empty<IPropertySymbol>();

            if (exclude.Count > 0)
                selected = selected.Where(p => !exclude.Contains(p.Name));

            var taken = new HashSet<string>(props.Select(p => p.Name));
            foreach (var p in selected)
            {
                if (!taken.Contains(p.Name))
                    props.Add(new PropertyInfo(p.Name, p.Type, prefix + p.Name));
            }
        }

        if (props.Count == 0)
            return null;

        var nsName = symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        var isStruct = symbol.IsValueType;
        var isRecord = symbol.IsRecord;
        var typeName = symbol.Name;

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
        sb.EnsureCapacity(256 + (info.Properties?.Count ?? 0) * 64 + (info.Containers?.Count ?? 0) * 32);
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        if (!string.IsNullOrEmpty(info.Namespace))
        {
            sb.Append("namespace ").Append(info.Namespace).AppendLine(";");
            sb.AppendLine();
        }

        // Ensure partial declaration matches original accessibility and kind (record/class, struct)
        // If nested, generate partial wrappers for containing types
        var __containers = info.Containers;
        if (__containers != null)
            foreach (var c in __containers)
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

        var __props = info.Properties;
        if (__props != null)
            foreach (var p in __props)
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
        var __containerCount = __containers?.Count ?? 0;
        for (int i = 0; i < __containerCount; i++) sb.AppendLine("}");
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
