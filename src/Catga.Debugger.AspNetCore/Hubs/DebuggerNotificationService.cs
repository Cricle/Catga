using System.Threading.Channels;
using Catga.Debugger.Models;
using Catga.Debugger.Storage;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Catga.Debugger.AspNetCore.Hubs;

/// <summary>Background service for pushing real-time updates via SignalR</summary>
/// <remarks>
/// Uses System.Threading.Channels for zero-allocation queuing.
/// Avoids Task.Run to prevent thread pool exhaustion.
/// </remarks>
public sealed partial class DebuggerNotificationService : BackgroundService
{
    private readonly IHubContext<DebuggerHub, IDebuggerClient> _hubContext;
    private readonly IEventStore _eventStore;
    private readonly ILogger<DebuggerNotificationService> _logger;

    // Use Channel for efficient, thread-safe queuing
    private readonly Channel<ReplayableEvent> _eventChannel;
    private readonly Channel<StatsUpdate> _statsChannel;

    // Use PeriodicTimer instead of Task.Run for periodic tasks
    private readonly PeriodicTimer _statsTimer;

    public DebuggerNotificationService(
        IHubContext<DebuggerHub, IDebuggerClient> hubContext,
        IEventStore eventStore,
        ILogger<DebuggerNotificationService> logger)
    {
        _hubContext = hubContext;
        _eventStore = eventStore;
        _logger = logger;

        // Bounded channel with backpressure
        _eventChannel = Channel.CreateBounded<ReplayableEvent>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _statsChannel = Channel.CreateBounded<StatsUpdate>(new BoundedChannelOptions(10)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _statsTimer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        // Subscribe to event store notifications
        _eventStore.EventSaved += EnqueueEvent;
    }

    /// <summary>Enqueue event for broadcast (called by EventStore)</summary>
    public void EnqueueEvent(ReplayableEvent evt)
    {
        // Non-blocking write
        if (!_eventChannel.Writer.TryWrite(evt))
        {
            LogEventChannelFull(evt.Id);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogServiceStarted();

        // Start event processing task
        var eventTask = ProcessEventsAsync(stoppingToken);

        // Start stats broadcasting task
        var statsTask = BroadcastStatsAsync(stoppingToken);

        // Wait for both tasks
        await Task.WhenAll(eventTask, statsTask);

        LogServiceStopped();
    }

    private async Task ProcessEventsAsync(CancellationToken ct)
    {
        await foreach (var evt in _eventChannel.Reader.ReadAllAsync(ct))
        {
            try
            {
                // Broadcast individual event to specific flow group
                var flowGroup = $"flow-{evt.CorrelationId}";
                await _hubContext.Clients.Group(flowGroup).FlowEventReceived(new FlowEventUpdate
                {
                    CorrelationId = evt.CorrelationId,
                    EventId = evt.Id,
                    EventType = evt.Type.ToString(),
                    Timestamp = evt.Timestamp,
                    ServiceName = evt.ServiceName
                });

                // Get all events for this flow and broadcast complete FlowInfo to all clients
                var flowEvents = await _eventStore.GetEventsByCorrelationAsync(evt.CorrelationId, ct);
                var eventsList = flowEvents.ToList();

                if (eventsList.Count > 0)
                {
                    var firstEvent = eventsList.OrderBy(e => e.Timestamp).First();
                    var lastEvent = eventsList.OrderByDescending(e => e.Timestamp).First();
                    var duration = (lastEvent.Timestamp - firstEvent.Timestamp).TotalMilliseconds;

                    var flowInfo = new
                    {
                        correlationId = evt.CorrelationId,
                        messageType = firstEvent.MessageType ?? "Unknown",
                        startTime = firstEvent.Timestamp,
                        endTime = lastEvent.Timestamp,
                        duration = duration,
                        eventCount = eventsList.Count,
                        status = eventsList.Any(e => e.Type == Models.EventType.ExceptionThrown) ? "Error" : "Success",
                        hasErrors = eventsList.Any(e => e.Type == Models.EventType.ExceptionThrown)
                    };

                    // Broadcast complete flow info to all clients (matches frontend expectation)
                    await _hubContext.Clients.All.FlowUpdate(flowInfo);
                }
            }
            catch (Exception ex)
            {
                LogBroadcastFailed(ex, evt.Id);
            }
        }
    }

    private async Task BroadcastStatsAsync(CancellationToken ct)
    {
        while (await _statsTimer.WaitForNextTickAsync(ct))
        {
            try
            {
                var stats = await _eventStore.GetStatsAsync(ct);

                var statsUpdate = new StatsUpdate
                {
                    TotalEvents = stats.TotalEvents,
                    TotalFlows = stats.TotalFlows,
                    StorageSizeBytes = stats.StorageSizeBytes,
                    Timestamp = DateTime.UtcNow
                };

                // Broadcast to all connected clients (use StatsUpdate to match frontend)
                await _hubContext.Clients.All.StatsUpdate(statsUpdate);
            }
            catch (Exception ex)
            {
                LogStatsBroadcastFailed(ex);
            }
        }
    }

    public override void Dispose()
    {
        // Unsubscribe from event store
        _eventStore.EventSaved -= EnqueueEvent;

        _statsTimer.Dispose();
        base.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Event channel full, dropping event {EventId}")]
    partial void LogEventChannelFull(string eventId);

    [LoggerMessage(Level = LogLevel.Information, Message = "DebuggerNotificationService started")]
    partial void LogServiceStarted();

    [LoggerMessage(Level = LogLevel.Information, Message = "DebuggerNotificationService stopped")]
    partial void LogServiceStopped();

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to broadcast event {EventId}")]
    partial void LogBroadcastFailed(Exception ex, string eventId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to broadcast stats")]
    partial void LogStatsBroadcastFailed(Exception ex);
}

