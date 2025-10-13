namespace Catga.Transaction;

/// <summary>Distributed transaction status</summary>
public enum TransactionStatus
{
    /// <summary>Transaction is pending execution</summary>
    Pending = 0,

    /// <summary>Transaction is running</summary>
    Running = 1,

    /// <summary>Transaction completed successfully</summary>
    Completed = 2,

    /// <summary>Transaction is compensating (rolling back)</summary>
    Compensating = 3,

    /// <summary>Transaction was compensated (rolled back)</summary>
    Compensated = 4,

    /// <summary>Transaction failed permanently</summary>
    Failed = 5,

    /// <summary>Transaction timed out</summary>
    TimedOut = 6
}

/// <summary>Transaction execution snapshot - immutable state record</summary>
public sealed class TransactionSnapshot
{
    public required string TransactionId { get; init; }
    public required string TransactionName { get; init; }
    public TransactionStatus Status { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public int CurrentStep { get; init; }
    public int TotalSteps { get; init; }
    public string? Error { get; init; }
    public List<TransactionEvent> Events { get; init; } = new();
}

/// <summary>Transaction event - immutable event record</summary>
public sealed class TransactionEvent
{
    public required string EventType { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string Data { get; init; }
    public int StepIndex { get; init; }
}

