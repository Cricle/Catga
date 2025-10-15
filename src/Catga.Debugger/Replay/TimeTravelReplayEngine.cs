using Catga.Debugger.Models;
using Catga.Debugger.Storage;
using Microsoft.Extensions.Logging;

namespace Catga.Debugger.Replay;

/// <summary>Time-travel replay engine implementation</summary>
public sealed class TimeTravelReplayEngine : IReplayEngine
{
    private readonly IEventStore _eventStore;
    private readonly ILogger<TimeTravelReplayEngine> _logger;

    public TimeTravelReplayEngine(
        IEventStore eventStore,
        ILogger<TimeTravelReplayEngine> logger)
    {
        _eventStore = eventStore;
        _logger = logger;
    }

    public async Task<SystemReplay> ReplaySystemAsync(
        DateTime startTime,
        DateTime endTime,
        double speed = 1.0,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Starting system replay from {Start} to {End} at {Speed}x speed",
            startTime, endTime, speed);

        // Load all events in time range
        var events = await _eventStore.GetEventsAsync(startTime, endTime, cancellationToken);

        // Sort by timestamp
        var timeline = events.OrderBy(e => e.Timestamp).ToList();

        _logger.LogInformation("Loaded {Count} events for system replay", timeline.Count);

        return new SystemReplay(timeline, speed);
    }

    public async Task<FlowReplay> ReplayFlowAsync(
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting flow replay for {CorrelationId}", correlationId);

        // Load all events for this flow
        var events = await _eventStore.GetEventsByCorrelationAsync(correlationId, cancellationToken);

        if (!events.Any())
        {
            _logger.LogWarning("No events found for correlation ID {CorrelationId}", correlationId);
        }

        // Build state machine
        var stateMachine = new FlowStateMachine(events);

        _logger.LogInformation(
            "Flow replay ready with {Steps} steps for {CorrelationId}",
            stateMachine.TotalSteps, correlationId);

        return new FlowReplay(stateMachine);
    }

    public async Task<ParallelReplay> ReplayParallelAsync(
        IEnumerable<string> correlationIds,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Starting parallel replay for {Count} flows",
            correlationIds.Count());

        var replays = new List<FlowReplay>();

        foreach (var correlationId in correlationIds)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var replay = await ReplayFlowAsync(correlationId, cancellationToken);
            replays.Add(replay);
        }

        _logger.LogInformation("Parallel replay ready with {Count} flows", replays.Count);

        return new ParallelReplay(replays);
    }
}

