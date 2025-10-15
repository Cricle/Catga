using Catga.Debugger.Models;
using Catga.Debugger.Storage;
using Microsoft.Extensions.Logging;

namespace Catga.Debugger.Replay;

/// <summary>State reconstructor - rebuilds state at any timestamp</summary>
public sealed class StateReconstructor
{
    private readonly IEventStore _eventStore;
    private readonly ILogger<StateReconstructor> _logger;
    
    public StateReconstructor(
        IEventStore eventStore,
        ILogger<StateReconstructor> logger)
    {
        _eventStore = eventStore;
        _logger = logger;
    }
    
    /// <summary>Reconstruct complete system state at timestamp</summary>
    public async Task<SystemState> ReconstructStateAsync(
        DateTime timestamp,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Reconstructing state at {Timestamp}", timestamp);
        
        // Find nearest snapshot before timestamp
        var snapshot = await FindNearestSnapshotAsync(timestamp, cancellationToken);
        
        if (snapshot == null)
        {
            _logger.LogWarning("No snapshot found before {Timestamp}, reconstructing from start", timestamp);
            return await ReconstructFromEventsAsync(DateTime.MinValue, timestamp, cancellationToken);
        }
        
        // Reconstruct from snapshot
        _logger.LogInformation("Found snapshot at {SnapshotTime}, replaying events", snapshot.Timestamp);
        return await ReconstructFromSnapshotAsync(snapshot, timestamp, cancellationToken);
    }
    
    /// <summary>Track variable value changes over time</summary>
    public async Task<VariableTimeline> TrackVariableAsync(
        string variableName,
        DateTime startTime,
        DateTime endTime,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Tracking variable {Variable} from {Start} to {End}",
            variableName, startTime, endTime);
        
        var timeline = new VariableTimeline(variableName);
        
        var events = await _eventStore.GetEventsAsync(startTime, endTime, cancellationToken);
        
        foreach (var evt in events.Where(e => e.Type == EventType.StateSnapshot))
        {
            if (evt.Data is StateSnapshot snapshot &&
                snapshot.Variables.TryGetValue(variableName, out var value))
            {
                timeline.AddPoint(evt.Timestamp, value);
            }
        }
        
        _logger.LogInformation(
            "Variable timeline for {Variable} has {Count} data points",
            variableName, timeline.Points.Count);
        
        return timeline;
    }
    
    /// <summary>Track multiple variables</summary>
    public async Task<Dictionary<string, VariableTimeline>> TrackVariablesAsync(
        IEnumerable<string> variableNames,
        DateTime startTime,
        DateTime endTime,
        CancellationToken cancellationToken = default)
    {
        var timelines = new Dictionary<string, VariableTimeline>();
        
        foreach (var variable in variableNames)
        {
            timelines[variable] = await TrackVariableAsync(
                variable, startTime, endTime, cancellationToken);
        }
        
        return timelines;
    }
    
    private async Task<StateSnapshot?> FindNearestSnapshotAsync(
        DateTime timestamp,
        CancellationToken cancellationToken)
    {
        // Query events before timestamp
        var events = await _eventStore.GetEventsAsync(
            DateTime.MinValue,
            timestamp,
            cancellationToken);
        
        // Find latest snapshot
        var snapshotEvent = events
            .Where(e => e.Type == EventType.StateSnapshot)
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefault();
        
        return snapshotEvent?.Data as StateSnapshot;
    }
    
    private async Task<SystemState> ReconstructFromSnapshotAsync(
        StateSnapshot snapshot,
        DateTime targetTime,
        CancellationToken cancellationToken)
    {
        var state = new SystemState
        {
            Timestamp = targetTime,
            Variables = new Dictionary<string, object?>(snapshot.Variables),
            CallStack = new List<CallFrame>(snapshot.CallStack)
        };
        
        // Replay events from snapshot to target time
        var events = await _eventStore.GetEventsAsync(
            snapshot.Timestamp,
            targetTime,
            cancellationToken);
        
        foreach (var evt in events.OrderBy(e => e.Timestamp))
        {
            ApplyEvent(state, evt);
        }
        
        return state;
    }
    
    private async Task<SystemState> ReconstructFromEventsAsync(
        DateTime startTime,
        DateTime endTime,
        CancellationToken cancellationToken)
    {
        var state = new SystemState
        {
            Timestamp = endTime,
            Variables = new Dictionary<string, object?>(),
            CallStack = new List<CallFrame>()
        };
        
        var events = await _eventStore.GetEventsAsync(startTime, endTime, cancellationToken);
        
        foreach (var evt in events.OrderBy(e => e.Timestamp))
        {
            ApplyEvent(state, evt);
        }
        
        return state;
    }
    
    private void ApplyEvent(SystemState state, ReplayableEvent evt)
    {
        switch (evt.Type)
        {
            case EventType.StateSnapshot when evt.Data is StateSnapshot snapshot:
                // Update state from snapshot
                foreach (var kvp in snapshot.Variables)
                {
                    state.Variables[kvp.Key] = kvp.Value;
                }
                state.CallStack = new List<CallFrame>(snapshot.CallStack);
                break;
            
            case EventType.VariableChanged when evt.Data is Dictionary<string, object?> changes:
                // Apply variable changes
                foreach (var kvp in changes)
                {
                    state.Variables[kvp.Key] = kvp.Value;
                }
                break;
            
            // Other event types can be handled here
        }
    }
}

/// <summary>System state at a specific point in time</summary>
public sealed class SystemState
{
    public required DateTime Timestamp { get; init; }
    public required Dictionary<string, object?> Variables { get; set; }
    public required List<CallFrame> CallStack { get; set; }
}

/// <summary>Variable value timeline</summary>
public sealed class VariableTimeline
{
    public VariableTimeline(string variableName)
    {
        VariableName = variableName;
        Points = new List<TimelinePoint>();
    }
    
    public string VariableName { get; }
    public List<TimelinePoint> Points { get; }
    
    public void AddPoint(DateTime timestamp, object? value)
    {
        Points.Add(new TimelinePoint { Timestamp = timestamp, Value = value });
    }
    
    /// <summary>Get value at specific timestamp (interpolated)</summary>
    public object? GetValueAt(DateTime timestamp)
    {
        if (Points.Count == 0) return null;
        
        // Find nearest point before or at timestamp
        var point = Points
            .Where(p => p.Timestamp <= timestamp)
            .OrderByDescending(p => p.Timestamp)
            .FirstOrDefault();
        
        return point?.Value;
    }
}

/// <summary>Timeline data point</summary>
public sealed class TimelinePoint
{
    public required DateTime Timestamp { get; init; }
    public required object? Value { get; init; }
}

