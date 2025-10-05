using Catga;
using Catga.DependencyInjection;
using Catga.Handlers;
using Catga.Messages;
using Catga.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Outbox & Inbox 模式演示
/// 展示如何实现可靠的分布式消息传递
/// </summary>

// ==================== 消息定义 ====================

public record CreateOrderCommand : ICommand<OrderResult>, MessageBase
{
    public required string CustomerId { get; init; }
    public required decimal Amount { get; init; }
    public MessageId MessageId { get; init; } = MessageId.Generate();
    public CorrelationId CorrelationId { get; init; } = CorrelationId.Generate();
}

public record OrderResult
{
    public required string OrderId { get; init; }
    public required string Status { get; init; }
}

public record OrderCreatedEvent : IEvent, MessageBase
{
    public required string OrderId { get; init; }
    public required string CustomerId { get; init; }
    public required decimal Amount { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public MessageId MessageId { get; init; } = MessageId.Generate();
    public CorrelationId CorrelationId { get; init; } = CorrelationId.Generate();
}

// ==================== 处理器 ====================

public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderResult>
{
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<CreateOrderHandler> _logger;

    public CreateOrderHandler(
        ICatgaMediator mediator,
        ILogger<CreateOrderHandler> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<CatgaResult<OrderResult>> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating order for customer {CustomerId} with amount {Amount}",
            request.CustomerId, request.Amount);

        // 模拟业务逻辑
        var orderId = Guid.NewGuid().ToString("N")[..8];

        // 发布事件（会自动保存到 Outbox）
        await _mediator.PublishAsync(new OrderCreatedEvent
        {
            OrderId = orderId,
            CustomerId = request.CustomerId,
            Amount = request.Amount,
            MessageId = MessageId.Generate(),
            CorrelationId = request.CorrelationId
        }, cancellationToken);

        return CatgaResult<OrderResult>.Success(new OrderResult
        {
            OrderId = orderId,
            Status = "Created"
        });
    }
}

public class SendOrderEmailHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly ILogger<SendOrderEmailHandler> _logger;

    public SendOrderEmailHandler(ILogger<SendOrderEmailHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        // Inbox Behavior 确保这个处理器只执行一次（即使收到重复消息）
        _logger.LogInformation("Sending email for order {OrderId} (MessageId: {MessageId})",
            @event.OrderId, @event.MessageId.Value);

        // 模拟发送邮件
        await Task.Delay(100, cancellationToken);

        _logger.LogInformation("Email sent successfully for order {OrderId}", @event.OrderId);
    }
}

public class UpdateInventoryHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly ILogger<UpdateInventoryHandler> _logger;

    public UpdateInventoryHandler(ILogger<UpdateInventoryHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        // Inbox Behavior 确保这个处理器只执行一次
        _logger.LogInformation("Updating inventory for order {OrderId} (MessageId: {MessageId})",
            @event.OrderId, @event.MessageId.Value);

        // 模拟更新库存
        await Task.Delay(150, cancellationToken);

        _logger.LogInformation("Inventory updated successfully for order {OrderId}", @event.OrderId);
    }
}

// ==================== 主程序 ====================

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                // 1. 添加 Catga 核心
                services.AddCatga(options =>
                {
                    options.EnableLogging = true;
                    options.EnableTracing = true;
                });

                // 2. 添加 Outbox 模式（内存版本）
                services.AddOutbox(options =>
                {
                    options.EnablePublisher = true;
                    options.PollingInterval = TimeSpan.FromSeconds(2);
                    options.BatchSize = 100;
                });

                // 3. 添加 Inbox 模式（内存版本）
                services.AddInbox(options =>
                {
                    options.LockDuration = TimeSpan.FromMinutes(5);
                    options.RetentionPeriod = TimeSpan.FromHours(24);
                });

                // 4. 注册处理器
                services.AddRequestHandler<CreateOrderCommand, OrderResult, CreateOrderHandler>();
                services.AddEventHandler<OrderCreatedEvent, SendOrderEmailHandler>();
                services.AddEventHandler<OrderCreatedEvent, UpdateInventoryHandler>();

                // 5. 添加演示服务
                services.AddHostedService<DemoService>();
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .Build();

        await host.RunAsync();
    }
}

// ==================== 演示服务 ====================

public class DemoService : BackgroundService
{
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<DemoService> _logger;

    public DemoService(
        ICatgaMediator mediator,
        ILogger<DemoService> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("=== Outbox & Inbox 模式演示开始 ===\n");

        await Task.Delay(1000, stoppingToken); // 等待 Outbox Publisher 启动

        // 演示 1: 创建订单（触发 Outbox）
        _logger.LogInformation("📦 演示 1: 创建订单（使用 Outbox 模式）");
        var result1 = await _mediator.SendAsync<CreateOrderCommand, OrderResult>(
            new CreateOrderCommand
            {
                CustomerId = "CUST-001",
                Amount = 99.99m
            }, stoppingToken);

        if (result1.IsSuccess)
        {
            _logger.LogInformation("✅ 订单创建成功: {OrderId}", result1.Value?.OrderId);
        }

        await Task.Delay(3000, stoppingToken); // 等待 Outbox Publisher 发送消息

        // 演示 2: 创建另一个订单
        _logger.LogInformation("\n📦 演示 2: 创建第二个订单");
        var result2 = await _mediator.SendAsync<CreateOrderCommand, OrderResult>(
            new CreateOrderCommand
            {
                CustomerId = "CUST-002",
                Amount = 149.99m
            }, stoppingToken);

        if (result2.IsSuccess)
        {
            _logger.LogInformation("✅ 订单创建成功: {OrderId}", result2.Value?.OrderId);
        }

        await Task.Delay(3000, stoppingToken);

        // 演示 3: 模拟重复消息（测试 Inbox 幂等性）
        _logger.LogInformation("\n📥 演示 3: 测试 Inbox 幂等性（发送相同 MessageId 的事件两次）");

        var messageId = MessageId.Generate();
        var duplicateEvent = new OrderCreatedEvent
        {
            OrderId = "ORDER-DUP",
            CustomerId = "CUST-003",
            Amount = 199.99m,
            MessageId = messageId,  // 相同的 MessageId
            CorrelationId = CorrelationId.Generate()
        };

        // 第一次发送
        _logger.LogInformation("📤 第一次发送事件 (MessageId: {MessageId})", messageId.Value);
        await _mediator.PublishAsync(duplicateEvent, stoppingToken);

        await Task.Delay(1000, stoppingToken);

        // 第二次发送（相同 MessageId，应该被 Inbox 拒绝）
        _logger.LogInformation("📤 第二次发送相同事件 (MessageId: {MessageId})", messageId.Value);
        await _mediator.PublishAsync(duplicateEvent, stoppingToken);

        _logger.LogInformation("\n💡 注意: 即使发送了两次，处理器应该只执行一次！");

        await Task.Delay(3000, stoppingToken);

        _logger.LogInformation("\n=== 演示完成 ===");
        _logger.LogInformation("按 Ctrl+C 退出...");

        // 保持运行
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(5000, stoppingToken);
        }
    }
}

