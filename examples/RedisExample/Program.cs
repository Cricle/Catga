using Catga;
using Catga.Caching;
using Catga.DependencyInjection;
using Catga.DistributedLock;
using Catga.Handlers;
using Catga.Messages;
using Catga.Persistence.Redis.DependencyInjection;
using Catga.Results;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ✨ Catga
builder.Services.AddCatga();
builder.Services.AddGeneratedHandlers();

// 💾 Redis（可选 - 连接失败会优雅降级）
var redisConnection = builder.Configuration.GetValue<string>("Redis:Connection") ?? "localhost:6379";
try
{
    var redis = ConnectionMultiplexer.Connect(redisConnection);
    builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
    builder.Services.AddRedisDistributedLock();
    builder.Services.AddRedisDistributedCache();
}
catch (Exception ex)
{
    Console.WriteLine($"⚠️  Redis unavailable: {ex.Message}");
}

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();

// 创建订单（使用分布式锁）
app.MapPost("/orders", async (ICatgaMediator mediator, CreateOrderCommand cmd) =>
{
    var result = await mediator.SendAsync<CreateOrderCommand, OrderResponse>(cmd);
    return result.IsSuccess ? Results.Created($"/orders/{result.Value!.OrderId}", result.Value) : Results.BadRequest(result.Error);
});

// 查询订单（使用缓存）
app.MapGet("/orders/{id}", async (ICatgaMediator mediator, string id) =>
{
    var result = await mediator.SendAsync<GetOrderQuery, OrderResponse>(new(id));
    return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(result.Error);
});

app.Run();

// ==================== 消息 ====================

public record CreateOrderCommand(string ProductId, int Quantity) : IRequest<OrderResponse>;
public record GetOrderQuery(string OrderId) : IRequest<OrderResponse>;
public record OrderResponse(string OrderId, string ProductId, int Quantity, decimal TotalPrice, bool FromCache = false);

// ==================== Handler（源生成器自动注册）====================

public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderResponse>
{
    private readonly ILogger<CreateOrderHandler> _logger;
    private readonly IDistributedLock? _lock;

    public CreateOrderHandler(ILogger<CreateOrderHandler> logger, IDistributedLock? distributedLock = null)
    {
        _logger = logger;
        _lock = distributedLock;
    }

    public async Task<CatgaResult<OrderResponse>> HandleAsync(CreateOrderCommand cmd, CancellationToken ct = default)
    {
        if (cmd.Quantity <= 0)
            return CatgaResult<OrderResponse>.Failure("数量必须大于0");

        // 🔐 使用分布式锁（如果可用）
        if (_lock != null)
        {
            var lockKey = $"order:product:{cmd.ProductId}";
            await using var handle = await _lock.TryAcquireAsync(lockKey, TimeSpan.FromSeconds(10), ct);
            if (handle == null)
                return CatgaResult<OrderResponse>.Failure("系统繁忙，请稍后重试");

            _logger.LogInformation("🔒 Lock acquired for: {ProductId}", cmd.ProductId);
        }

        var orderId = Guid.NewGuid().ToString();
        var totalPrice = 99.99m * cmd.Quantity;

        return CatgaResult<OrderResponse>.Success(new OrderResponse(orderId, cmd.ProductId, cmd.Quantity, totalPrice));
    }
}

public class GetOrderHandler : IRequestHandler<GetOrderQuery, OrderResponse>
{
    private readonly ILogger<GetOrderHandler> _logger;
    private readonly IDistributedCache? _cache;

    public GetOrderHandler(ILogger<GetOrderHandler> logger, IDistributedCache? cache = null)
    {
        _logger = logger;
        _cache = cache;
    }

    public async Task<CatgaResult<OrderResponse>> HandleAsync(GetOrderQuery query, CancellationToken ct = default)
    {
        var cacheKey = $"order:{query.OrderId}";

        // 📦 先从缓存读取
        if (_cache != null)
        {
            var cached = await _cache.GetAsync<OrderResponse>(cacheKey, ct);
            if (cached != null)
            {
                _logger.LogInformation("✨ Cache hit: {OrderId}", query.OrderId);
                return CatgaResult<OrderResponse>.Success(cached with { FromCache = true });
            }
        }

        // 模拟数据库查询
        if (query.OrderId == "999")
            return CatgaResult<OrderResponse>.Failure($"订单 '{query.OrderId}' 不存在");

        var response = new OrderResponse(query.OrderId, "PROD-001", 2, 199.98m);

        // 💾 写入缓存
        if (_cache != null)
            await _cache.SetAsync(cacheKey, response, TimeSpan.FromHours(1), ct);

        return CatgaResult<OrderResponse>.Success(response);
    }
}
