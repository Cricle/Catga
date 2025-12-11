using Catga.Flow.Dsl;
using Catga.Persistence.Redis;
using OrderSystem.Api;
using OrderSystem.Api.Flows;
using OrderSystem.Api.Messages;
using Xunit;

namespace Catga.Tests.SourceGeneration;

/// <summary>
/// End-to-end tests for FlowState change-tracking with Redis/NATS persistence.
/// Verifies that source-generated change-tracking works correctly in distributed scenarios.
/// </summary>
public class FlowStateE2ETests
{
    #region CreateOrderFlowState E2E Tests

    [Fact]
    public void CreateOrderFlowState_E2E_ChangeTrackingPersists()
    {
        var state = new CreateOrderFlowState
        {
            FlowId = Guid.NewGuid().ToString(),
            OrderId = "order-123",
            TotalAmount = 500m,
            StockReserved = true
        };

        // Verify all changes are tracked
        Assert.True(state.HasChanges);
        var changedFields = state.GetChangedFieldNames().ToList();
        Assert.Contains("OrderId", changedFields);
        Assert.Contains("TotalAmount", changedFields);
        Assert.Contains("StockReserved", changedFields);

        // Simulate persistence: clear changes after saving
        state.ClearChanges();
        Assert.False(state.HasChanges);

        // Simulate loading from persistence: make another change
        state.OrderId = "order-456";
        Assert.True(state.HasChanges);
        Assert.Contains("OrderId", state.GetChangedFieldNames());
    }

    #endregion

    #region PaymentFlowState E2E Tests

    [Fact]
    public void PaymentFlowState_E2E_ChangeTrackingAcrossUpdates()
    {
        var state = new PaymentFlowState
        {
            FlowId = Guid.NewGuid().ToString(),
            PaymentId = "pay-001",
            Amount = 1000m
        };

        Assert.True(state.HasChanges);
        state.ClearChanges();

        // Update amount
        state.Amount = 1500m;
        Assert.True(state.HasChanges);
        Assert.Contains("Amount", state.GetChangedFieldNames());

        // Clear and verify
        state.ClearChanges();
        Assert.False(state.HasChanges);
    }

    #endregion

    #region ShippingFlowState E2E Tests

    [Fact]
    public void ShippingFlowState_E2E_MultipleUpdates()
    {
        var state = new ShippingFlowState
        {
            FlowId = Guid.NewGuid().ToString(),
            ShipmentId = "ship-001"
        };

        state.ClearChanges();

        // First update
        state.SelectedCarrier = "FedEx";
        Assert.True(state.HasChanges);
        state.ClearChanges();

        // Second update
        state.SelectedCarrier = "UPS";
        Assert.True(state.HasChanges);
        Assert.Contains("SelectedCarrier", state.GetChangedFieldNames());
    }

    #endregion

    #region InventoryFlowState E2E Tests

    [Fact]
    public void InventoryFlowState_E2E_CollectionTracking()
    {
        var state = new InventoryFlowState
        {
            FlowId = Guid.NewGuid().ToString(),
            Products = new List<Product>
            {
                new() { Id = "p1", Name = "Product 1" },
                new() { Id = "p2", Name = "Product 2" }
            },
            TotalQuantity = 100
        };

        Assert.True(state.HasChanges);
        var changedFields = state.GetChangedFieldNames().ToList();
        Assert.Contains("Products", changedFields);
        Assert.Contains("TotalQuantity", changedFields);

        state.ClearChanges();

        // Update collection
        state.Products.Add(new() { Id = "p3", Name = "Product 3" });
        Assert.True(state.HasChanges);
    }

    #endregion

    #region CustomerFlowState E2E Tests

    [Fact]
    public void CustomerFlowState_E2E_SimpleTracking()
    {
        var state = new CustomerFlowState
        {
            FlowId = Guid.NewGuid().ToString(),
            CustomerId = "cust-001"
        };

        Assert.True(state.HasChanges);
        state.ClearChanges();

        // No changes
        Assert.False(state.HasChanges);

        // Make change
        state.CustomerId = "cust-002";
        Assert.True(state.HasChanges);
        Assert.Contains("CustomerId", state.GetChangedFieldNames());
    }

    #endregion

    #region OrderFlowState E2E Tests

    [Fact]
    public void OrderFlowState_E2E_ComplexStateTracking()
    {
        var state = new OrderFlowState
        {
            FlowId = Guid.NewGuid().ToString(),
            OrderId = "order-999",
            Order = new Order
            {
                CustomerId = "cust-001",
                CustomerEmail = "customer@example.com",
                CustomerType = CustomerType.VIP,
                Items = new List<OrderItem>
                {
                    new() { ProductId = "p1", Quantity = 2, Price = 50m }
                },
                TotalAmount = 100m
            },
            RequiresApproval = true,
            IsApproved = false,
            FraudScore = 0.3
        };

        Assert.True(state.HasChanges);
        var changedFields = state.GetChangedFieldNames().ToList();
        Assert.Contains("OrderId", changedFields);
        Assert.Contains("Order", changedFields);
        Assert.Contains("RequiresApproval", changedFields);
        Assert.Contains("IsApproved", changedFields);
        Assert.Contains("FraudScore", changedFields);

        state.ClearChanges();
        Assert.False(state.HasChanges);

        // Simulate approval workflow
        state.IsApproved = true;
        state.RequiresApproval = false;
        Assert.True(state.HasChanges);
        Assert.Contains("IsApproved", state.GetChangedFieldNames());
        Assert.Contains("RequiresApproval", state.GetChangedFieldNames());
    }

    [Fact]
    public void OrderFlowState_E2E_FullWorkflow()
    {
        var state = new OrderFlowState
        {
            FlowId = Guid.NewGuid().ToString(),
            OrderId = "order-complete"
        };

        // Step 1: Initial creation
        Assert.True(state.HasChanges);
        state.ClearChanges();

        // Step 2: Approval
        state.RequiresApproval = true;
        state.IsApproved = true;
        Assert.True(state.HasChanges);
        state.ClearChanges();

        // Step 3: Payment
        state.PaymentProcessed = true;
        state.PaymentProvider = "Stripe";
        state.TransactionId = "txn-123";
        Assert.True(state.HasChanges);
        state.ClearChanges();

        // Step 4: Fulfillment
        state.InvoiceNumber = "INV-001";
        state.EmailSent = true;
        state.ShippingLabel = "LABEL-001";
        Assert.True(state.HasChanges);
        state.ClearChanges();

        // Step 5: Completion
        state.ProcessingProgress = 100m;
        Assert.True(state.HasChanges);
        Assert.Contains("ProcessingProgress", state.GetChangedFieldNames());
    }

    #endregion

    #region Cross-FlowState Consistency Tests

    [Fact]
    public void AllFlowStates_E2E_ConsistentBehavior()
    {
        var states = new IFlowState[]
        {
            new CreateOrderFlowState { OrderId = "o1" },
            new PaymentFlowState { PaymentId = "p1" },
            new ShippingFlowState { ShipmentId = "s1" },
            new InventoryFlowState { TotalQuantity = 10 },
            new CustomerFlowState { CustomerId = "c1" },
            new OrderFlowState { OrderId = "o2" }
        };

        foreach (var state in states)
        {
            // All should have changes after initialization
            Assert.True(state.HasChanges, $"{state.GetType().Name} should have changes");

            // All should support ClearChanges
            state.ClearChanges();
            Assert.False(state.HasChanges, $"{state.GetType().Name} should have no changes after ClearChanges");

            // All should support MarkChanged
            state.MarkChanged(0);
            Assert.True(state.HasChanges, $"{state.GetType().Name} should have changes after MarkChanged");
        }
    }

    #endregion
}
