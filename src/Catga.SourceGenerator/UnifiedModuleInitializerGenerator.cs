#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Catga.SourceGenerator;

/// <summary>
/// Unified generator that combines EventType registration and MediatorBatch profiles.
/// Generates a single ModuleInitializer that registers all event types and batch configurations.
/// </summary>
[Generator]
public sealed class UnifiedModuleInitializerGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Discover event types
        var eventTypes = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => IsEventCandidate(node),
                static (ctx, _) => GetEventType(ctx))
            .Where(static t => t is not null)
            .Collect();

        // Discover batch profiles
        var batchProfiles = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is TypeDeclarationSyntax,
                static (ctx, _) => GetBatchProfileInfo(ctx))
            .Where(static x => x is not null)
            .Collect();

        // Combine both and generate unified module initializer
        context.RegisterSourceOutput(
            eventTypes.Combine(batchProfiles),
            static (spc, data) =>
            {
                var events = data.Left.Where(i => i is not null).Select(i => i!).ToList();
                var batches = data.Right.Where(i => i is not null).Select(i => i!).ToList();
                spc.AddSource("CatgaGenerated.UnifiedModuleInitializer.g.cs",
                    SourceText.From(GenerateModuleInitializer(events, batches), Encoding.UTF8));
            });
    }

    #region Event Type Discovery (from EventTypeRegistryGenerator)

    private static bool IsEventCandidate(SyntaxNode node)
    {
        if (node is not TypeDeclarationSyntax t) return false;
        if (t.BaseList is null || t.BaseList.Types.Count == 0) return false;
        if (t.Parent is TypeDeclarationSyntax) return false; // skip nested for simplicity
        return true;
    }

    private static string? GetEventType(GeneratorSyntaxContext context)
    {
        var t = (TypeDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(t) as INamedTypeSymbol;
        if (symbol is null) return null;
        var implementsEvent = symbol.AllInterfaces.Any(i => i.Name == "IEvent");
        if (!implementsEvent) return null;
        // Use ToDisplayString with FullyQualifiedFormat to get proper type name
        var fullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        // Remove global:: prefix as we add it in the template
        if (fullName.StartsWith("global::"))
            fullName = fullName.Substring(8);
        return fullName;
    }

    #endregion

    #region Batch Profile Discovery (from MediatorBatchProfilesGenerator)

    private static BatchProfileInfo? GetBatchProfileInfo(GeneratorSyntaxContext context)
    {
        var typeDecl = (TypeDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(typeDecl) is not INamedTypeSymbol symbol) return null;
        if (symbol.IsAbstract) return null;

        bool IsRequestInterface(INamedTypeSymbol i)
        {
            var name = i.Name;
            var ns = i.ContainingNamespace?.ToDisplayString();
            return ns == "Catga.Abstractions" && name == "IRequest" && (i.TypeArguments.Length == 0 || i.TypeArguments.Length == 1);
        }

        bool implementsIRequest = symbol.AllInterfaces.Any(IsRequestInterface);
        if (!implementsIRequest) return null;

        // Read attributes on the request type
        var attrs = symbol.GetAttributes();
        var optionsAttr = attrs.FirstOrDefault(a => a.AttributeClass?.Name == "BatchOptionsAttribute");
        var keyAttr = attrs.FirstOrDefault(a => a.AttributeClass?.Name == "BatchKeyAttribute");

        if (optionsAttr is null && keyAttr is null)
        {
            // No need to generate anything for this request type
            return null;
        }

        var info = new BatchProfileInfo(symbol.ToDisplayString());

        if (optionsAttr is not null)
        {
            int GetInt(string name)
            {
                var arg = optionsAttr.NamedArguments.FirstOrDefault(kv => kv.Key == name).Value;
                return arg.Value is int v ? v : 0;
            }

            info.MaxBatchSize = GetInt("MaxBatchSize");
            info.BatchTimeoutMs = GetInt("BatchTimeoutMs");
            info.MaxQueueLength = GetInt("MaxQueueLength");
            info.ShardIdleTtlMs = GetInt("ShardIdleTtlMs");
            info.MaxShards = GetInt("MaxShards");
            info.FlushDegree = GetInt("FlushDegree");
        }

        if (keyAttr is not null)
        {
            // Positional ctor: BatchKeyAttribute(string propertyName)
            if (keyAttr.ConstructorArguments.Length == 1 && keyAttr.ConstructorArguments[0].Value is string s)
            {
                info.KeyPropertyName = s;
                // Try to get property type
                var prop = symbol.GetMembers().OfType<IPropertySymbol>().FirstOrDefault(p => p.Name == s);
                if (prop is not null)
                {
                    info.KeyIsString = prop.Type.SpecialType == SpecialType.System_String;
                    info.KeyIsValueType = prop.Type.IsValueType && prop.Type.SpecialType != SpecialType.System_String;
                }
            }
        }

        return info;
    }

    #endregion

    #region Code Generation

    private static string GenerateModuleInitializer(
        IReadOnlyList<string> eventTypes,
        IReadOnlyList<BatchProfileInfo> batchProfiles)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine();
        sb.AppendLine("namespace Catga.Generated");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Unified module initializer for event types and batch profiles.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    internal static class CatgaUnifiedModuleInitializer");
        sb.AppendLine("    {");
        sb.AppendLine("        [ModuleInitializer]");
        sb.AppendLine("        internal static void Initialize()");
        sb.AppendLine("        {");

        // Register event types
        if (eventTypes.Count > 0)
        {
            sb.AppendLine("            // Register event types");
            foreach (var fullName in eventTypes.Distinct().OrderBy(n => n))
            {
                sb.AppendLine($"            Catga.Generated.EventTypeRegistry.Register<global::{fullName}>();");
            }
            sb.AppendLine();
        }

        // Register batch profiles
        if (batchProfiles.Count > 0)
        {
            sb.AppendLine("            // Register mediator batch profiles");
            foreach (var info in batchProfiles)
            {
                if (info.HasAnyOptions)
                {
                    sb.Append("            global::Catga.Pipeline.MediatorBatchProfiles.RegisterOptionsTransformer<");
                    sb.Append(info.RequestType);
                    sb.Append(">(static global => global with { ");
                    bool first = true;
                    void AppendComma()
                    {
                        if (!first) sb.Append(", ");
                        first = false;
                    }
                    if (info.MaxBatchSize > 0) { AppendComma(); sb.Append("MaxBatchSize = ").Append(info.MaxBatchSize); }
                    if (info.BatchTimeoutMs > 0) { AppendComma(); sb.Append("BatchTimeout = global::System.TimeSpan.FromMilliseconds(").Append(info.BatchTimeoutMs).Append(")"); }
                    if (info.MaxQueueLength > 0) { AppendComma(); sb.Append("MaxQueueLength = ").Append(info.MaxQueueLength); }
                    if (info.ShardIdleTtlMs > 0) { AppendComma(); sb.Append("ShardIdleTtl = global::System.TimeSpan.FromMilliseconds(").Append(info.ShardIdleTtlMs).Append(")"); }
                    if (info.MaxShards > 0) { AppendComma(); sb.Append("MaxShards = ").Append(info.MaxShards); }
                    if (info.FlushDegree > 0) { AppendComma(); sb.Append("FlushDegree = ").Append(info.FlushDegree); }
                    sb.Append(" });");
                    sb.AppendLine();
                }

                if (!string.IsNullOrWhiteSpace(info.KeyPropertyName))
                {
                    sb.Append("            global::Catga.Pipeline.MediatorBatchProfiles.RegisterKeySelector<");
                    sb.Append(info.RequestType);
                    sb.Append(">(static r => ");
                    if (info.KeyIsString)
                    {
                        sb.Append("r.").Append(info.KeyPropertyName);
                    }
                    else if (info.KeyIsValueType)
                    {
                        sb.Append("r.").Append(info.KeyPropertyName).Append(".ToString()");
                    }
                    else
                    {
                        sb.Append("r.").Append(info.KeyPropertyName).Append("?.ToString()");
                    }
                    sb.AppendLine(");");
                }
            }
        }

        if (eventTypes.Count == 0 && batchProfiles.Count == 0)
        {
            sb.AppendLine("            // No event types or batch profiles discovered");
        }

        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    #endregion

    #region Data Models

    private sealed class BatchProfileInfo
    {
        public string RequestType { get; }
        public int MaxBatchSize { get; set; }
        public int BatchTimeoutMs { get; set; }
        public int MaxQueueLength { get; set; }
        public int ShardIdleTtlMs { get; set; }
        public int MaxShards { get; set; }
        public int FlushDegree { get; set; }
        public string? KeyPropertyName { get; set; }
        public bool KeyIsString { get; set; }
        public bool KeyIsValueType { get; set; }

        public bool HasAnyOptions => MaxBatchSize > 0 || BatchTimeoutMs > 0 || MaxQueueLength > 0 ||
                                     ShardIdleTtlMs > 0 || MaxShards > 0 || FlushDegree > 0;

        public BatchProfileInfo(string requestType)
        {
            RequestType = requestType;
        }
    }

    #endregion
}
