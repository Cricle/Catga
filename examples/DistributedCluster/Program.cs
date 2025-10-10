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
app.UseSwagger();
app.UseSwaggerUI();

// API
app.MapPost("/orders", async (ICatgaMediator mediator, CreateOrderCommand cmd) =>
    await mediator.SendAsync<CreateOrderCommand, OrderResponse>(cmd) is var result && result.IsSuccess
        ? Results.Ok(result.Value)
        : Results.BadRequest(result.Error));

app.MapPost("/orders/{id}/ship", async (ICatgaMediator mediator, string id) =>
{
    await mediator.PublishAsync(new OrderShippedEvent(id));
    return Results.Ok(new { Message = $"事件已发布到所有节点" });
});

app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Node = Environment.MachineName }));

app.Run();

// ==================== 消息 ====================

public record CreateOrderCommand(string ProductId, int Quantity) : MessageBase, IRequest<OrderResponse>;
public record OrderResponse(string OrderId, string Status);
public record OrderShippedEvent(string OrderId) : EventBase;

// ==================== Handler（自动注册，跨节点负载均衡）====================

public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderResponse>
{
    private readonly ILogger<CreateOrderHandler> _logger;
    public CreateOrderHandler(ILogger<CreateOrderHandler> logger) => _logger = logger;

    public Task<CatgaResult<OrderResponse>> HandleAsync(CreateOrderCommand cmd, CancellationToken ct = default)
    {
        _logger.LogInformation("[{Node}] Processing order: {ProductId}", Environment.MachineName, cmd.ProductId);
        var orderId = Guid.NewGuid().ToString();
        return Task.FromResult(CatgaResult<OrderResponse>.Success(new(orderId, "Created")));
    }
}

public class OrderShippedEventHandler : IEventHandler<OrderShippedEvent>
{
    private readonly ILogger<OrderShippedEventHandler> _logger;
    public OrderShippedEventHandler(ILogger<OrderShippedEventHandler> logger) => _logger = logger;

    public Task HandleAsync(OrderShippedEvent evt, CancellationToken ct = default)
    {
        _logger.LogInformation("[{Node}] Order shipped: {OrderId}", Environment.MachineName, evt.OrderId);
        return Task.CompletedTask;
    }
}
