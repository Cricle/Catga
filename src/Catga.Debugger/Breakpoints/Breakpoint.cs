using System;

namespace Catga.Debugger.Breakpoints;

/// <summary>
/// Represents a breakpoint that can pause message processing.
/// Production-safe: Only active when explicitly enabled.
/// </summary>
public sealed class Breakpoint
{
    /// <summary>
    /// Unique identifier
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Display name
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Whether the breakpoint is enabled
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The condition that must be met for the breakpoint to trigger
    /// </summary>
    public BreakpointCondition Condition { get; set; }

    /// <summary>
    /// Number of times this breakpoint has been hit
    /// </summary>
    public int HitCount { get; private set; }

    /// <summary>
    /// When the breakpoint was created
    /// </summary>
    public DateTime CreatedAt { get; }

    /// <summary>
    /// When the breakpoint was last hit
    /// </summary>
    public DateTime? LastHitAt { get; private set; }

    /// <summary>
    /// The correlation ID of the last message that hit this breakpoint
    /// </summary>
    public string? LastCorrelationId { get; private set; }

    public Breakpoint(string id, string name, BreakpointCondition condition, bool enabled = true)
    {
        Id = id;
        Name = name;
        Condition = condition;
        Enabled = enabled;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Records a hit on this breakpoint
    /// </summary>
    public void RecordHit(string correlationId)
    {
        HitCount++;
        LastHitAt = DateTime.UtcNow;
        LastCorrelationId = correlationId;
    }

    /// <summary>
    /// Evaluates whether this breakpoint should trigger for a given message
    /// </summary>
    public bool ShouldTrigger(object message)
    {
        if (!Enabled)
            return false;

        return Condition.Evaluate(message);
    }
}

