using Catga;
using Catga.DependencyInjection;
using Catga.Handlers;
using Catga.Messages;
using Catga.Results;
using Catga.Transport.Nats;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ✨ Catga with Production Defaults
builder.Services.AddCatga(options =>
{
    options.EnableCircuitBreaker = true;
    options.EnableRetry = true;
    options.EnableRateLimiting = true;
    options.MaxConcurrentRequests = 100;
});
builder.Services.AddGeneratedHandlers();

// 🚀 NATS 传输（跨节点通信）with fault tolerance
var natsUrl = builder.Configuration.GetValue<string>("Nats:Url") ?? "nats://localhost:4222";
try
{
    builder.Services.AddNatsTransport(options =>
    {
        options.Url = natsUrl;
        options.SubjectPrefix = "catga.";
    });
    
    Console.WriteLine($"✅ Connected to NATS: {natsUrl}");
}
catch (Exception ex)
{
    Console.WriteLine($"⚠️  NATS connection failed: {ex.Message}. Running in standalone mode...");
}

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();

// API with error handling
app.MapPost("/orders", async (ICatgaMediator mediator, CreateOrderCommand cmd) =>
{
    var result = await mediator.SendAsync<CreateOrderCommand, OrderResponse>(cmd);
    
    if (!result.IsSuccess)
    {
        if (result.DetailedError != null)
        {
            return result.DetailedError.Category switch
            {
                ErrorCategory.System when result.DetailedError.Code == CatgaErrorCodes.CircuitBreakerOpen
                    => Results.StatusCode(503),  // Service Unavailable
                ErrorCategory.System when result.DetailedError.Code == CatgaErrorCodes.RateLimitExceeded
                    => Results.StatusCode(429),  // Too Many Requests
                _ => Results.Problem(result.DetailedError.Message)
            };
        }
        
        return Results.BadRequest(result.Error);
    }
    
    return Results.Created($"/orders/{result.Value!.OrderId}", result.Value);
});

app.MapPost("/orders/{id}/ship", async (ICatgaMediator mediator, string id) =>
{
    try
    {
        await mediator.PublishAsync(new OrderShippedEvent(id));
        return Results.Ok(new { 
            Message = $"事件已发布到所有节点",
            Node = Environment.MachineName,
            Timestamp = DateTime.UtcNow
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            detail: ex.Message,
            title: "Failed to publish event",
            statusCode: 500
        );
    }
});

// 健康检查（包括 NATS 连接状态）
app.MapGet("/health", () => 
{
    var health = new
    {
        Status = "Healthy",
        Node = Environment.MachineName,
        Version = "1.0.0",
        Timestamp = DateTime.UtcNow
    };
    
    return Results.Ok(health);
});

// 节点信息
app.MapGet("/node-info", () => Results.Ok(new
{
    NodeId = Environment.MachineName,
    ProcessId = Environment.ProcessId,
    StartTime = DateTime.UtcNow,
    Environment = builder.Environment.EnvironmentName
}));

app.Run();

// ==================== 消息 ====================

public record CreateOrderCommand(string ProductId, int Quantity) : MessageBase, IRequest<OrderResponse>;
public record OrderResponse(string OrderId, string Status, string ProcessedBy);
public record OrderShippedEvent(string OrderId) : EventBase;

// ==================== Handler（自动注册，跨节点负载均衡）====================

public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderResponse>
{
    private readonly ILogger<CreateOrderHandler> _logger;

    public CreateOrderHandler(ILogger<CreateOrderHandler> logger) => _logger = logger;

    public Task<CatgaResult<OrderResponse>> HandleAsync(CreateOrderCommand cmd, CancellationToken ct = default)
    {
        var nodeName = Environment.MachineName;
        _logger.LogInformation("[{Node}] Processing order: {ProductId} x {Quantity}", 
            nodeName, cmd.ProductId, cmd.Quantity);

        // 验证
        if (cmd.Quantity <= 0)
        {
            return Task.FromResult(CatgaResult<OrderResponse>.Failure(
                CatgaError.Validation("ORD_001", "数量必须大于0")
            ));
        }

        // 模拟业务处理
        var orderId = Guid.NewGuid().ToString();
        
        _logger.LogInformation("[{Node}] Order created: {OrderId}", nodeName, orderId);
        
        return Task.FromResult(CatgaResult<OrderResponse>.Success(
            new OrderResponse(orderId, "Created", nodeName)
        ));
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
        
        // 每个节点都会收到此事件，可以执行：
        // - 更新本地缓存
        // - 发送通知
        // - 记录日志
        // - 触发其他业务流程
        
        return Task.CompletedTask;
    }
}
