using Catga.Flow.Dsl;
using MemoryPack;
using OrderSystem.Models;

namespace OrderSystem.Flows;

/// <summary>
/// State for complex order processing flow.
/// Demonstrates advanced Flow DSL features: parallel execution, switch, foreach.
/// </summary>
[MemoryPackable]
public partial class ComplexOrderState : IFlowState
{
    public string? FlowId { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public List<OrderItem> Items { get; set; } = [];
    public decimal Total { get; set; }
    public OrderType Type { get; set; }
    public bool InventoryChecked { get; set; }
    public bool CreditChecked { get; set; }
    public int ProcessedItems { get; set; }
    public bool IsCompleted { get; set; }

    // IFlowState implementation (simplified - no change tracking for demo)
    [MemoryPackIgnore]
    public bool HasChanges => false;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() => [];
}

public enum OrderType
{
    Standard,
    Express,
    Bulk
}
