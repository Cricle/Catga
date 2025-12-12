using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Catga.SourceGenerator;

[Generator]
public class FlowStateChangeTrackingGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var provider = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: (syntax, _) => IsCandidateFlowState(syntax),
                transform: (context, _) => GetFlowStateInfo(context))
            .Where(info => info != null);

        context.RegisterSourceOutput(provider, (context, info) =>
        {
            if (info != null)
            {
                var source = GenerateFlowStateImplementation(info);
                context.AddSource($"{info.ClassName}.g.cs", source);
            }
        });
    }

    private static bool IsCandidateFlowState(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax classDecl &&
               classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)) &&
               classDecl.AttributeLists.Any(al =>
                   al.Attributes.Any(a =>
                       a.Name.ToString().Contains("FlowState")));
    }

    private static FlowStateInfo? GetFlowStateInfo(GeneratorSyntaxContext context)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        var classSymbol = semanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
        if (classSymbol == null)
            return null;

        var fields = new List<FlowStateField>();
        int fieldIndex = 0;

        foreach (var member in classDecl.Members)
        {
            if (member is FieldDeclarationSyntax fieldDecl)
            {
                var hasAttribute = fieldDecl.AttributeLists.Any(al =>
                    al.Attributes.Any(a =>
                        a.Name.ToString().Contains("FlowStateField")));

                if (hasAttribute)
                {
                    foreach (var variable in fieldDecl.Declaration.Variables)
                    {
                        var fieldName = variable.Identifier.Text;
                        var propertyName = ToPascalCase(fieldName);

                        fields.Add(new FlowStateField
                        {
                            FieldName = fieldName,
                            PropertyName = propertyName,
                            FieldIndex = fieldIndex,
                            Type = fieldDecl.Declaration.Type.ToString()
                        });

                        fieldIndex++;
                    }
                }
            }
        }

        if (fields.Count == 0)
            return null;

        return new FlowStateInfo
        {
            ClassName = classDecl.Identifier.Text,
            Namespace = GetNamespace(classDecl),
            Fields = fields
        };
    }

    private static string GenerateFlowStateImplementation(FlowStateInfo info)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine($"namespace {info.Namespace};");
        sb.AppendLine();
        sb.AppendLine($"public partial class {info.ClassName}");
        sb.AppendLine("{");

        // Generate bit constants
        foreach (var field in info.Fields)
        {
            sb.AppendLine($"    private const int {field.PropertyName}Bit = 1 << {field.FieldIndex};");
        }

        sb.AppendLine();
        sb.AppendLine("    private int _changedMask;");
        sb.AppendLine();

        // Generate properties with change tracking
        foreach (var field in info.Fields)
        {
            sb.AppendLine($"    public {field.Type} {field.PropertyName}");
            sb.AppendLine("    {");
            sb.AppendLine($"        get => {field.FieldName};");
            sb.AppendLine("        set");
            sb.AppendLine("        {");

            // Generate comparison based on type
            if (field.Type == "string")
            {
                sb.AppendLine($"            if (!string.Equals({field.FieldName}, value, System.StringComparison.Ordinal))");
            }
            else if (field.Type.Contains("?"))
            {
                sb.AppendLine($"            if (!EqualityComparer<{field.Type}>.Default.Equals({field.FieldName}, value))");
            }
            else
            {
                sb.AppendLine($"            if (!{field.FieldName}.Equals(value))");
            }

            sb.AppendLine("            {");
            sb.AppendLine($"                {field.FieldName} = value;");
            sb.AppendLine($"                _changedMask |= {field.PropertyName}Bit;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        // Generate IFlowState implementation
        sb.AppendLine("    public bool HasChanges => _changedMask != 0;");
        sb.AppendLine();
        sb.AppendLine("    public int GetChangedMask() => _changedMask;");
        sb.AppendLine();
        sb.AppendLine("    public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;");
        sb.AppendLine();
        sb.AppendLine("    public void ClearChanges() => _changedMask = 0;");
        sb.AppendLine();
        sb.AppendLine("    public void MarkChanged(int fieldIndex) => _changedMask |= 1 << fieldIndex;");
        sb.AppendLine();
        sb.AppendLine("    public IEnumerable<string> GetChangedFieldNames()");
        sb.AppendLine("    {");

        foreach (var field in info.Fields)
        {
            sb.AppendLine($"        if ((_changedMask & {field.PropertyName}Bit) != 0) yield return nameof({field.PropertyName});");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string GetNamespace(ClassDeclarationSyntax classDecl)
    {
        var parent = classDecl.Parent;
        while (parent != null)
        {
            if (parent is NamespaceDeclarationSyntax ns)
                return ns.Name.ToString();
            if (parent is FileScopedNamespaceDeclarationSyntax fsns)
                return fsns.Name.ToString();
            parent = parent.Parent;
        }
        return "global";
    }

    private static string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var result = input;
        if (result.StartsWith("_"))
            result = result.Substring(1);

        if (result.Length == 0)
            return input;

        return char.ToUpper(result[0]) + result.Substring(1);
    }

    private class FlowStateInfo
    {
        public string ClassName { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public List<FlowStateField> Fields { get; set; } = new();
    }

    private class FlowStateField
    {
        public string FieldName { get; set; } = string.Empty;
        public string PropertyName { get; set; } = string.Empty;
        public int FieldIndex { get; set; }
        public string Type { get; set; } = string.Empty;
    }
}
