using Catga;
using Catga.DependencyInjection;
using Catga.Handlers;
using Catga.Messages;
using Catga.Results;
using Catga.Transport.Nats;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ✨ Catga + NATS 分布式集群
builder.Services.AddCatga();
builder.Services.AddGeneratedHandlers();

// 🚀 NATS 传输（跨节点通信）
builder.Services.AddNatsTransport(options =>
{
    options.Url = builder.Configuration.GetValue<string>("Nats:Url") ?? "nats://localhost:4222";
    options.SubjectPrefix = "catga.";
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ==================== API 端点 ====================

// 创建订单（跨节点分发）
app.MapPost("/orders", async (ICatgaMediator mediator, CreateOrderCommand cmd) =>
{
    var result = await mediator.SendAsync<CreateOrderCommand, OrderResponse>(cmd);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
});

// 发布事件（所有节点接收）
app.MapPost("/orders/{id}/ship", async (ICatgaMediator mediator, string id) =>
{
    await mediator.PublishAsync(new OrderShippedEvent(id));
    return Results.Ok(new { Message = "事件已发布到所有节点" });
});

// 健康检查
app.MapGet("/health", () => Results.Ok(new
{
    Status = "Healthy",
    Node = Environment.MachineName
}));

app.Run();

// ==================== 消息 ====================

public record CreateOrderCommand(string ProductId, int Quantity) : MessageBase, IRequest<OrderResponse>;
public record OrderResponse(string OrderId, string Status);
public record OrderShippedEvent(string OrderId) : EventBase;

// ==================== Handler ====================

// 订单创建 Handler（任意节点处理）
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderResponse>
{
    private readonly ILogger<CreateOrderHandler> _logger;

    public CreateOrderHandler(ILogger<CreateOrderHandler> logger) => _logger = logger;

    public Task<CatgaResult<OrderResponse>> HandleAsync(CreateOrderCommand cmd, CancellationToken ct = default)
    {
        _logger.LogInformation("[{Node}] Processing order: {ProductId} x {Quantity}",
            Environment.MachineName, cmd.ProductId, cmd.Quantity);

        var orderId = Guid.NewGuid().ToString();
        return Task.FromResult(CatgaResult<OrderResponse>.Success(new(orderId, "Created")));
    }
}

// 订单发货事件 Handler（所有节点接收）
public class OrderShippedEventHandler : IEventHandler<OrderShippedEvent>
{
    private readonly ILogger<OrderShippedEventHandler> _logger;

    public OrderShippedEventHandler(ILogger<OrderShippedEventHandler> logger) => _logger = logger;

    public Task HandleAsync(OrderShippedEvent evt, CancellationToken ct = default)
    {
        _logger.LogInformation("[{Node}] Order shipped: {OrderId}",
            Environment.MachineName, evt.OrderId);

        // TODO: 更新本地缓存、发送通知等
        return Task.CompletedTask;
    }
}
