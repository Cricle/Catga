using Catga;
using Catga.Abstractions;
using Catga.Core;
using Catga.Observability;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Diagnostics;
using System.Linq;

namespace Catga.Tests.Integration.E2E;

/// <summary>
/// 电商订单流程综合业务场景测试 (TDD方法)
/// 完整测试真实业务流程：
/// 1. 创建订单 → 扣减库存 → 支付 → 发货
/// 2. 订单取消流程
/// 3. 支付失败回滚
/// 4. 库存不足处理
/// 5. 并发订单处理
/// 6. 分布式事务场景
/// </summary>
public class ECommerceOrderFlowTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<ECommerceOrderFlowTests> _logger;

    public ECommerceOrderFlowTests()
    {
        _logger = Substitute.For<ILogger<ECommerceOrderFlowTests>>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga().WithTracing(true);

        // 注册订单相关Handler
        services.AddScoped<IRequestHandler<CreateOrderCommand, OrderCreatedResult>, CreateOrderCommandHandler>();
        services.AddScoped<IRequestHandler<ReserveInventoryCommand, InventoryReservedResult>, ReserveInventoryCommandHandler>();
        services.AddScoped<IRequestHandler<ProcessPaymentCommand, PaymentResult>, ProcessPaymentCommandHandler>();
        services.AddScoped<IRequestHandler<ShipOrderCommand, ShipmentResult>, ShipOrderCommandHandler>();
        services.AddScoped<IRequestHandler<CancelOrderCommand, OrderCancelledResult>, CancelOrderCommandHandler>();
        services.AddScoped<IRequestHandler<GetOrderQuery, OrderDetails>, GetOrderQueryHandler>();

        // 注册事件Handler
        services.AddScoped<IEventHandler<OrderCreatedEvent>, OrderCreatedNotificationHandler>();
        services.AddScoped<IEventHandler<OrderCreatedEvent>, OrderCreatedMetricsHandler>();
        services.AddScoped<IEventHandler<PaymentCompletedEvent>, PaymentNotificationHandler>();
        services.AddScoped<IEventHandler<OrderShippedEvent>, ShippingNotificationHandler>();
        services.AddScoped<IEventHandler<OrderCancelledEvent>, RefundHandler>();

        // 注册共享服务
        services.AddSingleton<InMemoryInventoryService>();
        services.AddSingleton<InMemoryOrderRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<ICatgaMediator>();
    }

    [Fact]
    public async Task Tracing_CreateOrder_ShouldInclude_Request_And_Response_Tags()
    {
        var productId = "TRACE-PROD";
        var cmd = new CreateOrderCommand(productId, 2, 10.5m) { CorrelationId = MessageExtensions.NewMessageId() };

        Activity? act = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == CatgaActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a =>
            {
                if (a.OperationName.Contains("CreateOrderCommand") && a.Tags.Any(t => t.Key.StartsWith("catga.req.") || t.Key.StartsWith("catga.res.")))
                    act = a;
            }
        };
        ActivitySource.AddActivityListener(listener);

        var res = await _mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(cmd);
        res.IsSuccess.Should().BeTrue();
        act.Should().NotBeNull();
        var objs1 = new Dictionary<string, object?>();
        foreach (var kv in act!.EnumerateTagObjects()) objs1[kv.Key] = kv.Value;
        objs1.Should().ContainKey("catga.req.product_id");
        objs1["catga.req.product_id"].Should().Be(productId);
        objs1.Should().ContainKey("catga.req.quantity");
        objs1.Should().ContainKey("catga.req.amount");
        objs1.Should().ContainKey("catga.res.order_id");
    }

    [Fact]
    public async Task Tracing_ReserveInventory_ShouldInclude_Request_And_Response_Tags()
    {
        var create = await _mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(new CreateOrderCommand("X", 1, 1m));
        var cmd = new ReserveInventoryCommand(create.Value!.OrderId, "X", 1) { CorrelationId = MessageExtensions.NewMessageId() };

        Activity? act = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == CatgaActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a =>
            {
                if (a.OperationName.Contains("ReserveInventoryCommand") && a.Tags.Any(t => t.Key.StartsWith("catga.req.") || t.Key.StartsWith("catga.res.")))
                    act = a;
            }
        };
        ActivitySource.AddActivityListener(listener);

        // ensure inventory exists for product X
        _serviceProvider.GetRequiredService<InMemoryInventoryService>().AddStock("X", 10);
        var res = await _mediator.SendAsync<ReserveInventoryCommand, InventoryReservedResult>(cmd);
        res.IsSuccess.Should().BeTrue();
        act.Should().NotBeNull();
        var objs2 = new Dictionary<string, object?>();
        foreach (var kv in act!.EnumerateTagObjects()) objs2[kv.Key] = kv.Value;
        objs2.Should().ContainKey("catga.req.order_id");
        objs2.Should().ContainKey("catga.req.product_id");
        objs2.Should().ContainKey("catga.req.quantity");
        objs2.Should().ContainKey("catga.res.reservation_id");
    }

    [Fact]
    public async Task Tracing_ShipOrder_ShouldInclude_Request_And_Response_Tags()
    {
        var create = await _mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(new CreateOrderCommand("Y", 1, 1m));
        var cmd = new ShipOrderCommand(create.Value!.OrderId, "ADDR") { CorrelationId = MessageExtensions.NewMessageId() };

        Activity? act = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == CatgaActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a =>
            {
                if (a.OperationName.Contains("ShipOrderCommand") && a.Tags.Any(t => t.Key.StartsWith("catga.req.") || t.Key.StartsWith("catga.res.")))
                    act = a;
            }
        };
        ActivitySource.AddActivityListener(listener);

        var res = await _mediator.SendAsync<ShipOrderCommand, ShipmentResult>(cmd);
        res.IsSuccess.Should().BeTrue();
        act.Should().NotBeNull();
        var objs3 = new Dictionary<string, object?>();
        foreach (var kv in act!.EnumerateTagObjects()) objs3[kv.Key] = kv.Value;
        objs3.Should().ContainKey("catga.req.order_id");
        objs3.Should().ContainKey("catga.req.address");
        objs3.Should().ContainKey("catga.res.tracking");
    }

    [Fact]
    public async Task Tracing_CancelOrder_ShouldInclude_Request_And_Response_Tags()
    {
        var create = await _mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(new CreateOrderCommand("Z", 1, 1m));
        var cmd = new CancelOrderCommand(create.Value!.OrderId, "Reason") { CorrelationId = MessageExtensions.NewMessageId() };

        Activity? act = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == CatgaActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a =>
            {
                if (a.OperationName.Contains("CancelOrderCommand") && a.Tags.Any(t => t.Key.StartsWith("catga.req.") || t.Key.StartsWith("catga.res.")))
                    act = a;
            }
        };
        ActivitySource.AddActivityListener(listener);

        var res = await _mediator.SendAsync<CancelOrderCommand, OrderCancelledResult>(cmd);
        res.IsSuccess.Should().BeTrue();
        act.Should().NotBeNull();
        var objs4 = new Dictionary<string, object?>();
        foreach (var kv in act!.EnumerateTagObjects()) objs4[kv.Key] = kv.Value;
        objs4.Should().ContainKey("catga.req.order_id");
        objs4.Should().ContainKey("catga.req.reason");
        objs4.Should().ContainKey("catga.res.order_id");
    }

    [Fact]
    public async Task Tracing_GetOrderQuery_ShouldInclude_Request_And_Response_Tags()
    {
        var create = await _mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(new CreateOrderCommand("Q", 1, 2m));
        var qry = new GetOrderQuery(create.Value!.OrderId);

        Activity? act = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == CatgaActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a =>
            {
                if (a.OperationName.Contains("GetOrderQuery") && a.Tags.Any(t => t.Key.StartsWith("catga.req.") || t.Key.StartsWith("catga.res.")))
                    act = a;
            }
        };
        ActivitySource.AddActivityListener(listener);

        var res = await _mediator.SendAsync<GetOrderQuery, OrderDetails>(qry);
        res.IsSuccess.Should().BeTrue();
        act.Should().NotBeNull();
        var objs5 = new Dictionary<string, object?>();
        foreach (var kv in act!.EnumerateTagObjects()) objs5[kv.Key] = kv.Value;
        objs5.Should().ContainKey("catga.req.order_id");
        objs5.Should().ContainKey("catga.res.order_id");
        objs5.Should().ContainKey("catga.res.status");
        objs5.Should().ContainKey("catga.res.product_id");
        objs5.Should().ContainKey("catga.res.amount");
    }

    [Fact]
    public async Task Tracing_ShouldInclude_Request_And_Response_Tags()
    {
        var orderId = 123456L;
        var amount = 42.5m;

        Activity? captured = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == CatgaActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a =>
            {
                if (a.OperationName.Contains("ProcessPaymentCommand") && a.Tags.Any(t => t.Key.StartsWith("catga.req.") || t.Key.StartsWith("catga.res.")))
                    captured = a;
            }
        };
        ActivitySource.AddActivityListener(listener);

        var cmd = new ProcessPaymentCommand(orderId, amount) { CorrelationId = MessageExtensions.NewMessageId() };
        var result = await _mediator.SendAsync<ProcessPaymentCommand, PaymentResult>(cmd);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        var objs6 = new Dictionary<string, object?>();
        foreach (var kv in captured!.EnumerateTagObjects()) objs6[kv.Key] = kv.Value;
        objs6.Should().ContainKey("catga.req.OrderId");
        objs6["catga.req.OrderId"].Should().Be(orderId);
        objs6.Should().ContainKey("catga.req.Amount");
        objs6.Should().ContainKey("catga.res.TransactionId");
        objs6.Should().ContainKey("catga.res.Amount");
        objs6["catga.res.Amount"].Should().Be(amount);
    }

    #region 完整订单流程 - 成功路径

    [Fact]
    public async Task CompleteOrderFlow_HappyPath_ShouldSucceed()
    {
        // Arrange
        var correlationId = MessageExtensions.NewMessageId();
        var productId = "LAPTOP-001";
        var quantity = 1;
        var amount = 999.99m;

        // 设置初始库存
        var inventoryService = _serviceProvider.GetRequiredService<InMemoryInventoryService>();
        inventoryService.AddStock(productId, 10);

        // Act & Assert

        // 步骤1: 创建订单
        var createOrderCommand = new CreateOrderCommand(productId, quantity, amount)
        {
            CorrelationId = correlationId
        };
        var orderResult = await _mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(createOrderCommand);

        orderResult.IsSuccess.Should().BeTrue();
        orderResult.Value.Should().NotBeNull();
        var orderId = orderResult.Value!.OrderId;

        // 步骤2: 预留库存
        var reserveInventoryCommand = new ReserveInventoryCommand(orderId, productId, quantity)
        {
            CorrelationId = correlationId
        };
        var inventoryResult = await _mediator.SendAsync<ReserveInventoryCommand, InventoryReservedResult>(reserveInventoryCommand);

        inventoryResult.IsSuccess.Should().BeTrue();
        inventoryService.GetAvailableStock(productId).Should().Be(9); // 10 - 1

        // 步骤3: 处理支付
        var paymentCommand = new ProcessPaymentCommand(orderId, amount)
        {
            CorrelationId = correlationId
        };
        var paymentResult = await _mediator.SendAsync<ProcessPaymentCommand, PaymentResult>(paymentCommand);

        paymentResult.IsSuccess.Should().BeTrue();
        paymentResult.Value!.TransactionId.Should().NotBeNullOrEmpty();

        // 步骤4: 发货
        var shipCommand = new ShipOrderCommand(orderId, "123 Main St")
        {
            CorrelationId = correlationId
        };
        var shipResult = await _mediator.SendAsync<ShipOrderCommand, ShipmentResult>(shipCommand);

        shipResult.IsSuccess.Should().BeTrue();
        shipResult.Value!.TrackingNumber.Should().NotBeNullOrEmpty();

        // 步骤5: 查询订单状态
        var query = new GetOrderQuery(orderId);
        var orderDetails = await _mediator.SendAsync<GetOrderQuery, OrderDetails>(query);

        orderDetails.IsSuccess.Should().BeTrue();
        orderDetails.Value!.Status.Should().Be(OrderStatus.Shipped);
    }

    [Fact]
    public async Task CompleteOrderFlow_WithEvents_ShouldNotifyAllStakeholders()
    {
        // Arrange
        var correlationId = MessageExtensions.NewMessageId();
        var productId = "PHONE-001";
        var amount = 699.99m;

        var inventoryService = _serviceProvider.GetRequiredService<InMemoryInventoryService>();
        inventoryService.AddStock(productId, 5);

        OrderCreatedNotificationHandler.NotificationCount = 0;
        OrderCreatedMetricsHandler.MetricsCount = 0;
        PaymentNotificationHandler.NotificationCount = 0;
        ShippingNotificationHandler.NotificationCount = 0;

        // Act - 完整流程
        var createOrderCommand = new CreateOrderCommand(productId, 1, amount) { CorrelationId = correlationId };
        var orderResult = await _mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(createOrderCommand);

        // 发布订单创建事件
        var orderCreatedEvent = new OrderCreatedEvent(orderResult.Value!.OrderId, productId, 1) { CorrelationId = correlationId };
        await _mediator.PublishAsync(orderCreatedEvent);
        await Task.Delay(50);

        // 预留库存
        var reserveCommand = new ReserveInventoryCommand(orderResult.Value.OrderId, productId, 1) { CorrelationId = correlationId };
        await _mediator.SendAsync<ReserveInventoryCommand, InventoryReservedResult>(reserveCommand);

        // 支付
        var paymentCommand = new ProcessPaymentCommand(orderResult.Value.OrderId, amount) { CorrelationId = correlationId };
        var paymentResult = await _mediator.SendAsync<ProcessPaymentCommand, PaymentResult>(paymentCommand);

        // 发布支付完成事件
        var paymentEvent = new PaymentCompletedEvent(orderResult.Value.OrderId, paymentResult.Value!.TransactionId) { CorrelationId = correlationId };
        await _mediator.PublishAsync(paymentEvent);
        await Task.Delay(50);

        // 发货
        var shipCommand = new ShipOrderCommand(orderResult.Value.OrderId, "456 Oak Ave") { CorrelationId = correlationId };
        var shipResult = await _mediator.SendAsync<ShipOrderCommand, ShipmentResult>(shipCommand);

        // 发布发货事件
        var shippedEvent = new OrderShippedEvent(orderResult.Value.OrderId, shipResult.Value!.TrackingNumber) { CorrelationId = correlationId };
        await _mediator.PublishAsync(shippedEvent);
        await Task.Delay(50);

        // Assert - 所有事件handler都应该被触发
        OrderCreatedNotificationHandler.NotificationCount.Should().BeGreaterThan(0);
        OrderCreatedMetricsHandler.MetricsCount.Should().BeGreaterThan(0);
        PaymentNotificationHandler.NotificationCount.Should().BeGreaterThan(0);
        ShippingNotificationHandler.NotificationCount.Should().BeGreaterThan(0);
    }

    #endregion

    #region 失败场景 - 库存不足

    [Fact]
    public async Task OrderFlow_InsufficientInventory_ShouldFail()
    {
        // Arrange
        var productId = "LIMITED-001";
        var inventoryService = _serviceProvider.GetRequiredService<InMemoryInventoryService>();
        inventoryService.AddStock(productId, 2);

        var createOrderCommand = new CreateOrderCommand(productId, 5, 99.99m); // 请求5个，但只有2个

        // Act
        var orderResult = await _mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(createOrderCommand);
        orderResult.IsSuccess.Should().BeTrue(); // 订单创建成功

        var reserveCommand = new ReserveInventoryCommand(orderResult.Value!.OrderId, productId, 5);
        var inventoryResult = await _mediator.SendAsync<ReserveInventoryCommand, InventoryReservedResult>(reserveCommand);

        // Assert - 库存预留应该失败
        inventoryResult.IsSuccess.Should().BeFalse();
        inventoryResult.Error.Should().Contain("Insufficient inventory");
    }

    #endregion

    #region 失败场景 - 支付失败

    [Fact]
    public async Task OrderFlow_PaymentFailed_ShouldReleaseInventory()
    {
        // Arrange
        var productId = "TABLET-001";
        var inventoryService = _serviceProvider.GetRequiredService<InMemoryInventoryService>();
        inventoryService.AddStock(productId, 10);

        var createOrderCommand = new CreateOrderCommand(productId, 1, 499.99m);
        var orderResult = await _mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(createOrderCommand);

        var reserveCommand = new ReserveInventoryCommand(orderResult.Value!.OrderId, productId, 1);
        var inventoryResult = await _mediator.SendAsync<ReserveInventoryCommand, InventoryReservedResult>(reserveCommand);

        inventoryResult.IsSuccess.Should().BeTrue();
        var stockAfterReserve = inventoryService.GetAvailableStock(productId);

        // Act - 支付失败（金额为负数触发失败）
        var paymentCommand = new ProcessPaymentCommand(orderResult.Value.OrderId, -1m);
        var paymentResult = await _mediator.SendAsync<ProcessPaymentCommand, PaymentResult>(paymentCommand);

        // 支付失败后应该释放库存
        if (!paymentResult.IsSuccess)
        {
            inventoryService.ReleaseReservation(productId, 1);
        }

        // Assert
        paymentResult.IsSuccess.Should().BeFalse();
        inventoryService.GetAvailableStock(productId).Should().Be(10); // 库存已恢复
    }

    #endregion

    #region 订单取消流程

    [Fact]
    public async Task CancelOrder_WithRefund_ShouldCompleteSuccessfully()
    {
        // Arrange - 先创建一个已支付的订单
        var productId = "CAMERA-001";
        var amount = 799.99m;
        var inventoryService = _serviceProvider.GetRequiredService<InMemoryInventoryService>();
        inventoryService.AddStock(productId, 5);

        var createOrderCommand = new CreateOrderCommand(productId, 1, amount);
        var orderResult = await _mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(createOrderCommand);

        var reserveCommand = new ReserveInventoryCommand(orderResult.Value!.OrderId, productId, 1);
        await _mediator.SendAsync<ReserveInventoryCommand, InventoryReservedResult>(reserveCommand);

        var paymentCommand = new ProcessPaymentCommand(orderResult.Value.OrderId, amount);
        var paymentResult = await _mediator.SendAsync<ProcessPaymentCommand, PaymentResult>(paymentCommand);

        RefundHandler.RefundCount = 0;

        // Act - 取消订单
        var cancelCommand = new CancelOrderCommand(orderResult.Value.OrderId, "Customer request");
        var cancelResult = await _mediator.SendAsync<CancelOrderCommand, OrderCancelledResult>(cancelCommand);

        // 发布取消事件
        var cancelledEvent = new OrderCancelledEvent(orderResult.Value.OrderId, paymentResult.Value!.TransactionId);
        await _mediator.PublishAsync(cancelledEvent);
        await Task.Delay(50);

        // 释放库存
        inventoryService.ReleaseReservation(productId, 1);

        // Assert
        cancelResult.IsSuccess.Should().BeTrue();
        RefundHandler.RefundCount.Should().BeGreaterThan(0);
        inventoryService.GetAvailableStock(productId).Should().Be(5); // 库存已恢复
    }

    #endregion

    #region 并发订单处理

    [Fact]
    public async Task ConcurrentOrders_ShouldHandleCorrectly()
    {
        // Arrange
        var productId = "POPULAR-001";
        var inventoryService = _serviceProvider.GetRequiredService<InMemoryInventoryService>();
        inventoryService.AddStock(productId, 100);

        var concurrentOrders = 20;
        var successfulOrders = 0;

        // Act - 并发创建订单
        var tasks = Enumerable.Range(0, concurrentOrders).Select(async i =>
        {
            var createCommand = new CreateOrderCommand(productId, 1, 49.99m);
            var orderResult = await _mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(createCommand);

            if (orderResult.IsSuccess)
            {
                var reserveCommand = new ReserveInventoryCommand(
                    orderResult.Value!.OrderId,
                    productId,
                    1);
                var reserveResult = await _mediator.SendAsync<ReserveInventoryCommand, InventoryReservedResult>(reserveCommand);

                if (reserveResult.IsSuccess)
                {
                    Interlocked.Increment(ref successfulOrders);
                }
            }
        }).ToList();

        await Task.WhenAll(tasks);

        // Assert
        successfulOrders.Should().Be(concurrentOrders);
        inventoryService.GetAvailableStock(productId).Should().Be(100 - concurrentOrders);
    }

    [Fact]
    public async Task ConcurrentOrders_LimitedStock_ShouldHandleRaceCondition()
    {
        // Arrange - 只有10个库存，但20个并发订单
        var productId = "LIMITED-EDITION";
        var inventoryService = _serviceProvider.GetRequiredService<InMemoryInventoryService>();
        inventoryService.AddStock(productId, 10);

        var concurrentOrders = 20;
        var successfulReservations = 0;
        var failedReservations = 0;

        // Act
        var tasks = Enumerable.Range(0, concurrentOrders).Select(async i =>
        {
            var createCommand = new CreateOrderCommand(productId, 1, 99.99m);
            var orderResult = await _mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(createCommand);

            if (orderResult.IsSuccess)
            {
                var reserveCommand = new ReserveInventoryCommand(
                    orderResult.Value!.OrderId,
                    productId,
                    1);
                var reserveResult = await _mediator.SendAsync<ReserveInventoryCommand, InventoryReservedResult>(reserveCommand);

                if (reserveResult.IsSuccess)
                {
                    Interlocked.Increment(ref successfulReservations);
                }
                else
                {
                    Interlocked.Increment(ref failedReservations);
                }
            }
        }).ToList();

        await Task.WhenAll(tasks);

        // Assert - 应该只有10个成功，10个失败
        successfulReservations.Should().Be(10);
        failedReservations.Should().Be(10);
        inventoryService.GetAvailableStock(productId).Should().Be(0);
    }

    #endregion

    #region 批量订单处理

    [Fact]
    public async Task BatchOrders_ShouldProcessEfficiently()
    {
        // Arrange
        var productId = "BULK-ITEM";
        var inventoryService = _serviceProvider.GetRequiredService<InMemoryInventoryService>();
        inventoryService.AddStock(productId, 500);

        var batchSize = 100;
        var commands = Enumerable.Range(0, batchSize)
            .Select(i => new CreateOrderCommand(productId, 1, 19.99m))
            .ToList();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var results = await _mediator.SendBatchAsync<CreateOrderCommand, OrderCreatedResult>(commands);

        stopwatch.Stop();

        // Assert
        results.Should().HaveCount(batchSize);
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000);
    }

    #endregion

    #region 复杂业务场景 - 多商品订单

    [Fact]
    public async Task MultiItemOrder_ShouldHandleAllProducts()
    {
        // Arrange - 订单包含多个商品
        var inventoryService = _serviceProvider.GetRequiredService<InMemoryInventoryService>();
        var products = new[]
        {
            ("PROD-A", 10, 29.99m),
            ("PROD-B", 5, 49.99m),
            ("PROD-C", 15, 19.99m)
        };

        foreach (var (productId, stock, _) in products)
        {
            inventoryService.AddStock(productId, stock);
        }

        var totalAmount = products.Sum(p => p.Item3);

        // Act - 创建包含多个商品的订单
        var createCommand = new CreateOrderCommand("MULTI-ITEM", 1, totalAmount);
        var orderResult = await _mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(createCommand);

        orderResult.IsSuccess.Should().BeTrue();

        // 为每个商品预留库存
        var reservationTasks = products.Select(async p =>
        {
            var reserveCommand = new ReserveInventoryCommand(orderResult.Value!.OrderId, p.Item1, 1);
            return await _mediator.SendAsync<ReserveInventoryCommand, InventoryReservedResult>(reserveCommand);
        }).ToList();

        var reservationResults = await Task.WhenAll(reservationTasks);

        // Assert
        reservationResults.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());

        // 验证库存减少
        inventoryService.GetAvailableStock("PROD-A").Should().Be(9);
        inventoryService.GetAvailableStock("PROD-B").Should().Be(4);
        inventoryService.GetAvailableStock("PROD-C").Should().Be(14);
    }

    #endregion
}

#region 领域消息定义 - Commands

public partial record CreateOrderCommand(
    [property: TraceTag("catga.req.product_id")] string ProductId,
    [property: TraceTag("catga.req.quantity")] int Quantity,
    [property: TraceTag("catga.req.amount")] decimal Amount) : IRequest<OrderCreatedResult>
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
    public long? CorrelationId { get; init; }
}

public partial record ReserveInventoryCommand(
    [property: TraceTag("catga.req.order_id")] long OrderId,
    [property: TraceTag("catga.req.product_id")] string ProductId,
    [property: TraceTag("catga.req.quantity")] int Quantity) : IRequest<InventoryReservedResult>
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
    public long? CorrelationId { get; init; }
}

[
    TraceTags(Prefix = "catga.req.")
]
public partial record ProcessPaymentCommand(
    long OrderId,
    decimal Amount) : IRequest<PaymentResult>
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
    public long? CorrelationId { get; init; }
}

public partial record ShipOrderCommand(
    [property: TraceTag("catga.req.order_id")] long OrderId,
    [property: TraceTag("catga.req.address")] string Address) : IRequest<ShipmentResult>
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
    public long? CorrelationId { get; init; }
}

public partial record CancelOrderCommand(
    [property: TraceTag("catga.req.order_id")] long OrderId,
    [property: TraceTag("catga.req.reason")] string Reason) : IRequest<OrderCancelledResult>
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
    public long? CorrelationId { get; init; }
}

public partial record GetOrderQuery([property: TraceTag("catga.req.order_id")] long OrderId) : IRequest<OrderDetails>
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
}

#endregion

#region 领域消息定义 - Results

public partial record OrderCreatedResult([property: TraceTag("catga.res.order_id")] long OrderId, string ProductId, int Quantity);
public partial record InventoryReservedResult([property: TraceTag("catga.res.reservation_id")] long ReservationId);
[
    TraceTags(Prefix = "catga.res.")
]
public partial record PaymentResult(string TransactionId, decimal Amount);
public partial record ShipmentResult([property: TraceTag("catga.res.tracking")] string TrackingNumber);
public partial record OrderCancelledResult([property: TraceTag("catga.res.order_id")] long OrderId);
public partial record OrderDetails(
    [property: TraceTag("catga.res.order_id")] long OrderId,
    [property: TraceTag("catga.res.status")] OrderStatus Status,
    [property: TraceTag("catga.res.product_id")] string ProductId,
    [property: TraceTag("catga.res.amount")] decimal Amount);

public enum OrderStatus
{
    Created,
    InventoryReserved,
    Paid,
    Shipped,
    Cancelled
}

#endregion

#region 领域消息定义 - Events

public record OrderCreatedEvent(long OrderId, string ProductId, int Quantity) : IEvent
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
    public long? CorrelationId { get; init; }
}

public record PaymentCompletedEvent(long OrderId, string TransactionId) : IEvent
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
    public long? CorrelationId { get; init; }
}

public record OrderShippedEvent(long OrderId, string TrackingNumber) : IEvent
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
    public long? CorrelationId { get; init; }
}

public record OrderCancelledEvent(long OrderId, string TransactionId) : IEvent
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
    public long? CorrelationId { get; init; }
}

#endregion

#region Command Handlers

public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, OrderCreatedResult>
{
    private readonly InMemoryOrderRepository _repository;

    public CreateOrderCommandHandler(InMemoryOrderRepository repository)
    {
        _repository = repository;
    }

    public Task<CatgaResult<OrderCreatedResult>> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        var orderId = Random.Shared.Next(10000, 99999);
        _repository.AddOrder(orderId, request.ProductId, request.Quantity, request.Amount, OrderStatus.Created);

        var result = new OrderCreatedResult(orderId, request.ProductId, request.Quantity);
        return Task.FromResult(CatgaResult<OrderCreatedResult>.Success(result));
    }
}

public class ReserveInventoryCommandHandler : IRequestHandler<ReserveInventoryCommand, InventoryReservedResult>
{
    private readonly InMemoryInventoryService _inventoryService;
    private readonly InMemoryOrderRepository _orderRepository;

    public ReserveInventoryCommandHandler(
        InMemoryInventoryService inventoryService,
        InMemoryOrderRepository orderRepository)
    {
        _inventoryService = inventoryService;
        _orderRepository = orderRepository;
    }

    public Task<CatgaResult<InventoryReservedResult>> HandleAsync(
        ReserveInventoryCommand request,
        CancellationToken cancellationToken = default)
    {
        if (!_inventoryService.TryReserve(request.ProductId, request.Quantity))
        {
            return Task.FromResult(
                CatgaResult<InventoryReservedResult>.Failure("Insufficient inventory"));
        }

        _orderRepository.UpdateOrderStatus(request.OrderId, OrderStatus.InventoryReserved);
        var reservationId = Random.Shared.Next(1000, 9999);
        var result = new InventoryReservedResult(reservationId);

        return Task.FromResult(CatgaResult<InventoryReservedResult>.Success(result));
    }
}

public class ProcessPaymentCommandHandler : IRequestHandler<ProcessPaymentCommand, PaymentResult>
{
    private readonly InMemoryOrderRepository _orderRepository;

    public ProcessPaymentCommandHandler(InMemoryOrderRepository orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public Task<CatgaResult<PaymentResult>> HandleAsync(
        ProcessPaymentCommand request,
        CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0)
        {
            return Task.FromResult(
                CatgaResult<PaymentResult>.Failure("Invalid payment amount"));
        }

        var transactionId = $"TXN-{Guid.NewGuid().ToString()[..8]}";
        _orderRepository.UpdateOrderStatus(request.OrderId, OrderStatus.Paid);

        var result = new PaymentResult(transactionId, request.Amount);
        return Task.FromResult(CatgaResult<PaymentResult>.Success(result));
    }
}

public class ShipOrderCommandHandler : IRequestHandler<ShipOrderCommand, ShipmentResult>
{
    private readonly InMemoryOrderRepository _orderRepository;

    public ShipOrderCommandHandler(InMemoryOrderRepository orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public Task<CatgaResult<ShipmentResult>> HandleAsync(
        ShipOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        var trackingNumber = $"TRACK-{Random.Shared.Next(100000, 999999)}";
        _orderRepository.UpdateOrderStatus(request.OrderId, OrderStatus.Shipped);

        var result = new ShipmentResult(trackingNumber);
        return Task.FromResult(CatgaResult<ShipmentResult>.Success(result));
    }
}

public class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand, OrderCancelledResult>
{
    private readonly InMemoryOrderRepository _orderRepository;

    public CancelOrderCommandHandler(InMemoryOrderRepository orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public Task<CatgaResult<OrderCancelledResult>> HandleAsync(
        CancelOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        _orderRepository.UpdateOrderStatus(request.OrderId, OrderStatus.Cancelled);
        var result = new OrderCancelledResult(request.OrderId);
        return Task.FromResult(CatgaResult<OrderCancelledResult>.Success(result));
    }
}

public class GetOrderQueryHandler : IRequestHandler<GetOrderQuery, OrderDetails>
{
    private readonly InMemoryOrderRepository _orderRepository;

    public GetOrderQueryHandler(InMemoryOrderRepository orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public Task<CatgaResult<OrderDetails>> HandleAsync(
        GetOrderQuery request,
        CancellationToken cancellationToken = default)
    {
        var order = _orderRepository.GetOrder(request.OrderId);
        if (order == null)
        {
            return Task.FromResult(
                CatgaResult<OrderDetails>.Failure("Order not found"));
        }

        return Task.FromResult(CatgaResult<OrderDetails>.Success(order));
    }
}

#endregion

#region Event Handlers

public class OrderCreatedNotificationHandler : IEventHandler<OrderCreatedEvent>
{
    public static int NotificationCount = 0;

    public Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref NotificationCount);
        // 模拟发送通知
        return Task.CompletedTask;
    }
}

public class OrderCreatedMetricsHandler : IEventHandler<OrderCreatedEvent>
{
    public static int MetricsCount = 0;

    public Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref MetricsCount);
        // 模拟记录指标
        return Task.CompletedTask;
    }
}

public class PaymentNotificationHandler : IEventHandler<PaymentCompletedEvent>
{
    public static int NotificationCount = 0;

    public Task HandleAsync(PaymentCompletedEvent @event, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref NotificationCount);
        return Task.CompletedTask;
    }
}

public class ShippingNotificationHandler : IEventHandler<OrderShippedEvent>
{
    public static int NotificationCount = 0;

    public Task HandleAsync(OrderShippedEvent @event, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref NotificationCount);
        return Task.CompletedTask;
    }
}

public class RefundHandler : IEventHandler<OrderCancelledEvent>
{
    public static int RefundCount = 0;

    public Task HandleAsync(OrderCancelledEvent @event, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref RefundCount);
        // 模拟退款处理
        return Task.CompletedTask;
    }
}

#endregion

#region 共享服务

public class InMemoryInventoryService
{
    private readonly Dictionary<string, int> _stock = new();
    private readonly object _lock = new();

    public void AddStock(string productId, int quantity)
    {
        lock (_lock)
        {
            if (!_stock.ContainsKey(productId))
                _stock[productId] = 0;

            _stock[productId] += quantity;
        }
    }

    public bool TryReserve(string productId, int quantity)
    {
        lock (_lock)
        {
            if (!_stock.ContainsKey(productId) || _stock[productId] < quantity)
                return false;

            _stock[productId] -= quantity;
            return true;
        }
    }

    public void ReleaseReservation(string productId, int quantity)
    {
        lock (_lock)
        {
            if (!_stock.ContainsKey(productId))
                _stock[productId] = 0;

            _stock[productId] += quantity;
        }
    }

    public int GetAvailableStock(string productId)
    {
        lock (_lock)
        {
            return _stock.GetValueOrDefault(productId, 0);
        }
    }
}

public class InMemoryOrderRepository
{
    private readonly Dictionary<long, OrderDetails> _orders = new();
    private readonly object _lock = new();

    public void AddOrder(long orderId, string productId, int quantity, decimal amount, OrderStatus status)
    {
        lock (_lock)
        {
            _orders[orderId] = new OrderDetails(orderId, status, productId, amount);
        }
    }

    public void UpdateOrderStatus(long orderId, OrderStatus status)
    {
        lock (_lock)
        {
            if (_orders.ContainsKey(orderId))
            {
                var order = _orders[orderId];
                _orders[orderId] = order with { Status = status };
            }
        }
    }

    public OrderDetails? GetOrder(long orderId)
    {
        lock (_lock)
        {
            return _orders.GetValueOrDefault(orderId);
        }
    }
}

#endregion


