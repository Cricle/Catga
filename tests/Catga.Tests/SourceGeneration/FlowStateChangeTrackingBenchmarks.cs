using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Catga.Flow.Dsl;
using OrderSystem.Api;
using OrderSystem.Api.Flows;
using OrderSystem.Api.Messages;
using Xunit;

namespace Catga.Tests.SourceGeneration;

/// <summary>
/// Performance benchmarks for FlowState change-tracking.
/// Compares source-generated change-tracking vs manual implementation.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, targetCount: 5)]
public class FlowStateChangeTrackingBenchmarks
{
    private CreateOrderFlowState _createOrderState = null!;
    private OrderFlowState _orderState = null!;
    private PaymentFlowState _paymentState = null!;
    private ShippingFlowState _shippingState = null!;
    private InventoryFlowState _inventoryState = null!;
    private CustomerFlowState _customerState = null!;

    [GlobalSetup]
    public void Setup()
    {
        _createOrderState = new CreateOrderFlowState();
        _orderState = new OrderFlowState();
        _paymentState = new PaymentFlowState();
        _shippingState = new ShippingFlowState();
        _inventoryState = new InventoryFlowState();
        _customerState = new CustomerFlowState();
    }

    #region CreateOrderFlowState Benchmarks

    [Benchmark]
    public void CreateOrderFlowState_SetSingleField()
    {
        _createOrderState.ClearChanges();
        _createOrderState.OrderId = "order-123";
    }

    [Benchmark]
    public void CreateOrderFlowState_SetMultipleFields()
    {
        _createOrderState.ClearChanges();
        _createOrderState.OrderId = "order-123";
        _createOrderState.TotalAmount = 500m;
        _createOrderState.StockReserved = true;
    }

    [Benchmark]
    public void CreateOrderFlowState_GetChangedFields()
    {
        _createOrderState.ClearChanges();
        _createOrderState.OrderId = "order-123";
        var changedFields = _createOrderState.GetChangedFieldNames().ToList();
    }

    [Benchmark]
    public void CreateOrderFlowState_CheckHasChanges()
    {
        _createOrderState.ClearChanges();
        _createOrderState.OrderId = "order-123";
        var hasChanges = _createOrderState.HasChanges;
    }

    #endregion

    #region OrderFlowState Benchmarks (26 fields)

    [Benchmark]
    public void OrderFlowState_SetSingleField()
    {
        _orderState.ClearChanges();
        _orderState.OrderId = "order-999";
    }

    [Benchmark]
    public void OrderFlowState_SetMultipleFields()
    {
        _orderState.ClearChanges();
        _orderState.OrderId = "order-999";
        _orderState.RequiresApproval = true;
        _orderState.IsApproved = false;
        _orderState.FraudScore = 0.5;
        _orderState.PaymentProcessed = true;
        _orderState.EmailSent = true;
    }

    [Benchmark]
    public void OrderFlowState_GetChangedFieldNames()
    {
        _orderState.ClearChanges();
        _orderState.OrderId = "order-999";
        _orderState.RequiresApproval = true;
        _orderState.IsApproved = false;
        var changedFields = _orderState.GetChangedFieldNames().ToList();
    }

    [Benchmark]
    public void OrderFlowState_GetChangedMask()
    {
        _orderState.ClearChanges();
        _orderState.OrderId = "order-999";
        _orderState.RequiresApproval = true;
        var mask = _orderState.GetChangedMask();
    }

    [Benchmark]
    public void OrderFlowState_ClearChanges()
    {
        _orderState.OrderId = "order-999";
        _orderState.RequiresApproval = true;
        _orderState.ClearChanges();
    }

    #endregion

    #region PaymentFlowState Benchmarks

    [Benchmark]
    public void PaymentFlowState_SetFields()
    {
        _paymentState.ClearChanges();
        _paymentState.PaymentId = "pay-001";
        _paymentState.Amount = 1000m;
    }

    [Benchmark]
    public void PaymentFlowState_CheckChanges()
    {
        _paymentState.ClearChanges();
        _paymentState.PaymentId = "pay-001";
        var hasChanges = _paymentState.HasChanges;
        var changedFields = _paymentState.GetChangedFieldNames().ToList();
    }

    #endregion

    #region ShippingFlowState Benchmarks

    [Benchmark]
    public void ShippingFlowState_SetFields()
    {
        _shippingState.ClearChanges();
        _shippingState.ShipmentId = "ship-001";
        _shippingState.SelectedCarrier = "FedEx";
    }

    [Benchmark]
    public void ShippingFlowState_GetChangedFieldNames()
    {
        _shippingState.ClearChanges();
        _shippingState.ShipmentId = "ship-001";
        _shippingState.SelectedCarrier = "FedEx";
        var changedFields = _shippingState.GetChangedFieldNames().ToList();
    }

    #endregion

    #region InventoryFlowState Benchmarks

    [Benchmark]
    public void InventoryFlowState_SetFields()
    {
        _inventoryState.ClearChanges();
        _inventoryState.Products = new List<Product> { new() { Id = "p1", Name = "Product 1" } };
        _inventoryState.TotalQuantity = 100;
    }

    [Benchmark]
    public void InventoryFlowState_CheckChanges()
    {
        _inventoryState.ClearChanges();
        _inventoryState.TotalQuantity = 100;
        var hasChanges = _inventoryState.HasChanges;
    }

    #endregion

    #region CustomerFlowState Benchmarks

    [Benchmark]
    public void CustomerFlowState_SetField()
    {
        _customerState.ClearChanges();
        _customerState.CustomerId = "cust-001";
    }

    [Benchmark]
    public void CustomerFlowState_CheckChanges()
    {
        _customerState.ClearChanges();
        _customerState.CustomerId = "cust-001";
        var hasChanges = _customerState.HasChanges;
        var changedFields = _customerState.GetChangedFieldNames().ToList();
    }

    #endregion

    #region Bulk Operations Benchmarks

    [Benchmark]
    public void BulkOperation_CreateOrder_FullWorkflow()
    {
        var state = new CreateOrderFlowState();
        state.OrderId = "order-123";
        state.TotalAmount = 500m;
        state.StockReserved = true;

        var changedFields = state.GetChangedFieldNames().ToList();
        state.ClearChanges();

        state.OrderId = "order-456";
        var hasChanges = state.HasChanges;
    }

    [Benchmark]
    public void BulkOperation_Order_FullWorkflow()
    {
        var state = new OrderFlowState();
        state.OrderId = "order-999";
        state.RequiresApproval = true;
        state.IsApproved = false;
        state.FraudScore = 0.5;
        state.PaymentProcessed = true;
        state.EmailSent = true;

        var changedFields = state.GetChangedFieldNames().ToList();
        var mask = state.GetChangedMask();
        state.ClearChanges();

        state.OrderId = "order-888";
        var hasChanges = state.HasChanges;
    }

    [Benchmark]
    public void BulkOperation_AllFlowStates_SetAndCheck()
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
            var hasChanges = state.HasChanges;
            var changedFields = state.GetChangedFieldNames().ToList();
            state.ClearChanges();
        }
    }

    #endregion
}

/// <summary>
/// Unit test to run benchmarks
/// </summary>
public class FlowStateChangeTrackingBenchmarkTests
{
    [Fact]
    public void RunBenchmarks()
    {
        var summary = BenchmarkRunner.Run<FlowStateChangeTrackingBenchmarks>();
        Assert.NotNull(summary);
    }
}
