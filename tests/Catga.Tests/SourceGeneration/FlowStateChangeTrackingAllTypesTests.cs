using Catga.Flow.Dsl;
using OrderSystem.Api;
using OrderSystem.Api.Flows;
using OrderSystem.Api.Messages;
using Xunit;

namespace Catga.Tests.SourceGeneration;

/// <summary>
/// Comprehensive tests for all FlowState types' change-tracking functionality.
/// Verifies that source-generated change-tracking works correctly for:
/// - CreateOrderFlowState (3 fields)
/// - PaymentFlowState (2 fields)
/// - ShippingFlowState (3 fields)
/// - InventoryFlowState (2 fields)
/// - CustomerFlowState (1 field)
/// - OrderFlowState (26 fields)
/// </summary>
public class FlowStateChangeTrackingAllTypesTests
{
    #region CreateOrderFlowState Tests

    [Fact]
    public void CreateOrderFlowState_NoChanges_HasChangesFalse()
    {
        var state = new CreateOrderFlowState();
        state.ClearChanges();

        Assert.False(state.HasChanges);
    }

    [Fact]
    public void CreateOrderFlowState_SetOrderId_TracksChange()
    {
        var state = new CreateOrderFlowState();
        state.ClearChanges();

        state.OrderId = "order-123";

        Assert.True(state.HasChanges);
        Assert.Contains("OrderId", state.GetChangedFieldNames());
    }

    [Fact]
    public void CreateOrderFlowState_SetMultipleFields_TracksAll()
    {
        var state = new CreateOrderFlowState();
        state.ClearChanges();

        state.OrderId = "order-123";
        state.TotalAmount = 100m;
        state.StockReserved = true;

        var changedFields = state.GetChangedFieldNames().ToList();
        Assert.Contains("OrderId", changedFields);
        Assert.Contains("TotalAmount", changedFields);
        Assert.Contains("StockReserved", changedFields);
    }

    #endregion

    #region PaymentFlowState Tests

    [Fact]
    public void PaymentFlowState_SetPaymentId_TracksChange()
    {
        var state = new PaymentFlowState();
        state.ClearChanges();

        state.PaymentId = "pay-456";

        Assert.True(state.HasChanges);
        Assert.Contains("PaymentId", state.GetChangedFieldNames());
    }

    [Fact]
    public void PaymentFlowState_SetAmount_TracksChange()
    {
        var state = new PaymentFlowState();
        state.ClearChanges();

        state.Amount = 250.50m;

        Assert.True(state.HasChanges);
        Assert.Contains("Amount", state.GetChangedFieldNames());
    }

    #endregion

    #region ShippingFlowState Tests

    [Fact]
    public void ShippingFlowState_SetShipmentId_TracksChange()
    {
        var state = new ShippingFlowState();
        state.ClearChanges();

        state.ShipmentId = "ship-789";

        Assert.True(state.HasChanges);
        Assert.Contains("ShipmentId", state.GetChangedFieldNames());
    }

    [Fact]
    public void ShippingFlowState_SetSelectedCarrier_TracksChange()
    {
        var state = new ShippingFlowState();
        state.ClearChanges();

        state.SelectedCarrier = "FedEx";

        Assert.True(state.HasChanges);
        Assert.Contains("SelectedCarrier", state.GetChangedFieldNames());
    }

    #endregion

    #region InventoryFlowState Tests

    [Fact]
    public void InventoryFlowState_SetProducts_TracksChange()
    {
        var state = new InventoryFlowState();
        state.ClearChanges();

        state.Products = new List<Product> { new() { Id = "p1", Name = "Product 1" } };

        Assert.True(state.HasChanges);
        Assert.Contains("Products", state.GetChangedFieldNames());
    }

    [Fact]
    public void InventoryFlowState_SetTotalQuantity_TracksChange()
    {
        var state = new InventoryFlowState();
        state.ClearChanges();

        state.TotalQuantity = 100;

        Assert.True(state.HasChanges);
        Assert.Contains("TotalQuantity", state.GetChangedFieldNames());
    }

    #endregion

    #region CustomerFlowState Tests

    [Fact]
    public void CustomerFlowState_SetCustomerId_TracksChange()
    {
        var state = new CustomerFlowState();
        state.ClearChanges();

        state.CustomerId = "cust-101";

        Assert.True(state.HasChanges);
        Assert.Contains("CustomerId", state.GetChangedFieldNames());
    }

    [Fact]
    public void CustomerFlowState_SameValueSet_DoesNotMarkChanged()
    {
        var state = new CustomerFlowState { CustomerId = "cust-101" };
        state.ClearChanges();

        state.CustomerId = "cust-101";

        Assert.False(state.HasChanges);
    }

    #endregion

    #region OrderFlowState Tests

    [Fact]
    public void OrderFlowState_SetOrderId_TracksChange()
    {
        var state = new OrderFlowState();
        state.ClearChanges();

        state.OrderId = "order-999";

        Assert.True(state.HasChanges);
        Assert.Contains("OrderId", state.GetChangedFieldNames());
    }

    [Fact]
    public void OrderFlowState_SetMultipleFields_TracksAll()
    {
        var state = new OrderFlowState();
        state.ClearChanges();

        state.OrderId = "order-999";
        state.RequiresApproval = true;
        state.IsApproved = false;
        state.FraudScore = 0.5;

        var changedFields = state.GetChangedFieldNames().ToList();
        Assert.Contains("OrderId", changedFields);
        Assert.Contains("RequiresApproval", changedFields);
        Assert.Contains("IsApproved", changedFields);
        Assert.Contains("FraudScore", changedFields);
    }

    [Fact]
    public void OrderFlowState_ClearChanges_ResetsTracking()
    {
        var state = new OrderFlowState();
        state.OrderId = "order-999";
        Assert.True(state.HasChanges);

        state.ClearChanges();

        Assert.False(state.HasChanges);
        Assert.Empty(state.GetChangedFieldNames());
    }

    [Fact]
    public void OrderFlowState_MarkChanged_ManuallyTracksField()
    {
        var state = new OrderFlowState();
        state.ClearChanges();

        state.MarkChanged(0); // Mark first field as changed

        Assert.True(state.HasChanges);
    }

    #endregion

    #region Cross-FlowState Tests

    [Fact]
    public void AllFlowStates_ImplementIFlowState()
    {
        var states = new IFlowState[]
        {
            new CreateOrderFlowState(),
            new PaymentFlowState(),
            new ShippingFlowState(),
            new InventoryFlowState(),
            new CustomerFlowState(),
            new OrderFlowState()
        };

        foreach (var state in states)
        {
            Assert.NotNull(state.FlowId);
            Assert.False(state.HasChanges);
            Assert.Equal(0, state.GetChangedMask());
        }
    }

    [Fact]
    public void AllFlowStates_GetChangedFieldNames_ReturnsEnumerable()
    {
        var states = new IFlowState[]
        {
            new CreateOrderFlowState(),
            new PaymentFlowState(),
            new ShippingFlowState(),
            new InventoryFlowState(),
            new CustomerFlowState(),
            new OrderFlowState()
        };

        foreach (var state in states)
        {
            var changedFields = state.GetChangedFieldNames();
            Assert.NotNull(changedFields);
            Assert.IsAssignableFrom<IEnumerable<string>>(changedFields);
        }
    }

    #endregion
}
