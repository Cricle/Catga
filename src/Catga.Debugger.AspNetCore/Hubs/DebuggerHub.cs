using Catga.Debugger.Replay;
using Catga.Debugger.Storage;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Catga.Debugger.AspNetCore.Hubs;

/// <summary>SignalR Hub for real-time debugger updates (AOT-compatible)</summary>
public sealed class DebuggerHub : Hub<IDebuggerClient>
{
    private readonly IEventStore _eventStore;
    private readonly IReplayEngine _replayEngine;
    private readonly ILogger<DebuggerHub> _logger;
    
    public DebuggerHub(
        IEventStore eventStore,
        IReplayEngine replayEngine,
        ILogger<DebuggerHub> logger)
    {
        _eventStore = eventStore;
        _replayEngine = replayEngine;
        _logger = logger;
    }
    
    /// <summary>Subscribe to flow updates</summary>
    public async Task SubscribeToFlow(string correlationId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"flow-{correlationId}");
        _logger.LogInformation("Client {ConnectionId} subscribed to flow {CorrelationId}", 
            Context.ConnectionId, correlationId);
    }
    
    /// <summary>Unsubscribe from flow updates</summary>
    public async Task UnsubscribeFromFlow(string correlationId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"flow-{correlationId}");
        _logger.LogInformation("Client {ConnectionId} unsubscribed from flow {CorrelationId}", 
            Context.ConnectionId, correlationId);
    }
    
    /// <summary>Subscribe to system-wide updates</summary>
    public async Task SubscribeToSystem()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "system");
        _logger.LogInformation("Client {ConnectionId} subscribed to system updates", 
            Context.ConnectionId);
    }
    
    /// <summary>Unsubscribe from system-wide updates</summary>
    public async Task UnsubscribeFromSystem()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "system");
        _logger.LogInformation("Client {ConnectionId} unsubscribed from system updates", 
            Context.ConnectionId);
    }
    
    /// <summary>Get current stats</summary>
    public async Task<StatsUpdate> GetStats()
    {
        var stats = await _eventStore.GetStatsAsync(Context.ConnectionAborted);
        
        return new StatsUpdate
        {
            TotalEvents = stats.TotalEvents,
            TotalFlows = stats.TotalFlows,
            StorageSizeBytes = stats.StorageSizeBytes,
            Timestamp = DateTime.UtcNow
        };
    }
    
    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client {ConnectionId} connected", Context.ConnectionId);
        await base.OnConnectedAsync();
    }
    
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client {ConnectionId} disconnected. Exception: {Exception}", 
            Context.ConnectionId, exception?.Message);
        await base.OnDisconnectedAsync(exception);
    }
}

/// <summary>Strongly-typed client interface (AOT-compatible)</summary>
public interface IDebuggerClient
{
    /// <summary>Receive new flow event</summary>
    Task FlowEventReceived(FlowEventUpdate update);
    
    /// <summary>Receive stats update</summary>
    Task StatsUpdated(StatsUpdate stats);
    
    /// <summary>Receive replay progress</summary>
    Task ReplayProgress(ReplayProgressUpdate progress);
}

// AOT-compatible update types

public sealed record FlowEventUpdate
{
    public required string CorrelationId { get; init; }
    public required string EventId { get; init; }
    public required string EventType { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string? ServiceName { get; init; }
}

public sealed record StatsUpdate
{
    public required long TotalEvents { get; init; }
    public required long TotalFlows { get; init; }
    public required long StorageSizeBytes { get; init; }
    public required DateTime Timestamp { get; init; }
}

public sealed record ReplayProgressUpdate
{
    public required string CorrelationId { get; init; }
    public required int CurrentStep { get; init; }
    public required int TotalSteps { get; init; }
    public required double Progress { get; init; }
}

