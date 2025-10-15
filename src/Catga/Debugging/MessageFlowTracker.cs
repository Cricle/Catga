using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.ObjectPool;

namespace Catga.Debugging;

/// <summary>
/// Tracks message flows for debugging - memory-efficient with object pooling
/// Zero-allocation design with aggressive cleanup to prevent memory leaks
/// </summary>
public sealed class MessageFlowTracker : IDisposable
{
    private readonly ConcurrentDictionary<string, FlowContext> _activeFlows;
    private readonly ObjectPool<FlowContext> _contextPool;
    private readonly ObjectPool<List<StepInfo>> _stepListPool;
    private readonly DebugOptions _options;
    private readonly Timer _cleanupTimer;
    private long _totalFlows;
    private long _activeCount;

    public MessageFlowTracker(DebugOptions options)
    {
        _options = options;
        
        // Pre-size dictionary to reduce resizing (better GC)
        _activeFlows = new ConcurrentDictionary<string, FlowContext>(
            concurrencyLevel: Environment.ProcessorCount,
            capacity: Math.Min(_options.MaxActiveFlows, 100));
        
        _contextPool = new DefaultObjectPool<FlowContext>(
            new FlowContextPoolPolicy(), 
            _options.MaxActiveFlows);
        
        _stepListPool = new DefaultObjectPool<List<StepInfo>>(
            new StepListPoolPolicy(), 
            _options.MaxActiveFlows);

        // Start cleanup timer - runs on ThreadPool, minimal overhead
        _cleanupTimer = new Timer(CleanupExpiredFlows, null, _options.FlowTTL, _options.FlowTTL);
    }

    /// <summary>
    /// Begin tracking a message flow
    /// </summary>
    public FlowContext BeginFlow(string correlationId, string messageType)
    {
        // Check limit
        var currentActive = Interlocked.Read(ref _activeCount);
        if (currentActive >= _options.MaxActiveFlows)
        {
            EvictOldestFlow();
        }

        var context = _contextPool.Get();
        context.CorrelationId = correlationId;
        context.TraceId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        context.MessageType = messageType;
        context.StartTime = DateTime.UtcNow;
        context.Steps = _stepListPool.Get();

        _activeFlows[correlationId] = context;
        Interlocked.Increment(ref _activeCount);
        Interlocked.Increment(ref _totalFlows);

        return context;
    }

    /// <summary>
    /// Record a step in the flow
    /// </summary>
    public void RecordStep(string correlationId, StepInfo step)
    {
        if (_activeFlows.TryGetValue(correlationId, out var context))
        {
            if (context.Steps.Count < _options.MaxStepsPerFlow)
            {
                context.Steps.Add(step);
            }
        }
    }

    /// <summary>
    /// End tracking and return context to pool
    /// </summary>
    public FlowSummary EndFlow(string correlationId)
    {
        if (_activeFlows.TryRemove(correlationId, out var context))
        {
            Interlocked.Decrement(ref _activeCount);

            var summary = new FlowSummary(context);

            // Return to pools
            _stepListPool.Return(context.Steps);
            context.Reset();
            _contextPool.Return(context);

            return summary;
        }

        return FlowSummary.Empty;
    }

    /// <summary>
    /// Get active flow (read-only)
    /// </summary>
    public FlowSummary? GetFlow(string correlationId)
    {
        return _activeFlows.TryGetValue(correlationId, out var context)
            ? new FlowSummary(context)
            : null;
    }

    /// <summary>
    /// Get all active flows
    /// </summary>
    public IReadOnlyList<FlowSummary> GetActiveFlows()
    {
        return _activeFlows.Values.Select(c => new FlowSummary(c)).ToList();
    }

    /// <summary>
    /// Get statistics
    /// </summary>
    public DebugStatistics GetStatistics()
    {
        return new DebugStatistics
        {
            TotalFlows = Interlocked.Read(ref _totalFlows),
            ActiveFlows = Interlocked.Read(ref _activeCount),
            PooledContexts = _contextPool.ToString(),  // Pool stats if available
            MemoryEstimate = Interlocked.Read(ref _activeCount) * 1024 // ~1KB per flow
        };
    }

    private void EvictOldestFlow()
    {
        // Fast path: find oldest without LINQ allocation
        FlowContext? oldest = null;
        DateTime oldestTime = DateTime.MaxValue;

        foreach (var kvp in _activeFlows)
        {
            if (kvp.Value.StartTime < oldestTime)
            {
                oldestTime = kvp.Value.StartTime;
                oldest = kvp.Value;
            }
        }

        if (oldest != null)
        {
            EndFlow(oldest.CorrelationId);
        }
    }

    /// <summary>
    /// Cleanup expired flows - runs on timer, minimal GC pressure
    /// </summary>
    private void CleanupExpiredFlows(object? state)
    {
        var now = DateTime.UtcNow;
        var expiredKeys = new List<string>(capacity: 16);  // Pre-sized

        // Collect expired keys
        foreach (var kvp in _activeFlows)
        {
            if (now - kvp.Value.StartTime > _options.FlowTTL)
            {
                expiredKeys.Add(kvp.Key);
                
                if (expiredKeys.Count >= 100)  // Limit per cleanup
                    break;
            }
        }

        // Remove expired flows
        foreach (var key in expiredKeys)
        {
            EndFlow(key);
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        
        // Return all contexts to pool
        foreach (var kvp in _activeFlows)
        {
            EndFlow(kvp.Key);
        }
    }
}

/// <summary>
/// Flow context - pooled and reusable
/// </summary>
public sealed class FlowContext
{
    public string CorrelationId { get; set; } = string.Empty;
    public string TraceId { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public List<StepInfo> Steps { get; set; } = new();

    public void Reset()
    {
        CorrelationId = string.Empty;
        TraceId = string.Empty;
        MessageType = string.Empty;
        Steps.Clear();
    }
}

/// <summary>
/// Step information - value type to avoid allocations
/// </summary>
public readonly struct StepInfo
{
    public string Type { get; init; }          // Command, Event, Query, etc.
    public string Name { get; init; }          // Handler name
    public TimeSpan Duration { get; init; }
    public bool Success { get; init; }
    public string? Error { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }  // Transport-specific

    public StepInfo(string type, string name, TimeSpan duration, bool success = true, string? error = null)
    {
        Type = type;
        Name = name;
        Duration = duration;
        Success = success;
        Error = error;
        Metadata = null;
    }
}

/// <summary>
/// Flow summary - snapshot for external consumption
/// </summary>
public readonly record struct FlowSummary
{
    public string CorrelationId { get; init; }
    public string TraceId { get; init; }
    public string MessageType { get; init; }
    public DateTime StartTime { get; init; }
    public TimeSpan TotalDuration { get; init; }
    public int StepCount { get; init; }
    public bool Success { get; init; }

    public FlowSummary(FlowContext context)
    {
        CorrelationId = context.CorrelationId;
        TraceId = context.TraceId;
        MessageType = context.MessageType;
        StartTime = context.StartTime;
        TotalDuration = DateTime.UtcNow - context.StartTime;
        StepCount = context.Steps.Count;
        Success = context.Steps.All(s => s.Success);
    }

    public static FlowSummary Empty => default;
}

/// <summary>
/// Debug statistics
/// </summary>
public readonly record struct DebugStatistics
{
    public long TotalFlows { get; init; }
    public long ActiveFlows { get; init; }
    public string? PooledContexts { get; init; }
    public long MemoryEstimate { get; init; }
}

/// <summary>
/// Debug options - memory and performance tuning
/// </summary>
public sealed class DebugOptions
{
    public bool EnableDebug { get; set; } = false;
    public int MaxActiveFlows { get; set; } = 1000;
    public TimeSpan FlowTTL { get; set; } = TimeSpan.FromMinutes(5);
    public int MaxStepsPerFlow { get; set; } = 100;
    public bool EnableConsoleOutput { get; set; } = true;
    public bool EnableApiEndpoints { get; set; } = false;
    public bool IncludeTransportMetadata { get; set; } = true;
}

/// <summary>
/// Object pool policy for FlowContext
/// </summary>
internal sealed class FlowContextPoolPolicy : IPooledObjectPolicy<FlowContext>
{
    public FlowContext Create() => new FlowContext();

    public bool Return(FlowContext obj)
    {
        obj.Reset();
        return true;
    }
}

/// <summary>
/// Object pool policy for Step lists
/// </summary>
internal sealed class StepListPoolPolicy : IPooledObjectPolicy<List<StepInfo>>
{
    public List<StepInfo> Create() => new List<StepInfo>(capacity: 16);

    public bool Return(List<StepInfo> obj)
    {
        obj.Clear();
        return obj.Capacity <= 100;  // Don't pool oversized lists
    }
}

