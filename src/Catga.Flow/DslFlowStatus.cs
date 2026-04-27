namespace Catga.Flow.Dsl;

/// <summary>
/// DSL Flow execution status.
/// </summary>
public enum DslFlowStatus : byte
{
    Pending = 0,
    Running = 1,
    Suspended = 2,
    Compensating = 3,
    Completed = 4,
    Failed = 5,
    Cancelled = 6
}
