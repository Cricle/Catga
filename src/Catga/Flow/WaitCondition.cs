using MemoryPack;

namespace Catga.Flow.Dsl;

/// <summary>
/// Wait condition for WhenAll/WhenAny.
/// </summary>
[MemoryPackable]
public partial record WaitCondition
{
    public required string CorrelationId { get; init; }
    public required WaitType Type { get; init; }
    public required int ExpectedCount { get; init; }
    public int CompletedCount { get; set; }
    public required TimeSpan Timeout { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required string FlowId { get; init; }
    public required string FlowType { get; init; }
    public required int Step { get; init; }
    public bool CancelOthers { get; init; }
    public List<string> ChildFlowIds { get; init; } = [];
    public List<FlowCompletedEventData> Results { get; init; } = [];
    public string? ScheduleId { get; init; }
}

/// <summary>
/// Wait type for WhenAll/WhenAny.
/// </summary>
public enum WaitType
{
    All,
    Any
}

/// <summary>
/// Flow completed event data for WhenAll/WhenAny coordination.
/// </summary>
[MemoryPackable]
public partial record FlowCompletedEventData
{
    public required string FlowId { get; init; }
    public string? ParentCorrelationId { get; init; }
    public bool Success { get; init; }
    public string? Error { get; init; }
    [MemoryPackIgnore]
    public object? Result { get; init; }
}
