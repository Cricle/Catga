using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Catga.Debugger.Core;

namespace Catga.Debugger.Watch;

/// <summary>
/// Represents a watch expression that can be evaluated against a capture context.
/// </summary>
public sealed class WatchExpression
{
    /// <summary>
    /// Unique identifier
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// The expression as a string (for display)
    /// </summary>
    public string Expression { get; }

    /// <summary>
    /// Description of what this expression watches
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The compiled evaluator function
    /// </summary>
    private readonly Func<CaptureContext, object?> _evaluator;

    /// <summary>
    /// History of evaluated values
    /// </summary>
    private readonly List<WatchValue> _history = new();

    /// <summary>
    /// Maximum number of history entries to keep
    /// </summary>
    public int MaxHistorySize { get; set; } = 100;

    /// <summary>
    /// Current value
    /// </summary>
    public WatchValue? CurrentValue => _history.Count > 0 ? _history[^1] : null;

    /// <summary>
    /// All historical values
    /// </summary>
    public IReadOnlyList<WatchValue> History => _history;

    public WatchExpression(string id, string expression, Func<CaptureContext, object?> evaluator)
    {
        Id = id;
        Expression = expression;
        _evaluator = evaluator;
    }

    /// <summary>
    /// Evaluates the expression against a context
    /// </summary>
    public WatchValue Evaluate(CaptureContext context)
    {
        try
        {
            var value = _evaluator(context);
            var watchValue = new WatchValue(
                value,
                DateTime.UtcNow,
                context.CorrelationId,
                success: true,
                error: null
            );

            AddToHistory(watchValue);
            return watchValue;
        }
        catch (Exception ex)
        {
            var watchValue = new WatchValue(
                null,
                DateTime.UtcNow,
                context.CorrelationId,
                success: false,
                error: ex.Message
            );

            AddToHistory(watchValue);
            return watchValue;
        }
    }

    private void AddToHistory(WatchValue value)
    {
        _history.Add(value);

        // Keep history size limited
        while (_history.Count > MaxHistorySize)
        {
            _history.RemoveAt(0);
        }
    }

    /// <summary>
    /// Clears the history
    /// </summary>
    public void ClearHistory()
    {
        _history.Clear();
    }

    /// <summary>
    /// Creates a simple property watch expression from a lambda
    /// Note: CaptureContext currently only has Events, so watches should access event data
    /// </summary>
    public static WatchExpression FromLambda<TValue>(
        string id,
        string expression,
        Func<CaptureContext, TValue> evaluator)
    {
        return new WatchExpression(
            id,
            expression,
            ctx => evaluator(ctx)
        );
    }

}

/// <summary>
/// Represents a watched value at a specific point in time
/// </summary>
public sealed class WatchValue
{
    /// <summary>
    /// The evaluated value (null if evaluation failed)
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// When this value was captured
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// The correlation ID of the message being processed
    /// </summary>
    public string CorrelationId { get; }

    /// <summary>
    /// Whether evaluation was successful
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Error message if evaluation failed
    /// </summary>
    public string? Error { get; }

    /// <summary>
    /// The type of the value
    /// </summary>
    public Type? ValueType => Value?.GetType();

    public WatchValue(object? value, DateTime timestamp, string correlationId, bool success, string? error)
    {
        Value = value;
        Timestamp = timestamp;
        CorrelationId = correlationId;
        Success = success;
        Error = error;
    }

    /// <summary>
    /// Gets the string representation of the value
    /// </summary>
    public string GetDisplayValue()
    {
        if (!Success)
            return $"Error: {Error}";

        if (Value == null)
            return "null";

        return Value.ToString() ?? "<no string representation>";
    }
}

