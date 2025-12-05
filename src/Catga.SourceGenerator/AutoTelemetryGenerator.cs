using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Catga.SourceGenerator;

/// <summary>
/// Source generator for automatic telemetry wrapping.
/// Generates HandleAsync that wraps user's HandleAsyncCore with:
/// - Automatic Activity/Span creation
/// - Automatic metrics (duration, count, errors)
/// - Automatic tag extraction from Request properties
/// - Custom metrics via [Metric] attribute
/// </summary>
[Generator]
public class AutoTelemetryGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Register attributes
        context.RegisterPostInitializationOutput(ctx =>
        {
            ctx.AddSource("AutoTelemetryAttributes.g.cs", SourceText.From(AttributesSource, Encoding.UTF8));
        });

        // Find handlers with [CatgaHandler] that have HandleAsyncCore
        var handlerProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Catga.CatgaHandlerAttribute",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => GetHandlerInfo(ctx))
            .Where(static h => h is not null)
            .Collect();

        context.RegisterSourceOutput(handlerProvider, static (spc, handlers) =>
        {
            foreach (var handler in handlers)
            {
                if (handler is null) continue;
                var source = GenerateHandlerWrapper(handler);
                spc.AddSource($"{handler.ClassName}.AutoTelemetry.g.cs", SourceText.From(source, Encoding.UTF8));
            }
        });
    }

    private static HandlerInfo? GetHandlerInfo(GeneratorAttributeSyntaxContext ctx)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.TargetNode;
        var classSymbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
        if (classSymbol is null) return null;

        // Check if partial
        if (!classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
        {
            return null; // Diagnostic will be reported by analyzer
        }

        // Find HandleAsyncCore method
        var coreMethod = classSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => m.Name == "HandleAsyncCore");

        if (coreMethod is null) return null;

        // Get request/response types from interface
        var handlerInterface = classSymbol.AllInterfaces
            .FirstOrDefault(i =>
                i.OriginalDefinition.ToDisplayString().StartsWith("Catga.Handlers.IRequestHandler") ||
                i.OriginalDefinition.ToDisplayString().StartsWith("Catga.Abstractions.IRequestHandler"));

        if (handlerInterface is null) return null;

        var requestType = handlerInterface.TypeArguments[0];
        var responseType = handlerInterface.TypeArguments.Length > 1
            ? handlerInterface.TypeArguments[1]
            : null;

        // Get attribute settings
        var attr = ctx.Attributes.First();
        string? activitySource = null;
        string? meter = null;

        foreach (var arg in attr.NamedArguments)
        {
            if (arg.Key == "ActivitySource" && arg.Value.Value is string source)
                activitySource = source;
            if (arg.Key == "Meter" && arg.Value.Value is string m)
                meter = m;
        }

        // Get [Metric] attributes
        var metrics = classSymbol.GetAttributes()
            .Where(a => a.AttributeClass?.Name == "MetricAttribute")
            .Select(a => new MetricInfo
            {
                Name = a.ConstructorArguments.FirstOrDefault().Value?.ToString() ?? "",
                Type = a.NamedArguments.FirstOrDefault(x => x.Key == "Type").Value.Value is int t ? (MetricType)t : MetricType.Counter,
                Unit = a.NamedArguments.FirstOrDefault(x => x.Key == "Unit").Value.Value?.ToString(),
                Meter = a.NamedArguments.FirstOrDefault(x => x.Key == "Meter").Value.Value?.ToString()
            })
            .ToList();

        // Get request properties for auto-tagging
        var requestProps = GetTaggableProperties(requestType);

        return new HandlerInfo
        {
            Namespace = classSymbol.ContainingNamespace.ToDisplayString(),
            ClassName = classSymbol.Name,
            RequestType = requestType.ToDisplayString(),
            ResponseType = responseType?.ToDisplayString(),
            HasResponse = responseType is not null,
            ActivitySource = activitySource,
            Meter = meter,
            Metrics = metrics,
            RequestProperties = requestProps
        };
    }

    private static List<PropertyInfo> GetTaggableProperties(ITypeSymbol type)
    {
        var props = new List<PropertyInfo>();

        foreach (var member in type.GetMembers().OfType<IPropertySymbol>())
        {
            // Skip if has [NoTag]
            if (member.GetAttributes().Any(a => a.AttributeClass?.Name == "NoTagAttribute"))
                continue;

            // Only include simple types
            if (IsSimpleType(member.Type))
            {
                props.Add(new PropertyInfo
                {
                    Name = member.Name,
                    TagName = ToSnakeCase(member.Name),
                    Type = member.Type.ToDisplayString()
                });
            }
        }

        return props;
    }

    private static bool IsSimpleType(ITypeSymbol type)
    {
        return type.SpecialType switch
        {
            SpecialType.System_String => true,
            SpecialType.System_Int32 => true,
            SpecialType.System_Int64 => true,
            SpecialType.System_Boolean => true,
            SpecialType.System_Decimal => true,
            SpecialType.System_Double => true,
            _ => false
        };
    }

    private static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0)
            {
                sb.Append('_');
            }
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    private static string GenerateHandlerWrapper(HandlerInfo handler)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Diagnostics;");
        sb.AppendLine("using System.Diagnostics.Metrics;");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Catga;");
        sb.AppendLine("using Catga.Core;");
        sb.AppendLine();
        sb.AppendLine($"namespace {handler.Namespace};");
        sb.AppendLine();
        sb.AppendLine($"partial class {handler.ClassName}");
        sb.AppendLine("{");

        // Static telemetry fields
        var activitySource = handler.ActivitySource ?? "CatgaTelemetry.DefaultSource";
        var meter = handler.Meter ?? "CatgaTelemetry.DefaultMeter";

        sb.AppendLine($"    private static readonly ActivitySource s_activitySource = {activitySource};");
        sb.AppendLine($"    private static readonly Meter s_meter = {meter};");
        sb.AppendLine();

        // Default metrics
        sb.AppendLine($"    private static readonly Histogram<double> s_duration = s_meter.CreateHistogram<double>(\"catga.handler.duration\", \"ms\", \"Handler execution duration\");");
        sb.AppendLine($"    private static readonly Counter<long> s_count = s_meter.CreateCounter<long>(\"catga.handler.count\", description: \"Handler execution count\");");
        sb.AppendLine($"    private static readonly Counter<long> s_errors = s_meter.CreateCounter<long>(\"catga.handler.errors\", description: \"Handler error count\");");
        sb.AppendLine();

        // Custom metrics
        foreach (var metric in handler.Metrics)
        {
            var fieldName = $"s_{ToFieldName(metric.Name)}";
            var methodName = ToMethodName(metric.Name);

            if (metric.Type == MetricType.Counter)
            {
                var meterRef = metric.Meter ?? "s_meter";
                sb.AppendLine($"    private static readonly Counter<long> {fieldName} = {meterRef}.CreateCounter<long>(\"{metric.Name}\");");
                sb.AppendLine();
                sb.AppendLine($"    [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                sb.AppendLine($"    private void Record{methodName}(in TagList tags = default) => {fieldName}.Add(1, tags);");
            }
            else
            {
                var meterRef = metric.Meter ?? "s_meter";
                var unit = metric.Unit != null ? $", \"{metric.Unit}\"" : "";
                sb.AppendLine($"    private static readonly Histogram<double> {fieldName} = {meterRef}.CreateHistogram<double>(\"{metric.Name}\"{unit});");
                sb.AppendLine();
                sb.AppendLine($"    [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                sb.AppendLine($"    private void Record{methodName}(double value, in TagList tags = default) => {fieldName}.Record(value, tags);");
            }
            sb.AppendLine();
        }

        // Generate HandleAsync
        var returnType = handler.HasResponse
            ? $"Task<CatgaResult<{handler.ResponseType}>>"
            : "Task<CatgaResult>";

        sb.AppendLine($"    public async {returnType} HandleAsync({handler.RequestType} request, CancellationToken ct = default)");
        sb.AppendLine("    {");
        sb.AppendLine($"        using var activity = s_activitySource.StartActivity(\"{handler.ClassName}\");");

        // Auto-tag from request properties
        foreach (var prop in handler.RequestProperties)
        {
            sb.AppendLine($"        activity?.SetTag(\"{prop.TagName}\", request.{prop.Name});");
        }

        sb.AppendLine();
        sb.AppendLine("        var sw = Stopwatch.StartNew();");
        sb.AppendLine("        var success = false;");
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        sb.AppendLine("            var result = await HandleAsyncCore(request, ct);");
        sb.AppendLine("            success = result.IsSuccess;");
        sb.AppendLine("            return result;");
        sb.AppendLine("        }");
        sb.AppendLine("        catch (Exception ex)");
        sb.AppendLine("        {");
        sb.AppendLine("            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);");
        sb.AppendLine($"            s_errors.Add(1, new KeyValuePair<string, object?>(\"handler\", \"{handler.ClassName}\"), new KeyValuePair<string, object?>(\"error\", ex.GetType().Name));");
        sb.AppendLine("            throw;");
        sb.AppendLine("        }");
        sb.AppendLine("        finally");
        sb.AppendLine("        {");
        sb.AppendLine("            sw.Stop();");
        sb.AppendLine($"            s_duration.Record(sw.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>(\"handler\", \"{handler.ClassName}\"), new KeyValuePair<string, object?>(\"success\", success));");
        sb.AppendLine($"            s_count.Add(1, new KeyValuePair<string, object?>(\"handler\", \"{handler.ClassName}\"), new KeyValuePair<string, object?>(\"success\", success));");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string ToFieldName(string metricName)
    {
        return metricName.Replace(".", "_").Replace("-", "_");
    }

    private static string ToMethodName(string metricName)
    {
        var parts = metricName.Split('.', '-');
        return string.Join("", parts.Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
    }

    private const string AttributesSource = """
        // <auto-generated/>
        #nullable enable

        namespace Catga;

        /// <summary>
        /// Adds a custom metric to the handler.
        /// </summary>
        [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
        public sealed class MetricAttribute : System.Attribute
        {
            public string Name { get; }
            public MetricType Type { get; set; } = MetricType.Counter;
            public string? Unit { get; set; }
            public string? Meter { get; set; }

            public MetricAttribute(string name) => Name = name;
        }

        /// <summary>
        /// Metric type for [Metric] attribute.
        /// </summary>
        public enum MetricType
        {
            Counter = 0,
            Histogram = 1
        }

        /// <summary>
        /// Excludes a property from automatic tag extraction.
        /// </summary>
        [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Parameter)]
        public sealed class NoTagAttribute : System.Attribute { }

        /// <summary>
        /// Default telemetry instances.
        /// </summary>
        public static class CatgaTelemetry
        {
            public static readonly System.Diagnostics.ActivitySource DefaultSource = new("Catga", "1.0.0");
            public static readonly System.Diagnostics.Metrics.Meter DefaultMeter = new("Catga", "1.0.0");
        }

        // ============================================================
        // Distributed Capability Attributes
        // ============================================================

        /// <summary>
        /// Enables idempotent execution using Inbox pattern.
        /// Duplicate requests with same IdempotencyKey are rejected.
        /// </summary>
        [System.AttributeUsage(System.AttributeTargets.Class)]
        public sealed class IdempotentAttribute : System.Attribute
        {
            /// <summary>Key expression, e.g. "{request.OrderId}" or "{request.CustomerId}:{request.OrderId}"</summary>
            public string? Key { get; set; }
            /// <summary>Time-to-live for idempotency record. Default: 24 hours.</summary>
            public int TtlSeconds { get; set; } = 86400;
        }

        /// <summary>
        /// Acquires distributed lock before execution.
        /// </summary>
        [System.AttributeUsage(System.AttributeTargets.Class)]
        public sealed class DistributedLockAttribute : System.Attribute
        {
            /// <summary>Lock key expression, e.g. "order:{request.CustomerId}"</summary>
            public string Key { get; }
            /// <summary>Lock timeout in seconds. Default: 30.</summary>
            public int TimeoutSeconds { get; set; } = 30;
            /// <summary>Wait timeout in seconds. Default: 10.</summary>
            public int WaitSeconds { get; set; } = 10;

            public DistributedLockAttribute(string key) => Key = key;
        }

        /// <summary>
        /// Enables automatic retry on transient failures.
        /// </summary>
        [System.AttributeUsage(System.AttributeTargets.Class)]
        public sealed class RetryAttribute : System.Attribute
        {
            /// <summary>Maximum retry attempts. Default: 3.</summary>
            public int MaxAttempts { get; set; } = 3;
            /// <summary>Initial delay in milliseconds. Default: 100.</summary>
            public int DelayMs { get; set; } = 100;
            /// <summary>Use exponential backoff. Default: true.</summary>
            public bool Exponential { get; set; } = true;
        }

        /// <summary>
        /// Sets execution timeout.
        /// </summary>
        [System.AttributeUsage(System.AttributeTargets.Class)]
        public sealed class TimeoutAttribute : System.Attribute
        {
            /// <summary>Timeout in seconds.</summary>
            public int Seconds { get; }
            public TimeoutAttribute(int seconds) => Seconds = seconds;
        }

        /// <summary>
        /// Enables circuit breaker pattern.
        /// </summary>
        [System.AttributeUsage(System.AttributeTargets.Class)]
        public sealed class CircuitBreakerAttribute : System.Attribute
        {
            /// <summary>Failure threshold before opening. Default: 5.</summary>
            public int FailureThreshold { get; set; } = 5;
            /// <summary>Duration circuit stays open in seconds. Default: 30.</summary>
            public int BreakDurationSeconds { get; set; } = 30;
        }

        // ============================================================
        // Cluster Capability Attributes
        // ============================================================

        /// <summary>
        /// Handler only executes on cluster leader node.
        /// Non-leader nodes forward request to leader.
        /// </summary>
        [System.AttributeUsage(System.AttributeTargets.Class)]
        public sealed class LeaderOnlyAttribute : System.Attribute { }

        /// <summary>
        /// Routes request to specific shard based on key.
        /// </summary>
        [System.AttributeUsage(System.AttributeTargets.Class)]
        public sealed class ShardedAttribute : System.Attribute
        {
            /// <summary>Shard key expression, e.g. "{request.CustomerId}"</summary>
            public string Key { get; }
            public ShardedAttribute(string key) => Key = key;
        }

        /// <summary>
        /// Broadcasts request to all cluster nodes.
        /// </summary>
        [System.AttributeUsage(System.AttributeTargets.Class)]
        public sealed class BroadcastAttribute : System.Attribute { }

        /// <summary>
        /// Marks a background task that runs as singleton across cluster.
        /// </summary>
        [System.AttributeUsage(System.AttributeTargets.Class)]
        public sealed class ClusterSingletonAttribute : System.Attribute { }
        """;

    private class HandlerInfo
    {
        public string Namespace { get; set; } = "";
        public string ClassName { get; set; } = "";
        public string RequestType { get; set; } = "";
        public string? ResponseType { get; set; }
        public bool HasResponse { get; set; }
        public string? ActivitySource { get; set; }
        public string? Meter { get; set; }
        public List<MetricInfo> Metrics { get; set; } = new();
        public List<PropertyInfo> RequestProperties { get; set; } = new();
    }

    private class MetricInfo
    {
        public string Name { get; set; } = "";
        public MetricType Type { get; set; }
        public string? Unit { get; set; }
        public string? Meter { get; set; }
    }

    private enum MetricType { Counter, Histogram }

    private class PropertyInfo
    {
        public string Name { get; set; } = "";
        public string TagName { get; set; } = "";
        public string Type { get; set; } = "";
    }
}
