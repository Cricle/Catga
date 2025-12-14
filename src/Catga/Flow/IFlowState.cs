namespace Catga.Flow.Dsl;

/// <summary>
/// Interface for flow state with change tracking.
/// Implemented by source generator for [FlowState] attributed classes.
/// </summary>
public interface IFlowState
{
    /// <summary>Flow instance ID (infrastructure, not tracked).</summary>
    string? FlowId { get; set; }

    /// <summary>Whether any field has changed since last ClearChanges().</summary>
    bool HasChanges { get; }

    /// <summary>Get bitmask of changed fields.</summary>
    int GetChangedMask();

    /// <summary>Check if specific field has changed.</summary>
    bool IsFieldChanged(int fieldIndex);

    /// <summary>Clear all change flags.</summary>
    void ClearChanges();

    /// <summary>Mark a field as changed.</summary>
    void MarkChanged(int fieldIndex);

    /// <summary>Get names of changed fields.</summary>
    IEnumerable<string> GetChangedFieldNames();
}
