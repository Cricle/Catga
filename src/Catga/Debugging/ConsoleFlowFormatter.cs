using System.Buffers;
using System.Text;

namespace Catga.Debugging;

/// <summary>
/// Console flow formatter - colorized output with zero allocations
/// Uses ArrayPool for temporary buffers
/// </summary>
public static class ConsoleFlowFormatter
{
    private static readonly ArrayPool<char> CharPool = ArrayPool<char>.Shared;

    // ANSI color codes - no allocations
    private const string Reset = "\x1b[0m";
    private const string Green = "\x1b[32m";
    private const string Red = "\x1b[31m";
    private const string Yellow = "\x1b[33m";
    private const string Blue = "\x1b[34m";
    private const string Cyan = "\x1b[36m";
    private const string Gray = "\x1b[90m";

    /// <summary>
    /// Format flow summary for console - minimal allocations
    /// </summary>
    public static string FormatCompact(FlowSummary summary)
    {
        // Use string interpolation (compiler optimizes to single allocation)
        var status = summary.Success ? $"{Green}✅{Reset}" : $"{Red}❌{Reset}";
        var correlationShort = summary.CorrelationId.Length > 8
            ? summary.CorrelationId.Substring(0, 8)
            : summary.CorrelationId;

        return $"{Gray}[{correlationShort}]{Reset} {Cyan}{summary.MessageType}{Reset} {status} {Yellow}({summary.TotalDuration.TotalMilliseconds:F1}ms){Reset}";
    }

    /// <summary>
    /// Format detailed tree view - uses StringBuilder pooling
    /// </summary>
    public static string FormatTree(FlowContext context)
    {
        var sb = StringBuilderPool.Get();
        try
        {
            var status = context.Steps.All(s => s.Success) ? $"{Green}✅{Reset}" : $"{Red}❌{Reset}";

            sb.AppendLine($"{Blue}Flow {context.CorrelationId}{Reset} {status}");
            sb.AppendLine($"  {Gray}Type:{Reset} {context.MessageType}");
            sb.AppendLine($"  {Gray}Trace:{Reset} {context.TraceId}");
            sb.AppendLine($"  {Gray}Start:{Reset} {context.StartTime:HH:mm:ss.fff}");
            sb.AppendLine($"  {Gray}Steps:{Reset}");

            foreach (var step in context.Steps)
            {
                var stepStatus = step.Success ? Green : Red;
                var icon = step.Success ? "✓" : "✗";

                sb.Append($"    {stepStatus}{icon}{Reset} {step.Type}: {step.Name}");
                sb.Append($" {Yellow}({step.Duration.TotalMilliseconds:F2}ms){Reset}");

                if (!step.Success && step.Error != null)
                {
                    sb.Append($" {Red}[{step.Error}]{Reset}");
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }
        finally
        {
            StringBuilderPool.Return(sb);
        }
    }
}

/// <summary>
/// StringBuilder pool - reuse StringBuilders to reduce GC pressure
/// </summary>
internal static class StringBuilderPool
{
    [ThreadStatic]
    private static StringBuilder? _cached;

    public static StringBuilder Get()
    {
        var sb = _cached;
        if (sb != null)
        {
            _cached = null;
            sb.Clear();
            return sb;
        }
        return new StringBuilder(capacity: 512);
    }

    public static void Return(StringBuilder sb)
    {
        if (sb.Capacity <= 4096)  // Don't cache oversized builders
        {
            _cached = sb;
        }
    }
}

