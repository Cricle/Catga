using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Catga.Debugger.Core;
using Microsoft.Extensions.Logging;

namespace Catga.Debugger.Watch;

/// <summary>
/// Manages watch expressions for debugging.
/// Thread-safe and production-safe (zero overhead when disabled).
/// </summary>
public sealed class WatchManager
{
    private readonly ILogger<WatchManager> _logger;
    private readonly ConcurrentDictionary<string, WatchExpression> _watches = new();
    private readonly bool _enabled;

    /// <summary>
    /// Event raised when a watch expression is evaluated
    /// </summary>
    public event Action<WatchEvaluatedEventArgs>? WatchEvaluated;

    public WatchManager(ILogger<WatchManager> logger, bool enabled = false)
    {
        _logger = logger;
        _enabled = enabled;
    }

    /// <summary>
    /// Adds a watch expression
    /// </summary>
    public bool AddWatch(WatchExpression watch)
    {
        if (!_enabled)
        {
            _logger.LogWarning("Watch manager is disabled. Cannot add watch: {WatchId}", watch.Id);
            return false;
        }

        var added = _watches.TryAdd(watch.Id, watch);
        if (added)
        {
            _logger.LogInformation("Watch added: {WatchId} - {Expression}", watch.Id, watch.Expression);
        }
        return added;
    }

    /// <summary>
    /// Removes a watch expression
    /// </summary>
    public bool RemoveWatch(string watchId)
    {
        var removed = _watches.TryRemove(watchId, out _);
        if (removed)
        {
            _logger.LogInformation("Watch removed: {WatchId}", watchId);
        }
        return removed;
    }

    /// <summary>
    /// Gets a watch expression by ID
    /// </summary>
    public WatchExpression? GetWatch(string watchId)
    {
        _watches.TryGetValue(watchId, out var watch);
        return watch;
    }

    /// <summary>
    /// Gets all watch expressions
    /// </summary>
    public IReadOnlyList<WatchExpression> GetAllWatches()
    {
        return _watches.Values.ToList();
    }

    /// <summary>
    /// Evaluates all watches against a context
    /// </summary>
    public Dictionary<string, WatchValue> EvaluateAll(CaptureContext context)
    {
        // Fast path: if disabled or no watches, return empty
        if (!_enabled || _watches.IsEmpty)
            return new Dictionary<string, WatchValue>();

        var results = new Dictionary<string, WatchValue>();

        foreach (var kvp in _watches)
        {
            try
            {
                var value = kvp.Value.Evaluate(context);
                results[kvp.Key] = value;

                // Notify listeners
                WatchEvaluated?.Invoke(new WatchEvaluatedEventArgs(
                    kvp.Value,
                    value,
                    context.CorrelationId
                ));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to evaluate watch: {WatchId}", kvp.Key);
            }
        }

        return results;
    }

    /// <summary>
    /// Evaluates a single watch expression
    /// </summary>
    public WatchValue? EvaluateSingle(string watchId, CaptureContext context)
    {
        if (!_enabled)
            return null;

        if (_watches.TryGetValue(watchId, out var watch))
        {
            return watch.Evaluate(context);
        }

        return null;
    }

    /// <summary>
    /// Clears all watches
    /// </summary>
    public void ClearAll()
    {
        _watches.Clear();
        _logger.LogInformation("All watches cleared");
    }

    /// <summary>
    /// Clears history for all watches
    /// </summary>
    public void ClearAllHistory()
    {
        foreach (var watch in _watches.Values)
        {
            watch.ClearHistory();
        }
        _logger.LogInformation("Watch history cleared for all watches");
    }
}

/// <summary>
/// Event args for watch evaluated events
/// </summary>
public sealed class WatchEvaluatedEventArgs
{
    public WatchExpression Watch { get; }
    public WatchValue Value { get; }
    public string CorrelationId { get; }

    public WatchEvaluatedEventArgs(WatchExpression watch, WatchValue value, string correlationId)
    {
        Watch = watch;
        Value = value;
        CorrelationId = correlationId;
    }
}

