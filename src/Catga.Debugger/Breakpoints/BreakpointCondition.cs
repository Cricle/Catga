using System;
using System.Linq.Expressions;

namespace Catga.Debugger.Breakpoints;

/// <summary>
/// Represents a breakpoint condition that can be evaluated against a message.
/// </summary>
public sealed class BreakpointCondition
{
    /// <summary>
    /// Unique identifier for the condition
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Human-readable description of the condition
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// The condition expression as a string (for display)
    /// </summary>
    public string Expression { get; }

    /// <summary>
    /// The compiled predicate to evaluate
    /// </summary>
    private readonly Func<object, bool> _predicate;

    public BreakpointCondition(string id, string description, string expression, Func<object, bool> predicate)
    {
        Id = id;
        Description = description;
        Expression = expression;
        _predicate = predicate;
    }

    /// <summary>
    /// Evaluates the condition against a message
    /// </summary>
    public bool Evaluate(object message)
    {
        try
        {
            return _predicate(message);
        }
        catch
        {
            // If evaluation fails, don't break
            return false;
        }
    }

    /// <summary>
    /// Creates a condition that always breaks
    /// </summary>
    public static BreakpointCondition Always(string id, string description)
    {
        return new BreakpointCondition(id, description, "true", _ => true);
    }

    /// <summary>
    /// Creates a condition based on message type
    /// </summary>
    public static BreakpointCondition MessageType(string id, string messageType)
    {
        return new BreakpointCondition(
            id,
            $"Message type is {messageType}",
            $"messageType == \"{messageType}\"",
            msg => msg.GetType().Name == messageType
        );
    }

    /// <summary>
    /// Creates a condition based on a property value
    /// </summary>
    public static BreakpointCondition PropertyEquals<TMessage, TValue>(
        string id,
        string propertyName,
        TValue expectedValue,
        Expression<Func<TMessage, TValue>> propertySelector)
    {
        var compiled = propertySelector.Compile();
        return new BreakpointCondition(
            id,
            $"{propertyName} == {expectedValue}",
            $"{propertyName} == {expectedValue}",
            msg =>
            {
                if (msg is TMessage typedMsg)
                {
                    var actualValue = compiled(typedMsg);
                    return EqualityComparer<TValue>.Default.Equals(actualValue, expectedValue);
                }
                return false;
            }
        );
    }
}

