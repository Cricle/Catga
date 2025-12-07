using Catga.Abstractions;

namespace Catga.Flow.Dsl;

/// <summary>
/// Event published when a child flow completes, used to resume parent flows.
/// </summary>
public record FlowCompletedEvent(
    string FlowId,
    string? ParentCorrelationId,
    bool Success,
    string? Error,
    object? Result) : IEvent
{
    public long MessageId => 0;
}

/// <summary>
/// Handles FlowCompletedEvent to update wait conditions and resume parent flows.
/// </summary>
public class FlowResumeHandler(IDslFlowStore store) : IEventHandler<FlowCompletedEvent>
{
    public async ValueTask HandleAsync(FlowCompletedEvent @event, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(@event.ParentCorrelationId))
            return;

        var waitCondition = await store.GetWaitConditionAsync(@event.ParentCorrelationId, ct);
        if (waitCondition == null)
            return;

        // Update wait condition
        waitCondition.CompletedCount++;
        waitCondition.Results.Add(new FlowCompletedEventData
        {
            FlowId = @event.FlowId,
            ParentCorrelationId = @event.ParentCorrelationId,
            Success = @event.Success,
            Error = @event.Error,
            Result = @event.Result
        });

        await store.UpdateWaitConditionAsync(@event.ParentCorrelationId, waitCondition, ct);

        // Check if we should resume the parent flow
        bool shouldResume = waitCondition.Type switch
        {
            WaitType.All => waitCondition.CompletedCount >= waitCondition.ExpectedCount,
            WaitType.Any => waitCondition.Results.Any(r => r.Success) ||
                           waitCondition.CompletedCount >= waitCondition.ExpectedCount,
            _ => false
        };

        if (shouldResume)
        {
            // Publish event to trigger flow resume
            // The actual resume is handled by FlowTimeoutService or a dedicated resume service
        }
    }
}

/// <summary>
/// Background service that checks for timed out wait conditions and failed flows.
/// </summary>
public class FlowTimeoutService : IDisposable
{
    private readonly IDslFlowStore _store;
    private readonly TimeSpan _checkInterval;
    private readonly CancellationTokenSource _cts = new();
    private Task? _backgroundTask;

    public FlowTimeoutService(IDslFlowStore store, TimeSpan? checkInterval = null)
    {
        _store = store;
        _checkInterval = checkInterval ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Starts the background timeout checking.
    /// </summary>
    public void Start()
    {
        _backgroundTask = RunAsync(_cts.Token);
    }

    /// <summary>
    /// Stops the background timeout checking.
    /// </summary>
    public async Task StopAsync()
    {
        _cts.Cancel();
        if (_backgroundTask != null)
        {
            try
            {
                await _backgroundTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await CheckTimeoutsAsync(ct);
                await Task.Delay(_checkInterval, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                // Log error and continue
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    private async Task CheckTimeoutsAsync(CancellationToken ct)
    {
        var timedOut = await _store.GetTimedOutWaitConditionsAsync(ct);

        foreach (var condition in timedOut)
        {
            // Mark the wait condition as timed out by adding a failure result
            condition.Results.Add(new FlowCompletedEventData
            {
                FlowId = "timeout",
                Success = false,
                Error = $"Wait condition timed out after {condition.Timeout}"
            });
            condition.CompletedCount = condition.ExpectedCount; // Force completion

            await _store.UpdateWaitConditionAsync(condition.CorrelationId, condition, ct);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
