using Catga.Debugger.Models;

namespace Catga.Debugger.Replay;

/// <summary>Replay engine for time-travel debugging</summary>
public interface IReplayEngine
{
    /// <summary>System-wide replay (macro view)</summary>
    Task<SystemReplay> ReplaySystemAsync(
        DateTime startTime,
        DateTime endTime,
        double speed = 1.0,
        CancellationToken cancellationToken = default);
    
    /// <summary>Flow-level replay (micro view)</summary>
    Task<FlowReplay> ReplayFlowAsync(
        string correlationId,
        CancellationToken cancellationToken = default);
    
    /// <summary>Parallel replay of multiple flows</summary>
    Task<ParallelReplay> ReplayParallelAsync(
        IEnumerable<string> correlationIds,
        CancellationToken cancellationToken = default);
}

/// <summary>System-wide replay controller</summary>
public sealed class SystemReplay
{
    private readonly List<ReplayableEvent> _timeline;
    private int _currentIndex;
    private readonly double _speed;
    
    public SystemReplay(List<ReplayableEvent> timeline, double speed = 1.0)
    {
        _timeline = timeline;
        _speed = speed;
        _currentIndex = 0;
    }
    
    public IReadOnlyList<ReplayableEvent> Timeline => _timeline;
    public int CurrentIndex => _currentIndex;
    public double Speed => _speed;
    
    public DateTime StartTime => _timeline.FirstOrDefault()?.Timestamp ?? DateTime.MinValue;
    public DateTime EndTime => _timeline.LastOrDefault()?.Timestamp ?? DateTime.MaxValue;
    public DateTime CurrentTime => _currentIndex < _timeline.Count 
        ? _timeline[_currentIndex].Timestamp 
        : EndTime;
    
    /// <summary>Get events in time window</summary>
    public IEnumerable<ReplayableEvent> GetEventsInWindow(DateTime start, DateTime end)
    {
        return _timeline.Where(e => e.Timestamp >= start && e.Timestamp <= end);
    }
    
    /// <summary>Get global metrics timeline</summary>
    public IEnumerable<(DateTime Time, double Value)> GetMetricsTimeline()
    {
        return _timeline
            .Where(e => e.Type == EventType.PerformanceMetric)
            .Select(e => (e.Timestamp, GetMetricValue(e.Data)));
    }
    
    private double GetMetricValue(object data)
    {
        if (data is IDictionary<string, object> dict && dict.TryGetValue("Duration", out var duration))
            return Convert.ToDouble(duration);
        return 0;
    }
}

/// <summary>Flow-level replay controller</summary>
public sealed class FlowReplay
{
    private readonly FlowStateMachine _stateMachine;
    
    public FlowReplay(FlowStateMachine stateMachine)
    {
        _stateMachine = stateMachine;
    }
    
    public FlowStateMachine StateMachine => _stateMachine;
    
    /// <summary>Step forward by N steps</summary>
    public async Task StepAsync(int steps = 1)
    {
        await _stateMachine.StepAsync(steps);
    }
    
    /// <summary>Step into sub-flow</summary>
    public async Task StepIntoAsync()
    {
        await _stateMachine.StepIntoAsync();
    }
    
    /// <summary>Step over current step</summary>
    public async Task StepOverAsync()
    {
        await _stateMachine.StepOverAsync();
    }
    
    /// <summary>Step out of current context</summary>
    public async Task StepOutAsync()
    {
        await _stateMachine.StepOutAsync();
    }
    
    /// <summary>Jump to specific timestamp</summary>
    public async Task JumpToTimestampAsync(DateTime timestamp)
    {
        await _stateMachine.JumpToAsync(timestamp);
    }
    
    /// <summary>Get current state</summary>
    public object? GetCurrentState() => _stateMachine.CurrentState;
    
    /// <summary>Get variables at current step</summary>
    public Dictionary<string, object?> GetVariables() => _stateMachine.Variables;
    
    /// <summary>Get call stack at current step</summary>
    public List<CallFrame> GetCallStack() => _stateMachine.CallStack;
}

/// <summary>Parallel replay controller</summary>
public sealed class ParallelReplay
{
    private readonly List<FlowReplay> _replays;
    
    public ParallelReplay(List<FlowReplay> replays)
    {
        _replays = replays;
    }
    
    public IReadOnlyList<FlowReplay> Replays => _replays;
    
    /// <summary>Step all flows synchronously</summary>
    public async Task StepAllAsync()
    {
        await Task.WhenAll(_replays.Select(r => r.StepAsync()));
    }
    
    /// <summary>Sync all flows to specific timestamp</summary>
    public async Task SyncToTimestampAsync(DateTime timestamp)
    {
        await Task.WhenAll(_replays.Select(r => r.JumpToTimestampAsync(timestamp)));
    }
}

/// <summary>Flow state machine for single-step execution</summary>
public sealed class FlowStateMachine
{
    private readonly List<ReplayableEvent> _events;
    private int _currentIndex;
    private object? _currentState;
    private Dictionary<string, object?> _variables = new();
    private List<CallFrame> _callStack = new();
    
    public FlowStateMachine(IEnumerable<ReplayableEvent> events)
    {
        _events = events.OrderBy(e => e.Timestamp).ToList();
        _currentIndex = 0;
        ReconstructState();
    }
    
    public object? CurrentState => _currentState;
    public Dictionary<string, object?> Variables => _variables;
    public List<CallFrame> CallStack => _callStack;
    public int CurrentIndex => _currentIndex;
    public int TotalSteps => _events.Count;
    
    public async Task StepAsync(int steps)
    {
        _currentIndex = Math.Clamp(_currentIndex + steps, 0, _events.Count - 1);
        ReconstructState();
        await Task.CompletedTask;
    }
    
    public async Task StepIntoAsync()
    {
        // Find next sub-flow event
        for (int i = _currentIndex + 1; i < _events.Count; i++)
        {
            if (_events[i].ParentEventId == _events[_currentIndex].Id)
            {
                _currentIndex = i;
                ReconstructState();
                break;
            }
        }
        await Task.CompletedTask;
    }
    
    public async Task StepOverAsync()
    {
        // Skip to next sibling event
        var currentParent = _events[_currentIndex].ParentEventId;
        for (int i = _currentIndex + 1; i < _events.Count; i++)
        {
            if (_events[i].ParentEventId == currentParent)
            {
                _currentIndex = i;
                ReconstructState();
                break;
            }
        }
        await Task.CompletedTask;
    }
    
    public async Task StepOutAsync()
    {
        // Go to parent event
        var parentId = _events[_currentIndex].ParentEventId;
        if (parentId != null)
        {
            var parentIndex = _events.FindIndex(e => e.Id == parentId);
            if (parentIndex >= 0)
            {
                _currentIndex = parentIndex;
                ReconstructState();
            }
        }
        await Task.CompletedTask;
    }
    
    public async Task JumpToAsync(DateTime timestamp)
    {
        // Binary search for timestamp
        var index = _events.BinarySearch(
            new ReplayableEvent 
            { 
                Id = "", 
                CorrelationId = "", 
                Type = EventType.StateSnapshot, 
                Timestamp = timestamp, 
                Data = null! 
            },
            Comparer<ReplayableEvent>.Create((a, b) => a.Timestamp.CompareTo(b.Timestamp))
        );
        
        if (index < 0) index = ~index; // Get insertion point
        _currentIndex = Math.Clamp(index, 0, _events.Count - 1);
        ReconstructState();
        await Task.CompletedTask;
    }
    
    private void ReconstructState()
    {
        if (_currentIndex >= _events.Count) return;
        
        var currentEvent = _events[_currentIndex];
        _currentState = currentEvent.Data;
        
        // Reconstruct variables and call stack from snapshots
        if (currentEvent.Type == EventType.StateSnapshot && currentEvent.Data is StateSnapshot snapshot)
        {
            _variables = new Dictionary<string, object?>(snapshot.Variables);
            _callStack = new List<CallFrame>(snapshot.CallStack);
        }
    }
}

