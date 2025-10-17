using System.Collections.Concurrent;
using Catga.Debugger.Models;

namespace Catga.Debugger.Replay;

/// <summary>
/// Manages active replay sessions for time-travel debugging
/// Stores replay state across multiple API calls
/// </summary>
public sealed class ReplaySessionManager
{
    private readonly ConcurrentDictionary<string, FlowReplay> _flowReplays = new();
    private readonly ConcurrentDictionary<string, SystemReplay> _systemReplays = new();

    /// <summary>Start or get existing flow replay session</summary>
    public FlowReplay GetOrCreateFlowReplay(string correlationId, Func<FlowReplay> factory)
    {
        return _flowReplays.GetOrAdd(correlationId, _ => factory());
    }

    /// <summary>Get existing flow replay session</summary>
    public FlowReplay? GetFlowReplay(string correlationId)
    {
        return _flowReplays.TryGetValue(correlationId, out var replay) ? replay : null;
    }

    /// <summary>Remove flow replay session</summary>
    public void RemoveFlowReplay(string correlationId)
    {
        _flowReplays.TryRemove(correlationId, out _);
    }

    /// <summary>Start or get existing system replay session</summary>
    public SystemReplay GetOrCreateSystemReplay(string sessionId, Func<SystemReplay> factory)
    {
        return _systemReplays.GetOrAdd(sessionId, _ => factory());
    }

    /// <summary>Get existing system replay session</summary>
    public SystemReplay? GetSystemReplay(string sessionId)
    {
        return _systemReplays.TryGetValue(sessionId, out var replay) ? replay : null;
    }

    /// <summary>Remove system replay session</summary>
    public void RemoveSystemReplay(string sessionId)
    {
        _systemReplays.TryRemove(sessionId, out _);
    }

    /// <summary>Get all active replay sessions</summary>
    public (int flowCount, int systemCount) GetSessionCounts()
    {
        return (_flowReplays.Count, _systemReplays.Count);
    }

    /// <summary>Clear all sessions</summary>
    public void ClearAll()
    {
        _flowReplays.Clear();
        _systemReplays.Clear();
    }
}

