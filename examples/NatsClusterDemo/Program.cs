using Catga;
using Catga.Distributed;
using Catga.Distributed.Nats.DependencyInjection;
using Catga.Messages;
using Catga.Handlers;
using Catga.Results;
using Catga.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// 获取节点配置（从环境变量或命令行）
var nodeId = Environment.GetEnvironmentVariable("NODE_ID") ?? args.ElementAtOrDefault(0) ?? "node1";
var nodePort = int.Parse(Environment.GetEnvironmentVariable("NODE_PORT") ?? args.ElementAtOrDefault(1) ?? "5001");
var natsUrl = Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://localhost:4222";

Console.WriteLine($"🚀 Starting {nodeId} on port {nodePort}...");

// ✅ 只需 3 行代码启动分布式集群（完全无锁）
builder.Services
    .AddCatga()
    .AddNatsTransport(opts => opts.Url = natsUrl)
    .AddNatsCluster(
        natsUrl: natsUrl,
        nodeId: nodeId,
        endpoint: $"http://localhost:{nodePort}"
    );

builder.Services.AddGeneratedHandlers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// API 端点
app.MapPost("/orders", async (CreateOrderRequest request, IDistributedMediator mediator) =>
{
    var result = await mediator.SendAsync<CreateOrderRequest, CreateOrderResponse>(request);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
});

app.MapGet("/nodes", async (IDistributedMediator mediator) =>
{
    var nodes = await mediator.GetNodesAsync();
    return Results.Ok(nodes);
});

app.MapGet("/health", async (IDistributedMediator mediator) =>
{
    var currentNode = await mediator.GetCurrentNodeAsync();
    var allNodes = await mediator.GetNodesAsync();
    
    return Results.Ok(new
    {
        CurrentNode = currentNode,
        TotalNodes = allNodes.Count,
        OnlineNodes = allNodes.Where(n => n.IsOnline).Count(),
        Nodes = allNodes
    });
});

app.Run($"http://localhost:{nodePort}");

// ===== 消息定义 =====

// ✅ Request - 默认 QoS 1 (At-Least-Once)，保证至少一次送达
public record CreateOrderRequest(string ProductId, int Quantity) : IRequest<CreateOrderResponse>;
public record CreateOrderResponse(string OrderId, string Status, string ProcessedBy);

// ❌ Event - 默认 QoS 0 (Fire-and-Forget)，不保证送达（适合日志、通知）
public record OrderCreatedEvent(string OrderId, string ProductId, int Quantity) : IEvent;

// ✅ Reliable Event - QoS 1 (At-Least-Once)，保证至少一次送达（适合关键业务事件）
public record OrderShippedEvent(string OrderId, string TrackingNumber) : IReliableEvent;

// ===== 处理器 =====

public class CreateOrderHandler : IRequestHandler<CreateOrderRequest, CreateOrderResponse>
{
    private readonly IDistributedMediator _mediator;
    private readonly ILogger<CreateOrderHandler> _logger;

    public CreateOrderHandler(IDistributedMediator mediator, ILogger<CreateOrderHandler> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<CatgaResult<CreateOrderResponse>> HandleAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var currentNode = await _mediator.GetCurrentNodeAsync(cancellationToken);
        var orderId = Guid.NewGuid().ToString("N")[..8];

        _logger.LogInformation("📦 Processing order {OrderId} on {NodeId}", orderId, currentNode.NodeId);

        // 模拟处理
        await Task.Delay(100, cancellationToken);

        // 发布普通事件（QoS 0 - Fire-and-Forget，不保证送达）
        await _mediator.PublishAsync(new OrderCreatedEvent(
            OrderId: orderId,
            ProductId: request.ProductId,
            Quantity: request.Quantity
        ), cancellationToken);

        // 发布可靠事件（QoS 1 - At-Least-Once，保证送达）
        await _mediator.PublishAsync(new OrderShippedEvent(
            OrderId: orderId,
            TrackingNumber: $"TRK-{orderId}"
        ), cancellationToken);

        return CatgaResult<CreateOrderResponse>.Success(new CreateOrderResponse(
            OrderId: orderId,
            Status: "Created",
            ProcessedBy: currentNode.NodeId
        ));
    }
}

// QoS 0 (Fire-and-Forget) - 可能丢失，适合日志
public class OrderCreatedEventHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly ILogger<OrderCreatedEventHandler> _logger;

    public OrderCreatedEventHandler(ILogger<OrderCreatedEventHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("📝 [QoS 0] Order created event received (may be lost): {@Event}", @event);
        return Task.CompletedTask;
    }
}

// QoS 1 (At-Least-Once) - 保证送达，可能重复，需要幂等性
public class OrderShippedEventHandler : IEventHandler<OrderShippedEvent>
{
    private readonly ILogger<OrderShippedEventHandler> _logger;

    public OrderShippedEventHandler(ILogger<OrderShippedEventHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(OrderShippedEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("📦 [QoS 1] Order shipped event received (guaranteed delivery): {@Event}", @event);
        
        // 注意：QoS 1 可能重复送达，需要幂等性处理
        // 可以使用 IdempotencyStore 来去重
        
        return Task.CompletedTask;
    }
}

