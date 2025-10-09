namespace Catga.Saga;

/// <summary>
/// Builder for creating sagas
/// </summary>
public sealed class SagaBuilder
{
    private readonly string _sagaId;
    private readonly List<ISagaStep> _steps = new();

    private SagaBuilder(string sagaId)
    {
        _sagaId = sagaId;
    }

    /// <summary>
    /// Create a new saga builder
    /// </summary>
    public static SagaBuilder Create(string? sagaId = null)
    {
        return new SagaBuilder(sagaId ?? Guid.NewGuid().ToString());
    }

    /// <summary>
    /// Add a step to the saga
    /// </summary>
    public SagaBuilder AddStep(
        string name,
        Func<CancellationToken, ValueTask> execute,
        Func<CancellationToken, ValueTask> compensate)
    {
        _steps.Add(new FuncSagaStep(name, execute, compensate));
        return this;
    }

    /// <summary>
    /// Add a step with data to the saga
    /// </summary>
    public SagaBuilder AddStep<TData>(
        string name,
        Func<TData, CancellationToken, ValueTask<TData>> execute,
        Func<TData, CancellationToken, ValueTask> compensate,
        TData initialData)
    {
        _steps.Add(new FuncSagaStep<TData>(name, execute, compensate, initialData));
        return this;
    }

    /// <summary>
    /// Add a step to the saga
    /// </summary>
    public SagaBuilder AddStep(ISagaStep step)
    {
        _steps.Add(step);
        return this;
    }

    /// <summary>
    /// Build the saga
    /// </summary>
    public ISaga Build()
    {
        return new Saga(_sagaId, _steps.AsReadOnly());
    }
}

/// <summary>
/// Saga implementation
/// </summary>
internal sealed class Saga : ISaga
{
    public string SagaId { get; }
    public IReadOnlyList<ISagaStep> Steps { get; }

    public Saga(string sagaId, IReadOnlyList<ISagaStep> steps)
    {
        SagaId = sagaId;
        Steps = steps;
    }
}

/// <summary>
/// Saga step using functions
/// </summary>
internal sealed class FuncSagaStep : ISagaStep
{
    private readonly Func<CancellationToken, ValueTask> _execute;
    private readonly Func<CancellationToken, ValueTask> _compensate;

    public string Name { get; }

    public FuncSagaStep(
        string name,
        Func<CancellationToken, ValueTask> execute,
        Func<CancellationToken, ValueTask> compensate)
    {
        Name = name;
        _execute = execute;
        _compensate = compensate;
    }

    public ValueTask ExecuteAsync(CancellationToken cancellationToken = default)
    {
        return _execute(cancellationToken);
    }

    public ValueTask CompensateAsync(CancellationToken cancellationToken = default)
    {
        return _compensate(cancellationToken);
    }
}

/// <summary>
/// Saga step using functions with data
/// </summary>
internal sealed class FuncSagaStep<TData> : ISagaStep<TData>
{
    private readonly Func<TData, CancellationToken, ValueTask<TData>> _execute;
    private readonly Func<TData, CancellationToken, ValueTask> _compensate;
    private TData _data;

    public string Name { get; }

    public FuncSagaStep(
        string name,
        Func<TData, CancellationToken, ValueTask<TData>> execute,
        Func<TData, CancellationToken, ValueTask> compensate,
        TData initialData)
    {
        Name = name;
        _execute = execute;
        _compensate = compensate;
        _data = initialData;
    }

    public async ValueTask ExecuteAsync(CancellationToken cancellationToken = default)
    {
        _data = await _execute(_data, cancellationToken);
    }

    public async ValueTask<TData> ExecuteAsync(TData data, CancellationToken cancellationToken = default)
    {
        _data = await _execute(data, cancellationToken);
        return _data;
    }

    public ValueTask CompensateAsync(CancellationToken cancellationToken = default)
    {
        return _compensate(_data, cancellationToken);
    }

    public ValueTask CompensateAsync(TData data, CancellationToken cancellationToken = default)
    {
        return _compensate(data, cancellationToken);
    }
}

