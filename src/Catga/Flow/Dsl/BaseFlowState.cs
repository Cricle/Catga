using System;
using System.Collections.Generic;

namespace Catga.Flow.Dsl;

/// <summary>
/// Base class for all flow states, reducing boilerplate IFlowState implementation.
/// Provides default implementations of change tracking methods.
/// </summary>
public abstract class BaseFlowState : IFlowState
{
    /// <summary>
    /// Unique identifier for this flow instance.
    /// </summary>
    public string? FlowId { get; set; }

    /// <summary>
    /// Default implementation - always returns true.
    /// Override if you need specific change tracking logic.
    /// </summary>
    public virtual bool HasChanges => true;

    /// <summary>
    /// Default implementation - returns 0.
    /// Override if you need specific change tracking logic.
    /// </summary>
    public virtual int GetChangedMask() => 0;

    /// <summary>
    /// Default implementation - returns false.
    /// Override if you need specific change tracking logic.
    /// </summary>
    public virtual bool IsFieldChanged(int fieldIndex) => false;

    /// <summary>
    /// Default implementation - no-op.
    /// Override if you need specific change tracking logic.
    /// </summary>
    public virtual void ClearChanges() { }

    /// <summary>
    /// Default implementation - no-op.
    /// Override if you need specific change tracking logic.
    /// </summary>
    public virtual void MarkChanged(int fieldIndex) { }

    /// <summary>
    /// Default implementation - returns empty enumerable.
    /// Override if you need specific change tracking logic.
    /// </summary>
    public virtual IEnumerable<string> GetChangedFieldNames()
    {
        yield break;
    }
}
