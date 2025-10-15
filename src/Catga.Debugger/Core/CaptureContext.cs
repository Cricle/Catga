using Catga.Debugger.Models;

namespace Catga.Debugger.Core;

/// <summary>Capture context for a single flow execution</summary>
public sealed class CaptureContext
{
    public CaptureContext(string correlationId)
    {
        CorrelationId = correlationId;
        Events = new List<ReplayableEvent>();
        StartTime = DateTime.UtcNow;
    }
    
    /// <summary>Correlation ID</summary>
    public string CorrelationId { get; }
    
    /// <summary>Captured events</summary>
    public List<ReplayableEvent> Events { get; }
    
    /// <summary>Capture start time</summary>
    public DateTime StartTime { get; }
    
    /// <summary>Service name</summary>
    public string? ServiceName { get; set; }
    
    /// <summary>Parent event ID for causality</summary>
    public string? ParentEventId { get; set; }
}

