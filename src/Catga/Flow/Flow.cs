using System.Diagnostics;
using Catga.Abstractions;
using Catga.Core;

namespace Catga.Flow;

/// <summary>
/// Simple, fluent Flow orchestration for Saga pattern.
/// Automatically compensates on failure in reverse order.
/// </summary>
/// <example>
/// var result = await Flow.Create("CreateOrder", mediator)
///     .Step("CreateOrder",
///         () => mediator.SendAsync&lt;CreateOrderCmd, Order&gt;(cmd),
///         order => mediator.SendAsync(new CancelOrderCmd(order.Id)))
///     .Step("ReserveStock",
///         () => inventoryService.ReserveAsync(items),
///         () => inventoryService.ReleaseAsync(items))
///     .Step("ProcessPayment",
///         () => paymentService.ChargeAsync(amount),
///         () => paymentService.RefundAsync(amount))
///     .ExecuteAsync();
/// </example>
public sealed class Flow
{
    private readonly string _name;
    private readonly ICatgaMediator? _mediator;
    private readonly List<FlowStep> _steps = [];
    private readonly Activity? _activity;

    private Flow(string name, ICatgaMediator? mediator = null)
    {
        _name = name;
        _mediator = mediator;
        _activity = Activity.Current?.Source.StartActivity($"Flow:{name}");
        _activity?.SetTag("flow.name", name);
    }

    /// <summary>
    /// Creates a new Flow.
    /// </summary>
    public static Flow Create(string name, ICatgaMediator? mediator = null) => new(name, mediator);

    /// <summary>
    /// Adds a step with typed result and compensation.
    /// </summary>
    public Flow Step<T>(
        string name,
        Func<Task<T>> execute,
        Func<T, Task>? compensate = null)
    {
        _steps.Add(new FlowStep
        {
            Name = name,
            Execute = async () =>
            {
                var result = await execute();
                return result;
            },
            Compensate = compensate != null
                ? async (result) => await compensate((T)result!)
                : null
        });
        return this;
    }

    /// <summary>
    /// Adds a step with CatgaResult and compensation.
    /// </summary>
    public Flow Step<T>(
        string name,
        Func<Task<CatgaResult<T>>> execute,
        Func<T, Task>? compensate = null)
    {
        _steps.Add(new FlowStep
        {
            Name = name,
            Execute = async () =>
            {
                var result = await execute();
                if (!result.IsSuccess)
                    throw new FlowStepException(name, result.Error ?? "Step failed");
                return result.Value;
            },
            Compensate = compensate != null
                ? async (result) => await compensate((T)result!)
                : null
        });
        return this;
    }

    /// <summary>
    /// Adds a step without result (void action).
    /// </summary>
    public Flow Step(
        string name,
        Func<Task> execute,
        Func<Task>? compensate = null)
    {
        _steps.Add(new FlowStep
        {
            Name = name,
            Execute = async () =>
            {
                await execute();
                return null;
            },
            Compensate = compensate != null
                ? async (_) => await compensate()
                : null
        });
        return this;
    }

    /// <summary>
    /// Adds a step with CatgaResult (no value).
    /// </summary>
    public Flow Step(
        string name,
        Func<Task<CatgaResult>> execute,
        Func<Task>? compensate = null)
    {
        _steps.Add(new FlowStep
        {
            Name = name,
            Execute = async () =>
            {
                var result = await execute();
                if (!result.IsSuccess)
                    throw new FlowStepException(name, result.Error ?? "Step failed");
                return null;
            },
            Compensate = compensate != null
                ? async (_) => await compensate()
                : null
        });
        return this;
    }

    /// <summary>
    /// Executes the flow. On failure, compensates in reverse order.
    /// </summary>
    public async Task<FlowResult> ExecuteAsync(CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        var completedSteps = new Stack<(FlowStep Step, object? Result)>();

        try
        {
            foreach (var step in _steps)
            {
                ct.ThrowIfCancellationRequested();

                _activity?.AddEvent(new ActivityEvent($"Step:{step.Name}"));

                var result = await step.Execute();
                completedSteps.Push((step, result));
            }

            _activity?.SetStatus(ActivityStatusCode.Ok);
            return FlowResult.Ok(_name, _steps.Count, DateTime.UtcNow - startTime);
        }
        catch (FlowStepException ex)
        {
            _activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            await CompensateAsync(completedSteps, ct);
            return FlowResult.Failed(_name, ex.StepName, ex.Message, completedSteps.Count, DateTime.UtcNow - startTime);
        }
        catch (OperationCanceledException)
        {
            _activity?.SetStatus(ActivityStatusCode.Error, "Cancelled");
            await CompensateAsync(completedSteps, ct);
            return FlowResult.Cancelled(_name, completedSteps.Count, DateTime.UtcNow - startTime);
        }
        catch (Exception ex)
        {
            _activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            await CompensateAsync(completedSteps, ct);
            var failedStep = _steps.Count > completedSteps.Count ? _steps[completedSteps.Count].Name : "Unknown";
            return FlowResult.Failed(_name, failedStep, ex.Message, completedSteps.Count, DateTime.UtcNow - startTime);
        }
        finally
        {
            _activity?.Dispose();
        }
    }

    /// <summary>
    /// Executes the flow and returns a typed result from the last step.
    /// </summary>
    public async Task<FlowResult<T>> ExecuteAsync<T>(CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        var completedSteps = new Stack<(FlowStep Step, object? Result)>();
        object? lastResult = default;

        try
        {
            foreach (var step in _steps)
            {
                ct.ThrowIfCancellationRequested();

                _activity?.AddEvent(new ActivityEvent($"Step:{step.Name}"));

                lastResult = await step.Execute();
                completedSteps.Push((step, lastResult));
            }

            _activity?.SetStatus(ActivityStatusCode.Ok);
            return FlowResult<T>.Ok((T)lastResult!, _name, _steps.Count, DateTime.UtcNow - startTime);
        }
        catch (FlowStepException ex)
        {
            _activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            await CompensateAsync(completedSteps, ct);
            return FlowResult<T>.Failed(_name, ex.StepName, ex.Message, completedSteps.Count, DateTime.UtcNow - startTime);
        }
        catch (OperationCanceledException)
        {
            _activity?.SetStatus(ActivityStatusCode.Error, "Cancelled");
            await CompensateAsync(completedSteps, ct);
            return FlowResult<T>.Cancelled(_name, completedSteps.Count, DateTime.UtcNow - startTime);
        }
        catch (Exception ex)
        {
            _activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            await CompensateAsync(completedSteps, ct);
            var failedStep = _steps.Count > completedSteps.Count ? _steps[completedSteps.Count].Name : "Unknown";
            return FlowResult<T>.Failed(_name, failedStep, ex.Message, completedSteps.Count, DateTime.UtcNow - startTime);
        }
        finally
        {
            _activity?.Dispose();
        }
    }

    private static async Task CompensateAsync(
        Stack<(FlowStep Step, object? Result)> completedSteps,
        CancellationToken ct)
    {
        while (completedSteps.TryPop(out var item))
        {
            if (item.Step.Compensate == null) continue;

            try
            {
                await item.Step.Compensate(item.Result);
            }
            catch
            {
                // Log but continue compensating
            }
        }
    }

    private sealed class FlowStep
    {
        public required string Name { get; init; }
        public required Func<Task<object?>> Execute { get; init; }
        public Func<object?, Task>? Compensate { get; init; }
    }
}

/// <summary>
/// Flow execution result.
/// </summary>
public readonly record struct FlowResult
{
    public bool IsSuccess { get; init; }
    public bool IsCancelled { get; init; }
    public string FlowName { get; init; }
    public string? FailedStep { get; init; }
    public string? Error { get; init; }
    public int CompletedSteps { get; init; }
    public TimeSpan Duration { get; init; }

    public static FlowResult Ok(string flowName, int steps, TimeSpan duration) => new()
    {
        IsSuccess = true,
        FlowName = flowName,
        CompletedSteps = steps,
        Duration = duration
    };

    public static FlowResult Failed(string flowName, string failedStep, string error, int completedSteps, TimeSpan duration) => new()
    {
        IsSuccess = false,
        FlowName = flowName,
        FailedStep = failedStep,
        Error = error,
        CompletedSteps = completedSteps,
        Duration = duration
    };

    public static FlowResult Cancelled(string flowName, int completedSteps, TimeSpan duration) => new()
    {
        IsSuccess = false,
        IsCancelled = true,
        FlowName = flowName,
        CompletedSteps = completedSteps,
        Duration = duration
    };
}

/// <summary>
/// Flow execution result with value.
/// </summary>
public readonly record struct FlowResult<T>
{
    public bool IsSuccess { get; init; }
    public bool IsCancelled { get; init; }
    public T? Value { get; init; }
    public string FlowName { get; init; }
    public string? FailedStep { get; init; }
    public string? Error { get; init; }
    public int CompletedSteps { get; init; }
    public TimeSpan Duration { get; init; }

    public static FlowResult<T> Ok(T value, string flowName, int steps, TimeSpan duration) => new()
    {
        IsSuccess = true,
        Value = value,
        FlowName = flowName,
        CompletedSteps = steps,
        Duration = duration
    };

    public static FlowResult<T> Failed(string flowName, string failedStep, string error, int completedSteps, TimeSpan duration) => new()
    {
        IsSuccess = false,
        FlowName = flowName,
        FailedStep = failedStep,
        Error = error,
        CompletedSteps = completedSteps,
        Duration = duration
    };

    public static FlowResult<T> Cancelled(string flowName, int completedSteps, TimeSpan duration) => new()
    {
        IsSuccess = false,
        IsCancelled = true,
        FlowName = flowName,
        CompletedSteps = completedSteps,
        Duration = duration
    };
}

/// <summary>
/// Exception thrown when a flow step fails.
/// </summary>
public sealed class FlowStepException : Exception
{
    public string StepName { get; }

    public FlowStepException(string stepName, string message) : base(message)
    {
        StepName = stepName;
    }
}
