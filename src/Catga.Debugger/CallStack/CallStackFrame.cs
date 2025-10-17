using System;
using System.Collections.Generic;

namespace Catga.Debugger.CallStack;

/// <summary>
/// Represents a single frame in the call stack during message processing.
/// </summary>
public sealed class CallStackFrame
{
    /// <summary>
    /// Method name (e.g., "HandleAsync", "CreateOrderHandler")
    /// </summary>
    public string MethodName { get; set; }

    /// <summary>
    /// Full type name (e.g., "OrderSystem.Handlers.CreateOrderHandler")
    /// </summary>
    public string TypeName { get; set; }

    /// <summary>
    /// Source file name (if available)
    /// </summary>
    public string? FileName { get; set; }

    /// <summary>
    /// Line number in source file (if available)
    /// </summary>
    public int? LineNumber { get; set; }

    /// <summary>
    /// When this frame was entered
    /// </summary>
    public DateTime EnteredAt { get; set; }

    /// <summary>
    /// When this frame was exited (null if still executing)
    /// </summary>
    public DateTime? ExitedAt { get; set; }

    /// <summary>
    /// Duration of this frame execution
    /// </summary>
    public TimeSpan Duration => ExitedAt.HasValue
        ? ExitedAt.Value - EnteredAt
        : DateTime.UtcNow - EnteredAt;

    /// <summary>
    /// Local variables captured at this frame (if enabled)
    /// </summary>
    public Dictionary<string, object?> LocalVariables { get; set; } = new();

    /// <summary>
    /// Message being processed
    /// </summary>
    public string? MessageType { get; set; }

    /// <summary>
    /// Correlation ID
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Whether this frame completed successfully
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Exception if frame failed
    /// </summary>
    public Exception? Exception { get; set; }

    /// <summary>
    /// Frame depth (0 = root)
    /// </summary>
    public int Depth { get; set; }

    public CallStackFrame(
        string methodName,
        string typeName,
        string? fileName = null,
        int? lineNumber = null)
    {
        MethodName = methodName;
        TypeName = typeName;
        FileName = fileName;
        LineNumber = lineNumber;
        EnteredAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets a display-friendly string for this frame
    /// </summary>
    public string GetDisplayString()
    {
        var location = FileName != null && LineNumber.HasValue
            ? $" at {FileName}:{LineNumber}"
            : "";

        var duration = ExitedAt.HasValue
            ? $" ({Duration.TotalMilliseconds:F2}ms)"
            : " (executing...)";

        return $"{TypeName}.{MethodName}{location}{duration}";
    }

    /// <summary>
    /// Marks this frame as exited
    /// </summary>
    public void MarkExited(bool success = true, Exception? exception = null)
    {
        ExitedAt = DateTime.UtcNow;
        Success = success;
        Exception = exception;
    }
}

