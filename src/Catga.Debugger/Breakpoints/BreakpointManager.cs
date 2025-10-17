using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Catga.Messages;
using Microsoft.Extensions.Logging;

namespace Catga.Debugger.Breakpoints;

/// <summary>
/// Manages breakpoints for message processing.
/// Thread-safe and production-safe (zero overhead when disabled).
/// </summary>
public sealed class BreakpointManager : IDisposable
{
    private readonly ILogger<BreakpointManager> _logger;
    private readonly ConcurrentDictionary<string, Breakpoint> _breakpoints = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<DebugAction>> _pendingBreaks = new();
    private readonly bool _enabled;

    /// <summary>
    /// Event raised when a breakpoint is hit
    /// </summary>
    public event Action<BreakpointHitEventArgs>? BreakpointHit;

    public BreakpointManager(ILogger<BreakpointManager> logger, bool enabled = false)
    {
        _logger = logger;
        _enabled = enabled;
    }

    /// <summary>
    /// Adds a new breakpoint
    /// </summary>
    public bool AddBreakpoint(Breakpoint breakpoint)
    {
        if (!_enabled)
        {
            _logger.LogWarning("Breakpoint manager is disabled. Cannot add breakpoint: {BreakpointId}", breakpoint.Id);
            return false;
        }

        var added = _breakpoints.TryAdd(breakpoint.Id, breakpoint);
        if (added)
        {
            _logger.LogInformation("Breakpoint added: {BreakpointId} - {BreakpointName}", breakpoint.Id, breakpoint.Name);
        }
        return added;
    }

    /// <summary>
    /// Removes a breakpoint
    /// </summary>
    public bool RemoveBreakpoint(string breakpointId)
    {
        var removed = _breakpoints.TryRemove(breakpointId, out _);
        if (removed)
        {
            _logger.LogInformation("Breakpoint removed: {BreakpointId}", breakpointId);
        }
        return removed;
    }

    /// <summary>
    /// Gets a breakpoint by ID
    /// </summary>
    public Breakpoint? GetBreakpoint(string breakpointId)
    {
        _breakpoints.TryGetValue(breakpointId, out var breakpoint);
        return breakpoint;
    }

    /// <summary>
    /// Gets all breakpoints
    /// </summary>
    public IReadOnlyList<Breakpoint> GetAllBreakpoints()
    {
        return _breakpoints.Values.ToList();
    }

    /// <summary>
    /// Enables or disables a breakpoint
    /// </summary>
    public bool ToggleBreakpoint(string breakpointId, bool enabled)
    {
        if (_breakpoints.TryGetValue(breakpointId, out var breakpoint))
        {
            breakpoint.Enabled = enabled;
            _logger.LogInformation("Breakpoint {BreakpointId} {Action}", breakpointId, enabled ? "enabled" : "disabled");
            return true;
        }
        return false;
    }

    /// <summary>
    /// Checks if a message should trigger a breakpoint.
    /// Returns null if no break, otherwise returns a Task that completes when user continues.
    /// </summary>
    public async Task<DebugAction> CheckBreakpointAsync(
        object message,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        // Fast path: if disabled or no breakpoints, return immediately
        if (!_enabled || _breakpoints.IsEmpty)
            return DebugAction.Continue;

        // Check all enabled breakpoints
        foreach (var breakpoint in _breakpoints.Values)
        {
            if (breakpoint.ShouldTrigger(message))
            {
                breakpoint.RecordHit(correlationId);

                _logger.LogInformation(
                    "Breakpoint hit: {BreakpointId} - {BreakpointName} (Correlation: {CorrelationId})",
                    breakpoint.Id, breakpoint.Name, correlationId);

                // Notify listeners (UI, etc.)
                var eventArgs = new BreakpointHitEventArgs(
                    breakpoint,
                    message,
                    correlationId,
                    DateTime.UtcNow
                );
                BreakpointHit?.Invoke(eventArgs);

                // Wait for user action (Continue, StepOver, etc.)
                return await WaitForDebugActionAsync(correlationId, cancellationToken);
            }
        }

        return DebugAction.Continue;
    }

    /// <summary>
    /// Waits for the user to decide what to do (Continue, Step, etc.)
    /// </summary>
    private async Task<DebugAction> WaitForDebugActionAsync(string correlationId, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<DebugAction>();
        _pendingBreaks[correlationId] = tcs;

        try
        {
            // Wait for user action with cancellation support
            using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            return await tcs.Task;
        }
        finally
        {
            _pendingBreaks.TryRemove(correlationId, out _);
        }
    }

    /// <summary>
    /// Continues execution from a breakpoint
    /// </summary>
    public bool Continue(string correlationId, DebugAction action = DebugAction.Continue)
    {
        if (_pendingBreaks.TryRemove(correlationId, out var tcs))
        {
            tcs.TrySetResult(action);
            _logger.LogInformation("Execution continued for {CorrelationId} with action {Action}", correlationId, action);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Clears all breakpoints
    /// </summary>
    public void ClearAll()
    {
        _breakpoints.Clear();
        _logger.LogInformation("All breakpoints cleared");
    }

    public void Dispose()
    {
        // Cancel all pending breaks
        foreach (var kvp in _pendingBreaks)
        {
            kvp.Value.TrySetCanceled();
        }
        _pendingBreaks.Clear();
        _breakpoints.Clear();
    }
}

/// <summary>
/// Debug action to take when a breakpoint is hit
/// </summary>
public enum DebugAction
{
    /// <summary>Continue normal execution</summary>
    Continue,

    /// <summary>Step to the next message/event</summary>
    StepOver,

    /// <summary>Step into event handlers</summary>
    StepInto,

    /// <summary>Step out of current handler</summary>
    StepOut
}

/// <summary>
/// Event args for breakpoint hit events
/// </summary>
public sealed class BreakpointHitEventArgs
{
    public Breakpoint Breakpoint { get; }
    public object Message { get; }
    public string CorrelationId { get; }
    public DateTime Timestamp { get; }

    public BreakpointHitEventArgs(Breakpoint breakpoint, object message, string correlationId, DateTime timestamp)
    {
        Breakpoint = breakpoint;
        Message = message;
        CorrelationId = correlationId;
        Timestamp = timestamp;
    }
}

