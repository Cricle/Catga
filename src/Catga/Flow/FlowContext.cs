using System.Collections.Concurrent;
using Catga.Abstractions;

namespace Catga.Flow.Dsl;

/// <summary>
/// Flow execution context providing access to state, variables, and execution information.
/// </summary>
public class FlowContext<TState> where TState : class, IFlowState
{
    private readonly ConcurrentDictionary<string, object?> _variables;

    /// <summary>
    /// Current flow state.
    /// </summary>
    public TState State { get; }

    /// <summary>
    /// Current step index in the flow.
    /// </summary>
    public int CurrentStepIndex { get; }

    /// <summary>
    /// Current position in nested flows.
    /// </summary>
    public FlowPosition Position { get; }

    /// <summary>
    /// Cancellation token for the flow execution.
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Flow execution start time.
    /// </summary>
    public DateTime StartTime { get; }

    /// <summary>
    /// Flow execution elapsed time.
    /// </summary>
    public TimeSpan ElapsedTime => DateTime.UtcNow - StartTime;

    /// <summary>
    /// Number of steps executed so far.
    /// </summary>
    public int StepsExecuted { get; internal set; }

    /// <summary>
    /// Number of steps failed so far.
    /// </summary>
    public int StepsFailed { get; internal set; }

    /// <summary>
    /// Flow execution metadata.
    /// </summary>
    public Dictionary<string, object?> Metadata { get; }

    /// <summary>
    /// Initialize a new flow context.
    /// </summary>
    public FlowContext(
        TState state,
        int currentStepIndex,
        FlowPosition position,
        CancellationToken cancellationToken)
    {
        State = state;
        CurrentStepIndex = currentStepIndex;
        Position = position;
        CancellationToken = cancellationToken;
        StartTime = DateTime.UtcNow;
        _variables = new ConcurrentDictionary<string, object?>();
        Metadata = new Dictionary<string, object?>();
    }

    /// <summary>
    /// Get a variable value with type safety.
    /// </summary>
    public T GetVar<T>(string name)
    {
        if (_variables.TryGetValue(name, out var value))
        {
            if (value is T typedValue)
            {
                return typedValue;
            }
            throw new InvalidOperationException($"Variable '{name}' is not of type {typeof(T).Name}");
        }
        throw new KeyNotFoundException($"Variable '{name}' not found");
    }

    /// <summary>
    /// Get a variable value with default if not found.
    /// </summary>
    public T GetVar<T>(string name, T defaultValue)
    {
        if (_variables.TryGetValue(name, out var value))
        {
            if (value is T typedValue)
            {
                return typedValue;
            }
            return defaultValue;
        }
        return defaultValue;
    }

    /// <summary>
    /// Try to get a variable value.
    /// </summary>
    public bool TryGetVar<T>(string name, out T? value)
    {
        value = default;
        if (_variables.TryGetValue(name, out var objValue))
        {
            if (objValue is T typedValue)
            {
                value = typedValue;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Set a variable value.
    /// </summary>
    public void SetVar<T>(string name, T value)
    {
        _variables[name] = value;
    }

    /// <summary>
    /// Remove a variable.
    /// </summary>
    public bool RemoveVar(string name)
    {
        return _variables.TryRemove(name, out _);
    }

    /// <summary>
    /// Check if a variable exists.
    /// </summary>
    public bool HasVar(string name)
    {
        return _variables.ContainsKey(name);
    }

    /// <summary>
    /// Get all variable names.
    /// </summary>
    public IEnumerable<string> GetVarNames()
    {
        return _variables.Keys;
    }

    /// <summary>
    /// Clear all variables.
    /// </summary>
    public void ClearVars()
    {
        _variables.Clear();
    }

    /// <summary>
    /// Get variable count.
    /// </summary>
    public int VarCount => _variables.Count;
}

/// <summary>
/// Flow variable builder interface.
/// </summary>
public interface IVariableFlowBuilder<TState> where TState : class, IFlowState
{
    /// <summary>
    /// Define a variable with initial value.
    /// </summary>
    IVariableFlowBuilder<TState> Var<TValue>(
        string name,
        System.Linq.Expressions.Expression<Func<TState, TValue>> initializer);

    /// <summary>
    /// Define a variable with constant value.
    /// </summary>
    IVariableFlowBuilder<TState> Var<TValue>(string name, TValue value);

    /// <summary>
    /// Update a variable value.
    /// </summary>
    IVariableFlowBuilder<TState> SetVar<TValue>(
        string name,
        System.Linq.Expressions.Expression<Func<TState, TValue>> valueExpression);

    /// <summary>
    /// Use a variable in flow steps.
    /// </summary>
    IVariableFlowBuilder<TState> UseVar<TValue>(
        string name,
        Action<IFlowBuilder<TState>, TValue> configure);

    /// <summary>
    /// Increment a numeric variable.
    /// </summary>
    IVariableFlowBuilder<TState> IncrementVar(string name);

    /// <summary>
    /// Decrement a numeric variable.
    /// </summary>
    IVariableFlowBuilder<TState> DecrementVar(string name);

    /// <summary>
    /// Append to a collection variable.
    /// </summary>
    IVariableFlowBuilder<TState> AppendVar<TItem>(
        string name,
        System.Linq.Expressions.Expression<Func<TState, TItem>> itemExpression);

    /// <summary>
    /// End variable flow building.
    /// </summary>
    IFlowBuilder<TState> End();
}

/// <summary>
/// Variable definition information.
/// </summary>
internal class VariableDefinition<TState> where TState : class, IFlowState
{
    public string Name { get; set; } = string.Empty;
    public Type ValueType { get; set; } = typeof(object);
    public object? InitialValue { get; set; }
    public System.Linq.Expressions.Expression? Initializer { get; set; }
    public System.Linq.Expressions.Expression? UpdateExpression { get; set; }
    public Delegate? ConfigureAction { get; set; }
    public VariableOperationType OperationType { get; set; }
}

/// <summary>
/// Variable operation type enumeration.
/// </summary>
internal enum VariableOperationType
{
    Define,
    Update,
    Use,
    Increment,
    Decrement,
    Append
}

/// <summary>
/// Implementation of variable flow builder.
/// </summary>
internal class VariableFlowBuilder<TState> : IVariableFlowBuilder<TState> where TState : class, IFlowState
{
    private readonly FlowBuilder<TState> _flowBuilder;
    private readonly List<VariableDefinition<TState>> _variables = new();

    public VariableFlowBuilder(FlowBuilder<TState> flowBuilder)
    {
        _flowBuilder = flowBuilder;
    }

    public IVariableFlowBuilder<TState> Var<TValue>(
        string name,
        System.Linq.Expressions.Expression<Func<TState, TValue>> initializer)
    {
        _variables.Add(new VariableDefinition<TState>
        {
            Name = name,
            ValueType = typeof(TValue),
            Initializer = initializer,
            OperationType = VariableOperationType.Define
        });
        return this;
    }

    public IVariableFlowBuilder<TState> Var<TValue>(string name, TValue value)
    {
        _variables.Add(new VariableDefinition<TState>
        {
            Name = name,
            ValueType = typeof(TValue),
            InitialValue = value,
            OperationType = VariableOperationType.Define
        });
        return this;
    }

    public IVariableFlowBuilder<TState> SetVar<TValue>(
        string name,
        System.Linq.Expressions.Expression<Func<TState, TValue>> valueExpression)
    {
        _variables.Add(new VariableDefinition<TState>
        {
            Name = name,
            ValueType = typeof(TValue),
            UpdateExpression = valueExpression,
            OperationType = VariableOperationType.Update
        });
        return this;
    }

    public IVariableFlowBuilder<TState> UseVar<TValue>(
        string name,
        Action<IFlowBuilder<TState>, TValue> configure)
    {
        _variables.Add(new VariableDefinition<TState>
        {
            Name = name,
            ValueType = typeof(TValue),
            ConfigureAction = configure,
            OperationType = VariableOperationType.Use
        });
        return this;
    }

    public IVariableFlowBuilder<TState> IncrementVar(string name)
    {
        _variables.Add(new VariableDefinition<TState>
        {
            Name = name,
            OperationType = VariableOperationType.Increment
        });
        return this;
    }

    public IVariableFlowBuilder<TState> DecrementVar(string name)
    {
        _variables.Add(new VariableDefinition<TState>
        {
            Name = name,
            OperationType = VariableOperationType.Decrement
        });
        return this;
    }

    public IVariableFlowBuilder<TState> AppendVar<TItem>(
        string name,
        System.Linq.Expressions.Expression<Func<TState, TItem>> itemExpression)
    {
        _variables.Add(new VariableDefinition<TState>
        {
            Name = name,
            ValueType = typeof(TItem),
            UpdateExpression = itemExpression,
            OperationType = VariableOperationType.Append
        });
        return this;
    }

    public IFlowBuilder<TState> End()
    {
        // Apply all variable definitions to the flow builder
        foreach (var varDef in _variables)
        {
            ApplyVariableDefinition(varDef);
        }
        return _flowBuilder;
    }

    private void ApplyVariableDefinition(VariableDefinition<TState> varDef)
    {
        // Variable handling would be implemented in executor
    }
}

/// <summary>
/// Extension methods for variable flow building.
/// </summary>
public static class VariableFlowExtensions
{
    /// <summary>
    /// Start building with variables.
    /// </summary>
    public static IVariableFlowBuilder<TState> Variables<TState>(this IFlowBuilder<TState> builder)
        where TState : class, IFlowState
    {
        if (builder is FlowBuilder<TState> flowBuilder)
        {
            return new VariableFlowBuilder<TState>(flowBuilder);
        }
        throw new InvalidOperationException("Invalid flow builder type");
    }
}
