using Catga;
using Catga.DependencyInjection;
using Catga.Handlers;
using Catga.Messages;
using Catga.Results;
using Catga.Transport.Nats;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ✨ Catga
builder.Services.AddCatga();
builder.Services.AddGeneratedHandlers();

// 🚀 NATS 传输（可选 - 连接失败会优雅降级）
var natsUrl = builder.Configuration.GetValue<string>("Nats:Url") ?? "nats://localhost:4222";
try
{
    builder.Services.AddNatsTransport(options =>
    {
        options.Url = natsUrl;
        options.SubjectPrefix = "catga.";
    });
    Console.WriteLine($"✅ NATS connected: {natsUrl}");
}
catch (Exception ex)
{
    Console.WriteLine($"⚠️  NATS unavailable: {ex.Message}");
}

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();

// 创建订单（跨节点负载均衡）
app.MapPost("/orders", async (ICatgaMediator mediator, CreateOrderCommand cmd) =>
{
    var result = await mediator.SendAsync<CreateOrderCommand, OrderResponse>(cmd);
    return result.IsSuccess ? Results.Created($"/orders/{result.Value!.OrderId}", result.Value) : Results.BadRequest(result.Error);
});

// 订单发货（事件广播到所有节点）
app.MapPost("/orders/{id}/ship", async (ICatgaMediator mediator, string id) =>
{
    await mediator.PublishAsync(new OrderShippedEvent(id));
    return Results.Ok(new { Message = "事件已广播", Node = Environment.MachineName });
});

app.Run();

// ==================== 消息 ====================

public record CreateOrderCommand(string ProductId, int Quantity) : IRequest<OrderResponse>;
public record OrderResponse(string OrderId, string Status, string ProcessedBy);
public record OrderShippedEvent(string OrderId) : IEvent;

// ==================== Handler（源生成器自动注册，跨节点负载均衡）====================

public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderResponse>
{
    private readonly ILogger<CreateOrderHandler> _logger;

    public CreateOrderHandler(ILogger<CreateOrderHandler> logger) => _logger = logger;

    public Task<CatgaResult<OrderResponse>> HandleAsync(CreateOrderCommand cmd, CancellationToken ct = default)
    {
        var nodeName = Environment.MachineName;
        _logger.LogInformation("[{Node}] Processing: {ProductId} x {Quantity}", nodeName, cmd.ProductId, cmd.Quantity);

        if (cmd.Quantity <= 0)
            return Task.FromResult(CatgaResult<OrderResponse>.Failure("数量必须大于0"));

        var orderId = Guid.NewGuid().ToString();
        return Task.FromResult(CatgaResult<OrderResponse>.Success(new OrderResponse(orderId, "Created", nodeName)));
    }
}

public class OrderShippedEventHandler : IEventHandler<OrderShippedEvent>
{
    private readonly ILogger<OrderShippedEventHandler> _logger;

    public OrderShippedEventHandler(ILogger<OrderShippedEventHandler> logger) => _logger = logger;

    public Task HandleAsync(OrderShippedEvent evt, CancellationToken ct = default)
    {
        var nodeName = Environment.MachineName;
        _logger.LogInformation("[{Node}] 📦 Order shipped: {OrderId}", nodeName, evt.OrderId);
        return Task.CompletedTask;
    }
}
