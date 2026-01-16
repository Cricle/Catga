using Catga.Flow.Dsl;
using MemoryPack;
using OrderSystem.Models;

namespace OrderSystem.Flows;

/// <summary>
/// State for the order fulfillment flow.
/// Demonstrates Flow DSL state management with recovery and distributed execution.
/// </summary>
[MemoryPackable]
public partial class OrderFulfillmentState : IFlowState
{
    public string? FlowId { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public List<OrderItem> Items { get; set; } = [];
    public decimal Total { get; set; }
    public bool IsValidated { get; set; }
    public bool IsPaymentProcessed { get; set; }
    public bool IsShipped { get; set; }
    public string? TrackingNumber { get; set; }
    public DateTime? CompletedAt { get; set; }

    // IFlowState implementation (simplified - no change tracking for demo)
    [MemoryPackIgnore]
    public bool HasChanges => false;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() => [];
}
