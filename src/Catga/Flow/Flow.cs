using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Flow;

#region Flow State

/// <summary>Flow execution status.</summary>
public enum FlowStatus : byte { Running = 0, Compensating = 1, Done = 2, Failed = 3 }

/// <summary>Persistent flow state. Minimal for performance.</summary>
public sealed class FlowState
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public FlowStatus Status { get; set; }
    public int Step { get; set; }
    public long Version { get; set; }
    public string? Owner { get; set; }
    public long HeartbeatAt { get; set; }
    public byte[]? Data { get; set; }
    public string? Error { get; set; }
}

/// <summary>Flow result.</summary>
public readonly record struct FlowResult(bool IsSuccess, int CompletedSteps, TimeSpan Duration, string? Error = null)
{
    public bool IsCancelled { get; init; }
    public string? FlowId { get; init; }
}

/// <summary>Flow result with value.</summary>
public readonly record struct FlowResult<T>(bool IsSuccess, T? Value, int CompletedSteps, TimeSpan Duration, string? Error = null)
{
    public bool IsCancelled { get; init; }
    public string? FlowId { get; init; }
}

#endregion

#region Flow Store

/// <summary>Flow state persistence. Lock-free with CAS.</summary>
public interface IFlowStore
{
    /// <summary>Create flow. Returns false if ID exists (idempotent).</summary>
    ValueTask<bool> CreateAsync(FlowState state, CancellationToken ct = default);

    /// <summary>CAS update. Returns false if version mismatch.</summary>
    ValueTask<bool> UpdateAsync(FlowState state, CancellationToken ct = default);

    /// <summary>Get flow by ID.</summary>
    ValueTask<FlowState?> GetAsync(string id, CancellationToken ct = default);

    /// <summary>Try claim an abandoned flow (heartbeat timeout). Lock-free CAS.</summary>
    ValueTask<FlowState?> TryClaimAsync(string type, string owner, long timeoutMs, CancellationToken ct = default);

    /// <summary>Heartbeat to keep ownership. Returns false if lost.</summary>
    ValueTask<bool> HeartbeatAsync(string id, string owner, long version, CancellationToken ct = default);
}

#endregion

#region Flow Executor

/// <summary>
/// Distributed flow executor with auto-persistence and recovery.
/// Lock-free, AOT-compatible, high-performance.
/// </summary>
public sealed class FlowExecutor
{
    private readonly IFlowStore _store;
    private readonly string _nodeId;
    private readonly TimeSpan _heartbeatInterval;
    private readonly long _claimTimeoutMs;

    public FlowExecutor(IFlowStore store, FlowOptions? options = null)
    {
        _store = store;
        _nodeId = options?.NodeId ?? Environment.MachineName;
        _heartbeatInterval = options?.HeartbeatInterval ?? TimeSpan.FromSeconds(5);
        _claimTimeoutMs = (long)(options?.ClaimTimeout ?? TimeSpan.FromSeconds(30)).TotalMilliseconds;
    }

    /// <summary>Execute a persistent flow with auto-recovery.</summary>
    public async Task<FlowResult> ExecuteAsync(
        string flowId,
        string flowType,
        ReadOnlyMemory<byte> data,
        Func<FlowState, CancellationToken, Task<FlowResult>> executor,
        CancellationToken ct = default)
    {
        var now = Stopwatch.GetTimestamp();
        var state = new FlowState
        {
            Id = flowId,
            Type = flowType,
            Status = FlowStatus.Running,
            Step = 0,
            Version = 0,
            Owner = _nodeId,
            HeartbeatAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Data = data.ToArray()
        };

        // Try create (idempotent)
        if (!await _store.CreateAsync(state, ct))
        {
            // Already exists - try to resume
            state = await _store.GetAsync(flowId, ct);
            if (state == null)
                return new FlowResult(false, 0, TimeSpan.Zero, "Flow not found") { FlowId = flowId };

            if (state.Status == FlowStatus.Done)
                return new FlowResult(true, state.Step, TimeSpan.Zero) { FlowId = flowId };

            if (state.Status == FlowStatus.Failed)
                return new FlowResult(false, state.Step, TimeSpan.Zero, state.Error) { FlowId = flowId };

            // Try claim if abandoned
            if (state.Owner != _nodeId)
            {
                var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (nowMs - state.HeartbeatAt < _claimTimeoutMs)
                    return new FlowResult(false, state.Step, TimeSpan.Zero, "Flow owned by another node") { FlowId = flowId };

                state.Owner = _nodeId;
                state.HeartbeatAt = nowMs;
                if (!await _store.UpdateAsync(state, ct))
                    return new FlowResult(false, state.Step, TimeSpan.Zero, "Failed to claim flow") { FlowId = flowId };
            }
        }

        // Execute with heartbeat
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeatTask = HeartbeatLoopAsync(state, cts.Token);

        try
        {
            var result = await executor(state, ct);

            state.Status = result.IsSuccess ? FlowStatus.Done : FlowStatus.Failed;
            state.Error = result.Error;
            state.Step = result.CompletedSteps;
            await _store.UpdateAsync(state, CancellationToken.None);

            return result with { FlowId = flowId };
        }
        catch (Exception ex)
        {
            state.Status = FlowStatus.Failed;
            state.Error = ex.Message;
            await _store.UpdateAsync(state, CancellationToken.None);
            return new FlowResult(false, state.Step, TimeSpan.Zero, ex.Message) { FlowId = flowId };
        }
        finally
        {
            cts.Cancel();
            try { await heartbeatTask; } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Heartbeat task cleanup error: {ex.Message}"); }
        }
    }

    private async Task HeartbeatLoopAsync(FlowState state, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_heartbeatInterval, ct);
                state.HeartbeatAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                await _store.HeartbeatAsync(state.Id, _nodeId, state.Version, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Heartbeat error: {ex.Message}"); }
        }
    }
}

/// <summary>Flow configuration options.</summary>
public sealed class FlowOptions
{
    public string? NodeId { get; set; }
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan ClaimTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

#endregion

#region Simple Flow (In-Memory, No Persistence)

/// <summary>
/// Simple in-memory flow for local transactions. No persistence.
/// Use FlowExecutor for distributed scenarios.
/// </summary>
public sealed class Flow
{
    private readonly string _name;
    private readonly Activity? _activity;
    private readonly List<(Func<CancellationToken, Task> Execute, Func<CancellationToken, Task>? Compensate)> _steps = [];

    private Flow(string name)
    {
        _name = name;
        _activity = Activity.Current?.Source.StartActivity($"Flow:{name}");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Flow Create(string name) => new(name);

    /// <summary>Add step with optional compensation.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Flow Step(Func<CancellationToken, Task> execute, Func<CancellationToken, Task>? compensate = null)
    {
        _steps.Add((execute, compensate));
        return this;
    }

    /// <summary>Add step (no CancellationToken overload).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Flow Step(Func<Task> execute, Func<Task>? compensate = null)
    {
        _steps.Add((
            _ => execute(),
            compensate != null ? _ => compensate() : null
        ));
        return this;
    }

    /// <summary>Execute flow. Auto-compensate on failure.</summary>
    public async Task<FlowResult> ExecuteAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var completedCount = 0;

        try
        {
            for (int i = 0; i < _steps.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                _activity?.AddEvent(new ActivityEvent($"Step{i + 1}"));
                await _steps[i].Execute(ct);
                completedCount++;
            }

            _activity?.SetStatus(ActivityStatusCode.Ok);
            return new FlowResult(true, completedCount, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            await CompensateAsync(completedCount, ct);
            _activity?.SetStatus(ActivityStatusCode.Error, "Cancelled");
            return new FlowResult(false, completedCount, sw.Elapsed) { IsCancelled = true };
        }
        catch (Exception ex)
        {
            await CompensateAsync(completedCount, CancellationToken.None);
            _activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return new FlowResult(false, completedCount, sw.Elapsed, ex.Message);
        }
        finally
        {
            _activity?.Dispose();
        }
    }

    /// <summary>Execute from specific step (for recovery).</summary>
    public async Task<FlowResult> ExecuteFromAsync(int startStep, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var completedCount = startStep;

        try
        {
            for (int i = startStep; i < _steps.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                _activity?.AddEvent(new ActivityEvent($"Step{i + 1}"));
                await _steps[i].Execute(ct);
                completedCount++;
            }

            _activity?.SetStatus(ActivityStatusCode.Ok);
            return new FlowResult(true, completedCount, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            await CompensateFromAsync(completedCount, startStep, ct);
            _activity?.SetStatus(ActivityStatusCode.Error, "Cancelled");
            return new FlowResult(false, completedCount, sw.Elapsed) { IsCancelled = true };
        }
        catch (Exception ex)
        {
            await CompensateFromAsync(completedCount, startStep, CancellationToken.None);
            _activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return new FlowResult(false, completedCount, sw.Elapsed, ex.Message);
        }
        finally
        {
            _activity?.Dispose();
        }
    }

    private async Task CompensateAsync(int completedCount, CancellationToken ct)
    {
        for (int i = completedCount - 1; i >= 0; i--)
        {
            var compensate = _steps[i].Compensate;
            if (compensate == null) continue;
            try { await compensate(ct); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Compensation error at step {i}: {ex.Message}"); }
        }
    }

    private async Task CompensateFromAsync(int completedCount, int startStep, CancellationToken ct)
    {
        for (int i = completedCount - 1; i >= startStep; i--)
        {
            var compensate = _steps[i].Compensate;
            if (compensate == null) continue;
            try { await compensate(ct); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Compensation error at step {i}: {ex.Message}"); }
        }
    }
}

#endregion

#region Flow Recovery Service

/// <summary>
/// Background service for automatic flow recovery.
/// Scans for abandoned flows and resumes them.
/// </summary>
public abstract class FlowRecoveryService : IDisposable
{
    private readonly IFlowStore _store;
    private readonly FlowOptions _options;
    private readonly string _nodeId;
    private readonly CancellationTokenSource _cts = new();
    private Task? _task;

    protected FlowRecoveryService(IFlowStore store, FlowOptions? options = null)
    {
        _store = store;
        _options = options ?? new FlowOptions();
        _nodeId = _options.NodeId ?? Environment.MachineName;
    }

    /// <summary>Flow types to recover.</summary>
    protected abstract IEnumerable<string> FlowTypes { get; }

    /// <summary>Resume a flow from its state.</summary>
    protected abstract Task<FlowResult> ResumeFlowAsync(FlowState state, CancellationToken ct);

    public void Start()
    {
        _task = RecoveryLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts.Cancel();
        _task?.Wait(TimeSpan.FromSeconds(5));
    }

    private async Task RecoveryLoopAsync(CancellationToken ct)
    {
        var interval = _options.HeartbeatInterval * 2;
        var timeoutMs = (long)_options.ClaimTimeout.TotalMilliseconds;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, ct);

                foreach (var type in FlowTypes)
                {
                    var state = await _store.TryClaimAsync(type, _nodeId, timeoutMs, ct);
                    if (state != null)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var result = await ResumeFlowAsync(state, ct);
                                state.Status = result.IsSuccess ? FlowStatus.Done : FlowStatus.Failed;
                                state.Error = result.Error;
                                await _store.UpdateAsync(state, CancellationToken.None);
                            }
                            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Resume flow error: {ex.Message}"); }
                        }, ct);
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Recovery scan error: {ex.Message}"); }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}

#endregion

#region DI Extensions

/// <summary>Flow service collection extensions.</summary>
public static class FlowServiceCollectionExtensions
{
    /// <summary>Add flow services with custom store.</summary>
    public static IServiceCollection AddFlow<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStore>(this IServiceCollection services, Action<FlowOptions>? configure = null)
        where TStore : class, IFlowStore
    {
        var options = new FlowOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<IFlowStore, TStore>();
        services.AddSingleton<FlowExecutor>();

        return services;
    }
}

#endregion
