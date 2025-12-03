using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Core;
using Microsoft.Extensions.Logging;

namespace Catga.Flow;

/// <summary>
/// Flow execution context that automatically manages compensation on failure.
/// Uses AsyncLocal for implicit propagation - user code doesn't need to pass it around.
/// </summary>
public sealed class FlowContext : IAsyncDisposable
{
    private static readonly AsyncLocal<FlowContext?> _current = new();

    private readonly ICatgaMediator _mediator;
    private readonly ILogger? _logger;
    private readonly Stack<CompensationRecord> _compensations = new();
    private readonly string _flowName;
    private readonly long _correlationId;
    private readonly DateTime _startedAt;
    private readonly Activity? _activity;

    private bool _committed;
    private bool _disposed;
    private int _stepIndex;

    /// <summary>
    /// Gets the current flow context, or null if not in a flow.
    /// </summary>
    public static FlowContext? Current => _current.Value;

    /// <summary>
    /// Whether currently executing within a flow context.
    /// </summary>
    public static bool IsInFlow => _current.Value != null;

    /// <summary>
    /// Flow name for tracing.
    /// </summary>
    public string FlowName => _flowName;

    /// <summary>
    /// Correlation ID for tracing.
    /// </summary>
    public long CorrelationId => _correlationId;

    /// <summary>
    /// Number of steps executed.
    /// </summary>
    public int StepCount => _stepIndex;

    /// <summary>
    /// Whether the flow has been committed (success).
    /// </summary>
    public bool IsCommitted => _committed;

    /// <summary>
    /// Creates a new flow context.
    /// </summary>
    internal FlowContext(
        ICatgaMediator mediator,
        string flowName,
        long? correlationId = null,
        ILogger? logger = null)
    {
        _mediator = mediator;
        _flowName = flowName;
        _correlationId = correlationId ?? MessageExtensions.NewMessageId();
        _logger = logger;
        _startedAt = DateTime.UtcNow;

        // Start activity for tracing
        _activity = Activity.Current?.Source.StartActivity($"Flow:{flowName}");
        _activity?.SetTag("flow.name", flowName);
        _activity?.SetTag("flow.correlation_id", _correlationId);
    }

    /// <summary>
    /// Begins a new flow context. Use with 'await using'.
    /// </summary>
    internal static FlowContext Begin(
        ICatgaMediator mediator,
        string flowName,
        long? correlationId = null,
        ILogger? logger = null)
    {
        var context = new FlowContext(mediator, flowName, correlationId, logger);
        _current.Value = context;
        return context;
    }

    /// <summary>
    /// Executes a command within the flow, automatically recording compensation.
    /// </summary>
    public async ValueTask<CatgaResult<TResult>> ExecuteAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TCommand, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResult>(
        TCommand command,
        CancellationToken ct = default)
        where TCommand : IRequest<TResult>
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _stepIndex++;
        var commandTypeName = typeof(TCommand).Name;

        _activity?.AddEvent(new ActivityEvent($"Step{_stepIndex}:{commandTypeName}"));

        // Execute the command through mediator (uses existing pipeline)
        var result = await _mediator.SendAsync<TCommand, TResult>(command, ct);

        if (result.IsSuccess)
        {
            // Record compensation if command is compensatable
            if (command is ICompensatable compensatable)
            {
                var compensationCmd = compensatable.CreateCompensation(result.Value!);
                var mediator = _mediator;
                _compensations.Push(new CompensationRecord
                {
                    StepIndex = _stepIndex,
                    CommandTypeName = commandTypeName,
                    CompensationAction = async ct2 => await mediator.SendAsync(compensationCmd, ct2),
                    ExecutedAt = DateTime.UtcNow
                });

                _logger?.LogDebug(
                    "Flow {FlowName} step {Step} ({Command}) succeeded, compensation recorded",
                    _flowName, _stepIndex, commandTypeName);
            }
        }
        else
        {
            _logger?.LogWarning(
                "Flow {FlowName} step {Step} ({Command}) failed: {Error}",
                _flowName, _stepIndex, commandTypeName, result.Error);

            _activity?.SetStatus(ActivityStatusCode.Error, result.Error);

            // Don't compensate here - let DisposeAsync handle it
            // This allows the caller to decide whether to continue or abort
        }

        return result;
    }

    /// <summary>
    /// Executes a command without response within the flow.
    /// </summary>
    public async ValueTask<CatgaResult> ExecuteAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TCommand>(
        TCommand command,
        CancellationToken ct = default)
        where TCommand : IRequest
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _stepIndex++;
        var commandTypeName = typeof(TCommand).Name;

        var result = await _mediator.SendAsync(command, ct);

        if (result.IsSuccess)
        {
            // Commands without response can still have compensation
            // but we can't auto-create it without the result
            _logger?.LogDebug(
                "Flow {FlowName} step {Step} ({Command}) succeeded",
                _flowName, _stepIndex, commandTypeName);
        }
        else
        {
            _logger?.LogWarning(
                "Flow {FlowName} step {Step} ({Command}) failed: {Error}",
                _flowName, _stepIndex, commandTypeName, result.Error);

            // Don't compensate here - let DisposeAsync handle it
        }

        return result;
    }

    /// <summary>
    /// Manually registers a compensation action using a command.
    /// Use when the command doesn't implement ICompensatable.
    /// </summary>
    public void RegisterCompensation<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TCompensation>(TCompensation compensation)
        where TCompensation : IRequest
    {
        var mediator = _mediator;
        _compensations.Push(new CompensationRecord
        {
            StepIndex = _stepIndex,
            CommandTypeName = $"Manual:{typeof(TCompensation).Name}",
            CompensationAction = async ct => await mediator.SendAsync(compensation, ct),
            ExecutedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Manually registers a compensation action using a delegate.
    /// Use for inline compensation logic without creating a command.
    /// </summary>
    public void RegisterCompensation(Func<CancellationToken, Task> compensationAction, string? name = null)
    {
        _compensations.Push(new CompensationRecord
        {
            StepIndex = _stepIndex,
            CommandTypeName = name ?? $"Inline:{_stepIndex}",
            CompensationAction = compensationAction,
            ExecutedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Marks the flow as successfully completed.
    /// Compensation will not be executed on dispose.
    /// </summary>
    public void Commit()
    {
        _committed = true;
        _activity?.SetStatus(ActivityStatusCode.Ok);

        _logger?.LogInformation(
            "Flow {FlowName} committed successfully after {Steps} steps in {Duration}ms",
            _flowName, _stepIndex, (DateTime.UtcNow - _startedAt).TotalMilliseconds);
    }

    /// <summary>
    /// Executes all recorded compensations in reverse order.
    /// </summary>
    private async ValueTask CompensateAsync(CancellationToken ct = default)
    {
        if (_compensations.Count == 0)
            return;

        _logger?.LogWarning(
            "Flow {FlowName} compensating {Count} steps",
            _flowName, _compensations.Count);

        using var compensationActivity = _activity?.Source.StartActivity("Compensation");

        while (_compensations.TryPop(out var record))
        {
            try
            {
                compensationActivity?.AddEvent(new ActivityEvent($"Compensate:{record.CommandTypeName}"));

                // Execute the compensation action
                await record.CompensationAction(ct);

                _logger?.LogDebug(
                    "Flow {FlowName} compensated step {Step} ({Command})",
                    _flowName, record.StepIndex, record.CommandTypeName);
            }
            catch (Exception ex)
            {
                // Log but continue compensating other steps
                _logger?.LogError(ex,
                    "Flow {FlowName} compensation failed for step {Step} ({Command})",
                    _flowName, record.StepIndex, record.CommandTypeName);
            }
        }
    }

    /// <summary>
    /// Disposes the flow context. Executes compensation if not committed.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Execute compensation if not committed
        if (!_committed && _compensations.Count > 0)
        {
            await CompensateAsync();
        }

        // Clear AsyncLocal
        if (_current.Value == this)
        {
            _current.Value = null;
        }

        _activity?.Dispose();
    }
}

/// <summary>
/// Flow execution result.
/// </summary>
public readonly record struct FlowResult<T>
{
    public bool IsSuccess { get; init; }
    public T? Value { get; init; }
    public string? Error { get; init; }
    public int FailedAtStep { get; init; }
    public TimeSpan Duration { get; init; }

    public static FlowResult<T> Success(T value, TimeSpan duration) => new()
    {
        IsSuccess = true,
        Value = value,
        Duration = duration
    };

    public static FlowResult<T> Failure(string error, int step, TimeSpan duration) => new()
    {
        IsSuccess = false,
        Error = error,
        FailedAtStep = step,
        Duration = duration
    };
}
