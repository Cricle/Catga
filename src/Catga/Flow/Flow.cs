using System.Diagnostics;
using Catga.Core;

namespace Catga.Flow;

/// <summary>
/// Simple Flow for Saga pattern. Auto-compensates on failure.
/// </summary>
/// <example>
/// await Flow.Create("CreateOrder")
///     .Step(() => repo.SaveAsync(order), () => repo.DeleteAsync(order.Id))
///     .Step(() => inventory.ReserveAsync(items), () => inventory.ReleaseAsync(items))
///     .Step(() => payment.ChargeAsync(amount), () => payment.RefundAsync(amount))
///     .ExecuteAsync();
/// </example>
public sealed class Flow
{
    private readonly string _name;
    private readonly Activity? _activity;
    private readonly List<(Func<Task<object?>> Execute, Func<object?, Task>? Compensate)> _steps = [];

    private Flow(string name)
    {
        _name = name;
        _activity = Activity.Current?.Source.StartActivity($"Flow:{name}");
    }

    public static Flow Create(string name) => new(name);

    /// <summary>Step with result and compensation.</summary>
    public Flow Step<T>(Func<Task<T>> execute, Func<T, Task>? compensate = null)
    {
        _steps.Add((
            async () => await execute(),
            compensate != null ? async r => await compensate((T)r!) : null
        ));
        return this;
    }

    /// <summary>Step with CatgaResult.</summary>
    public Flow Step<T>(Func<Task<CatgaResult<T>>> execute, Func<T, Task>? compensate = null)
    {
        _steps.Add((
            async () =>
            {
                var r = await execute();
                return r.IsSuccess ? r.Value : throw new InvalidOperationException(r.Error);
            },
            compensate != null ? async r => await compensate((T)r!) : null
        ));
        return this;
    }

    /// <summary>Step without result.</summary>
    public Flow Step(Func<Task> execute, Func<Task>? compensate = null)
    {
        _steps.Add((
            async () => { await execute(); return null; },
            compensate != null ? async _ => await compensate() : null
        ));
        return this;
    }

    /// <summary>Step with CatgaResult (no value).</summary>
    public Flow Step(Func<Task<CatgaResult>> execute, Func<Task>? compensate = null)
    {
        _steps.Add((
            async () =>
            {
                var r = await execute();
                if (!r.IsSuccess) throw new InvalidOperationException(r.Error);
                return null;
            },
            compensate != null ? async _ => await compensate() : null
        ));
        return this;
    }

    /// <summary>Execute flow. On failure, compensate in reverse order.</summary>
    public async Task<FlowResult> ExecuteAsync(CancellationToken ct = default)
    {
        var completed = new Stack<(Func<object?, Task>? Compensate, object? Result)>();
        var sw = Stopwatch.StartNew();

        try
        {
            for (int i = 0; i < _steps.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                _activity?.AddEvent(new ActivityEvent($"Step{i + 1}"));
                var result = await _steps[i].Execute();
                completed.Push((_steps[i].Compensate, result));
            }

            _activity?.SetStatus(ActivityStatusCode.Ok);
            return new FlowResult(true, _steps.Count, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            await CompensateAsync(completed);
            _activity?.SetStatus(ActivityStatusCode.Error, "Cancelled");
            return new FlowResult(false, completed.Count, sw.Elapsed) { IsCancelled = true };
        }
        catch (Exception ex)
        {
            await CompensateAsync(completed);
            _activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return new FlowResult(false, completed.Count, sw.Elapsed, ex.Message);
        }
        finally
        {
            _activity?.Dispose();
        }
    }

    /// <summary>Execute flow and return last step result.</summary>
    public async Task<FlowResult<T>> ExecuteAsync<T>(CancellationToken ct = default)
    {
        var completed = new Stack<(Func<object?, Task>? Compensate, object? Result)>();
        var sw = Stopwatch.StartNew();
        object? lastResult = default;

        try
        {
            for (int i = 0; i < _steps.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                _activity?.AddEvent(new ActivityEvent($"Step{i + 1}"));
                lastResult = await _steps[i].Execute();
                completed.Push((_steps[i].Compensate, lastResult));
            }

            _activity?.SetStatus(ActivityStatusCode.Ok);
            return new FlowResult<T>(true, (T)lastResult!, _steps.Count, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            await CompensateAsync(completed);
            _activity?.SetStatus(ActivityStatusCode.Error, "Cancelled");
            return new FlowResult<T>(false, default, completed.Count, sw.Elapsed) { IsCancelled = true };
        }
        catch (Exception ex)
        {
            await CompensateAsync(completed);
            _activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return new FlowResult<T>(false, default, completed.Count, sw.Elapsed, ex.Message);
        }
        finally
        {
            _activity?.Dispose();
        }
    }

    private static async Task CompensateAsync(Stack<(Func<object?, Task>? Compensate, object? Result)> completed)
    {
        while (completed.TryPop(out var item))
        {
            if (item.Compensate == null) continue;
            try { await item.Compensate(item.Result); } catch { }
        }
    }
}

/// <summary>Flow result.</summary>
public readonly record struct FlowResult(bool IsSuccess, int CompletedSteps, TimeSpan Duration, string? Error = null)
{
    public bool IsCancelled { get; init; }
}

/// <summary>Flow result with value.</summary>
public readonly record struct FlowResult<T>(bool IsSuccess, T? Value, int CompletedSteps, TimeSpan Duration, string? Error = null)
{
    public bool IsCancelled { get; init; }
}

