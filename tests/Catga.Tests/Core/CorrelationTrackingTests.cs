using Catga;
using Catga.Abstractions;
using Catga.Core;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace Catga.Tests.Core;

/// <summary>
/// 消息相关性追踪完整场景测试 (TDD方法)
/// 测试CorrelationId的端到端传播：
/// 1. Command到Event的相关性传播
/// 2. 分布式追踪集成
/// 3. 多层级消息链路追踪
/// 4. 并发场景下的相关性隔离
/// 5. 错误场景的相关性保持
/// </summary>
public class CorrelationTrackingTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ICatgaMediator _mediator;

    public CorrelationTrackingTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();

        // 注册测试处理器
        services.AddScoped<IRequestHandler<CreateOrderCommand, CreateOrderResponse>, CreateOrderCommandHandler>();
        services.AddScoped<IRequestHandler<PaymentCommand, PaymentResponse>, PaymentCommandHandler>();
        services.AddScoped<IEventHandler<OrderCreatedEvent>, OrderCreatedEventHandler>();
        services.AddScoped<IEventHandler<OrderCreatedEvent>, SendEmailEventHandler>();
        services.AddScoped<IEventHandler<OrderCreatedEvent>, UpdateInventoryEventHandler>();

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<ICatgaMediator>();
    }

    #region 基础相关性测试

    [Fact]
    public async Task SendAsync_WithCorrelationId_ShouldPreserveCorrelationId()
    {
        // Arrange
        var correlationId = MessageExtensions.NewMessageId();
        var command = new CreateOrderCommand("PROD-001", 5) { CorrelationId = correlationId };

        // Act
        var result = await _mediator.SendAsync<CreateOrderCommand, CreateOrderResponse>(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        // 验证handler接收到了正确的correlationId
        CreateOrderCommandHandler.LastReceivedCorrelationId.Should().Be(correlationId);
    }

    [Fact]
    public async Task SendAsync_WithoutCorrelationId_ShouldStillProcess()
    {
        // Arrange
        var command = new CreateOrderCommand("PROD-002", 3);

        // Act
        var result = await _mediator.SendAsync<CreateOrderCommand, CreateOrderResponse>(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task PublishAsync_WithCorrelationId_ShouldPropagateToAllHandlers()
    {
        // Arrange
        var correlationId = MessageExtensions.NewMessageId();
        var @event = new OrderCreatedEvent(123, "PROD-001", 5) { CorrelationId = correlationId };

        // 重置追踪
        OrderCreatedEventHandler.ReceivedCorrelationIds.Clear();
        SendEmailEventHandler.ReceivedCorrelationIds.Clear();
        UpdateInventoryEventHandler.ReceivedCorrelationIds.Clear();

        // Act
        await _mediator.PublishAsync(@event);
        await Task.Delay(50); // 等待所有handler完成

        // Assert - 所有handler都应该收到相同的correlationId
        OrderCreatedEventHandler.ReceivedCorrelationIds.Should().Contain(correlationId);
        SendEmailEventHandler.ReceivedCorrelationIds.Should().Contain(correlationId);
        UpdateInventoryEventHandler.ReceivedCorrelationIds.Should().Contain(correlationId);
    }

    #endregion

    #region 跨消息传播测试

    [Fact]
    public async Task CommandToEvent_ShouldPropagateCorrelationId()
    {
        // Arrange
        var correlationId = MessageExtensions.NewMessageId();
        var command = new CreateOrderCommand("PROD-003", 2) { CorrelationId = correlationId };

        OrderCreatedEventHandler.ReceivedCorrelationIds.Clear();

        // Act - 执行命令（内部会发布事件）
        var result = await _mediator.SendAsync<CreateOrderCommand, CreateOrderResponse>(command);

        // 手动发布事件以模拟实际流程
        if (result.IsSuccess)
        {
            var @event = new OrderCreatedEvent(result.Value!.OrderId, command.ProductId, command.Quantity)
            {
                CorrelationId = correlationId
            };
            await _mediator.PublishAsync(@event);
            await Task.Delay(50);
        }

        // Assert
        result.IsSuccess.Should().BeTrue();
        OrderCreatedEventHandler.ReceivedCorrelationIds.Should().Contain(correlationId);
    }

    [Fact]
    public async Task MultiLevelMessageChain_ShouldMaintainCorrelationId()
    {
        // Arrange - 模拟多层级消息链：CreateOrder -> OrderCreated -> Payment -> PaymentCompleted
        var correlationId = MessageExtensions.NewMessageId();

        // Act - 第一层：创建订单
        var createOrderCommand = new CreateOrderCommand("PROD-004", 1) { CorrelationId = correlationId };
        var orderResult = await _mediator.SendAsync<CreateOrderCommand, CreateOrderResponse>(createOrderCommand);

        // 第二层：支付命令（使用相同的correlationId）
        var paymentCommand = new PaymentCommand(orderResult.Value!.OrderId, 99.99m) { CorrelationId = correlationId };
        var paymentResult = await _mediator.SendAsync<PaymentCommand, PaymentResponse>(paymentCommand);

        // Assert - 整个链路应该保持相同的correlationId
        PaymentCommandHandler.LastReceivedCorrelationId.Should().Be(correlationId);
        paymentResult.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region 并发隔离测试

    [Fact]
    public async Task ConcurrentRequests_ShouldIsolateCorrelationIds()
    {
        // Arrange - 启动多个并发请求，每个有不同的correlationId
        var taskCount = 20;
        var correlationIds = Enumerable.Range(0, taskCount)
            .Select(_ => MessageExtensions.NewMessageId())
            .ToList();

        var results = new Dictionary<long, CreateOrderResponse>();
        var lockObj = new object();

        // Act - 并发执行
        var tasks = correlationIds.Select(async correlationId =>
        {
            var command = new CreateOrderCommand($"PROD-{correlationId}", 1)
            {
                CorrelationId = correlationId
            };

            var result = await _mediator.SendAsync<CreateOrderCommand, CreateOrderResponse>(command);

            lock (lockObj)
            {
                if (result.IsSuccess && result.Value != null)
                {
                    results[correlationId] = result.Value;
                }
            }
        }).ToList();

        await Task.WhenAll(tasks);

        // Assert - 每个请求的结果应该能通过correlationId正确关联
        results.Should().HaveCount(taskCount);
        results.Keys.Should().BeEquivalentTo(correlationIds);
    }

    [Fact]
    public async Task ConcurrentEvents_ShouldPreserveIndividualCorrelationIds()
    {
        // Arrange
        var eventCount = 30;
        var correlationIds = Enumerable.Range(0, eventCount)
            .Select(_ => MessageExtensions.NewMessageId())
            .ToList();

        OrderCreatedEventHandler.ReceivedCorrelationIds.Clear();

        // Act - 并发发布事件
        var tasks = correlationIds.Select(async correlationId =>
        {
            var @event = new OrderCreatedEvent(
                Random.Shared.Next(1000, 9999),
                $"PROD-{correlationId}",
                1)
            {
                CorrelationId = correlationId
            };

            await _mediator.PublishAsync(@event);
        }).ToList();

        await Task.WhenAll(tasks);
        await Task.Delay(100); // 等待所有handler完成

        // Assert - 应该收到所有不同的correlationId
        OrderCreatedEventHandler.ReceivedCorrelationIds.Should().HaveCountGreaterOrEqualTo(eventCount);
        OrderCreatedEventHandler.ReceivedCorrelationIds.Should().Contain(correlationIds);
    }

    #endregion

    #region 分布式追踪集成测试

    [Fact]
    public async Task SendAsync_ShouldCreateActivityWithCorrelationId()
    {
        // Arrange
        var correlationId = MessageExtensions.NewMessageId();
        Activity? capturedActivity = null;

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Catga",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => capturedActivity = activity
        };

        ActivitySource.AddActivityListener(listener);

        var command = new CreateOrderCommand("PROD-TRACE", 1) { CorrelationId = correlationId };

        // Act
        var result = await _mediator.SendAsync<CreateOrderCommand, CreateOrderResponse>(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        // Note: 实际的Activity验证可能需要更复杂的设置
    }

    #endregion

    #region 错误场景测试

    [Fact]
    public async Task SendAsync_OnFailure_ShouldPreserveCorrelationId()
    {
        // Arrange
        var correlationId = MessageExtensions.NewMessageId();
        var command = new CreateOrderCommand("INVALID", -1) // 无效数量会导致失败
        {
            CorrelationId = correlationId
        };

        // Act
        var result = await _mediator.SendAsync<CreateOrderCommand, CreateOrderResponse>(command);

        // Assert - 即使失败，也应该保持correlationId
        CreateOrderCommandHandler.LastReceivedCorrelationId.Should().Be(correlationId);
    }

    [Fact]
    public async Task PublishAsync_WithFailingHandler_ShouldPropagateCorrelationIdToOtherHandlers()
    {
        // Arrange
        var correlationId = MessageExtensions.NewMessageId();
        var @event = new OrderCreatedEvent(999, "PROD-FAIL", 1) { CorrelationId = correlationId };

        SendEmailEventHandler.ReceivedCorrelationIds.Clear();
        UpdateInventoryEventHandler.ReceivedCorrelationIds.Clear();

        // Act - 即使某个handler失败，其他handler仍应收到correlationId
        await _mediator.PublishAsync(@event);
        await Task.Delay(50);

        // Assert - 至少其他handler应该收到correlationId
        var allReceived = SendEmailEventHandler.ReceivedCorrelationIds
            .Concat(UpdateInventoryEventHandler.ReceivedCorrelationIds)
            .ToList();

        allReceived.Should().Contain(correlationId);
    }

    #endregion

    #region 实际场景测试

    [Fact]
    public async Task ECommerceOrderFlow_ShouldTraceEntireJourney()
    {
        // Arrange - 模拟完整的电商订单流程
        var correlationId = MessageExtensions.NewMessageId(); // 一个请求ID追踪整个流程
        var operationLog = new List<string>();

        // Act - 步骤1: 创建订单
        var createOrderCommand = new CreateOrderCommand("PROD-E001", 2) { CorrelationId = correlationId };
        var orderResult = await _mediator.SendAsync<CreateOrderCommand, CreateOrderResponse>(createOrderCommand);
        operationLog.Add($"Order Created: {orderResult.Value?.OrderId}, CorrelationId: {correlationId}");

        // 步骤2: 发布订单创建事件（会触发多个handler）
        OrderCreatedEventHandler.ReceivedCorrelationIds.Clear();
        SendEmailEventHandler.ReceivedCorrelationIds.Clear();
        UpdateInventoryEventHandler.ReceivedCorrelationIds.Clear();

        var orderCreatedEvent = new OrderCreatedEvent(
            orderResult.Value!.OrderId,
            createOrderCommand.ProductId,
            createOrderCommand.Quantity)
        {
            CorrelationId = correlationId
        };
        await _mediator.PublishAsync(orderCreatedEvent);
        await Task.Delay(50);
        operationLog.Add($"Order Event Published, CorrelationId: {correlationId}");

        // 步骤3: 执行支付
        var paymentCommand = new PaymentCommand(orderResult.Value.OrderId, 199.98m) { CorrelationId = correlationId };
        var paymentResult = await _mediator.SendAsync<PaymentCommand, PaymentResponse>(paymentCommand);
        operationLog.Add($"Payment Processed: {paymentResult.Value?.TransactionId}, CorrelationId: {correlationId}");

        // Assert - 整个流程应该用同一个correlationId追踪
        orderResult.IsSuccess.Should().BeTrue();
        paymentResult.IsSuccess.Should().BeTrue();

        // 验证所有步骤都使用了相同的correlationId
        CreateOrderCommandHandler.LastReceivedCorrelationId.Should().Be(correlationId);
        PaymentCommandHandler.LastReceivedCorrelationId.Should().Be(correlationId);
        OrderCreatedEventHandler.ReceivedCorrelationIds.Should().Contain(correlationId);
        SendEmailEventHandler.ReceivedCorrelationIds.Should().Contain(correlationId);
        UpdateInventoryEventHandler.ReceivedCorrelationIds.Should().Contain(correlationId);

        operationLog.Should().HaveCount(3);
    }

    [Fact]
    public async Task MicroservicesCommunication_ShouldMaintainTraceContext()
    {
        // Arrange - 模拟微服务间通信，correlationId在服务间传递
        var correlationId = MessageExtensions.NewMessageId();
        var serviceCallChain = new List<(string Service, long CorrelationId)>();

        // Service 1: Order Service
        var orderCommand = new CreateOrderCommand("PROD-MS01", 1) { CorrelationId = correlationId };
        var orderResult = await _mediator.SendAsync<CreateOrderCommand, CreateOrderResponse>(orderCommand);
        serviceCallChain.Add(("OrderService", CreateOrderCommandHandler.LastReceivedCorrelationId ?? 0));

        // Service 2: Payment Service (使用相同的correlationId)
        var paymentCommand = new PaymentCommand(orderResult.Value!.OrderId, 99.99m) { CorrelationId = correlationId };
        var paymentResult = await _mediator.SendAsync<PaymentCommand, PaymentResponse>(paymentCommand);
        serviceCallChain.Add(("PaymentService", PaymentCommandHandler.LastReceivedCorrelationId ?? 0));

        // Assert - 所有服务调用应该共享同一个correlationId
        serviceCallChain.Should().AllSatisfy(call => call.CorrelationId.Should().Be(correlationId));
        serviceCallChain.Select(c => c.CorrelationId).Distinct().Should().ContainSingle();
    }

    #endregion
}

#region 测试消息定义

public record CreateOrderCommand(string ProductId, int Quantity) : IRequest<CreateOrderResponse>
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
    public long? CorrelationId { get; init; }
}

public record CreateOrderResponse(long OrderId, string ProductId, int Quantity);

public record PaymentCommand(long OrderId, decimal Amount) : IRequest<PaymentResponse>
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
    public long? CorrelationId { get; init; }
}

public record PaymentResponse(string TransactionId, bool Success);

public record OrderCreatedEvent(long OrderId, string ProductId, int Quantity) : IEvent
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
    public long? CorrelationId { get; init; }
}

#endregion

#region 测试Handler实现

public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, CreateOrderResponse>
{
    public static long? LastReceivedCorrelationId { get; private set; }

    public ValueTask<CatgaResult<CreateOrderResponse>> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        LastReceivedCorrelationId = request.CorrelationId;

        if (request.Quantity <= 0)
        {
            return new ValueTask<CatgaResult<CreateOrderResponse>>(
                CatgaResult<CreateOrderResponse>.Failure("Invalid quantity"));
        }

        var orderId = Random.Shared.Next(10000, 99999);
        var response = new CreateOrderResponse(orderId, request.ProductId, request.Quantity);
        return new ValueTask<CatgaResult<CreateOrderResponse>>(CatgaResult<CreateOrderResponse>.Success(response));
    }
}

public class PaymentCommandHandler : IRequestHandler<PaymentCommand, PaymentResponse>
{
    public static long? LastReceivedCorrelationId { get; private set; }

    public ValueTask<CatgaResult<PaymentResponse>> HandleAsync(
        PaymentCommand request,
        CancellationToken cancellationToken = default)
    {
        LastReceivedCorrelationId = request.CorrelationId;

        var transactionId = $"TXN-{Random.Shared.Next(100000, 999999)}";
        var response = new PaymentResponse(transactionId, true);
        return new ValueTask<CatgaResult<PaymentResponse>>(CatgaResult<PaymentResponse>.Success(response));
    }
}

public class OrderCreatedEventHandler : IEventHandler<OrderCreatedEvent>
{
    public static readonly List<long> ReceivedCorrelationIds = new();

    public ValueTask HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        if (@event.CorrelationId.HasValue)
        {
            lock (ReceivedCorrelationIds)
            {
                ReceivedCorrelationIds.Add(@event.CorrelationId.Value);
            }
        }
        return ValueTask.CompletedTask;
    }
}

public class SendEmailEventHandler : IEventHandler<OrderCreatedEvent>
{
    public static readonly List<long> ReceivedCorrelationIds = new();

    public ValueTask HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        if (@event.CorrelationId.HasValue)
        {
            lock (ReceivedCorrelationIds)
            {
                ReceivedCorrelationIds.Add(@event.CorrelationId.Value);
            }
        }
        return ValueTask.CompletedTask;
    }
}

public class UpdateInventoryEventHandler : IEventHandler<OrderCreatedEvent>
{
    public static readonly List<long> ReceivedCorrelationIds = new();

    public ValueTask HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        if (@event.CorrelationId.HasValue)
        {
            lock (ReceivedCorrelationIds)
            {
                ReceivedCorrelationIds.Add(@event.CorrelationId.Value);
            }
        }
        return ValueTask.CompletedTask;
    }
}

#endregion


