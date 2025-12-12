using Catga.Flow.Dsl;

namespace Catga.Tests.Flow.TDD;

/// <summary>
/// Base class for test states implementing IFlowState.
/// Provides common IFlowState implementation to avoid duplication.
/// </summary>
public abstract class TestStateBase : IFlowState
{
    public string? FlowId { get; set; }
    public bool HasChanges { get; set; }

    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
}
