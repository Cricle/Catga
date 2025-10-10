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

// ✨ Catga with Production Defaults
builder.Services.AddCatga(options =>
{
    options.EnableCircuitBreaker = true;
    options.EnableRetry = true;
    options.EnableRateLimiting = true;
});
builder.Services.AddGeneratedHandlers();

// 💾 Redis 连接
var redisConnection = builder.Configuration.GetValue<string>("Redis:Connection") ?? "localhost:6379";
try
{
    var redis = ConnectionMultiplexer.Connect(redisConnection);
    builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

    // 🔐 Redis 分布式锁
    builder.Services.AddRedisDistributedLock();

    // 📦 Redis 分布式缓存
    builder.Services.AddRedisDistributedCache();
}
catch (Exception ex)
{
    Console.WriteLine($"⚠️  Redis connection failed: {ex.Message}. Running without Redis...");
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
                ErrorCategory.System => Results.StatusCode(503),  // Service Unavailable
                ErrorCategory.Validation => Results.BadRequest(new { 
                    error = result.DetailedError.Code,
                    message = result.DetailedError.Message
                }),
                _ => Results.Problem(result.DetailedError.Message)
            };
        }
        
        return Results.BadRequest(result.Error);
    }
    
    return Results.Created($"/orders/{result.Value!.OrderId}", result.Value);
});

app.MapGet("/orders/{id}", async (ICatgaMediator mediator, string id) =>
{
    var result = await mediator.SendAsync<GetOrderQuery, OrderResponse>(new(id));
    
    if (!result.IsSuccess)
    {
        if (result.DetailedError?.Category == ErrorCategory.NotFound)
        {
            return Results.NotFound(new { 
                error = result.DetailedError.Code,
                message = result.DetailedError.Message
            });
        }
        
        return Results.Problem(result.Error);
    }
    
    return Results.Ok(result.Value);
});

// 删除缓存（演示缓存失效）
app.MapDelete("/orders/{id}/cache", async (IDistributedCache cache, string id) =>
{
    var cacheKey = $"order:{id}";
    await cache.RemoveAsync(cacheKey);
    return Results.Ok(new { message = $"Cache for order {id} invalidated" });
});

app.Run();

// ==================== 消息 ====================

public record CreateOrderCommand(string ProductId, int Quantity) : MessageBase, IRequest<OrderResponse>;
public record GetOrderQuery(string OrderId) : MessageBase, IRequest<OrderResponse>;
public record OrderResponse(string OrderId, string ProductId, int Quantity, decimal TotalPrice, bool FromCache = false);

// ==================== Handler（自动注册）====================

// 创建订单 - 使用分布式锁防止重复下单
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
        // 验证
        if (cmd.Quantity <= 0)
        {
            return CatgaResult<OrderResponse>.Failure(
                CatgaError.Validation("ORD_001", "数量必须大于0")
            );
        }

        // 🔐 使用分布式锁（如果可用）
        if (_lock != null)
        {
            var lockKey = $"order:product:{cmd.ProductId}";
            await using var lockHandle = await _lock.TryAcquireAsync(lockKey, TimeSpan.FromSeconds(10), ct);

            if (lockHandle == null)
            {
                _logger.LogWarning("Failed to acquire lock for product: {ProductId}", cmd.ProductId);
                return CatgaResult<OrderResponse>.Failure(
                    CatgaError.System("ORD_002", "系统繁忙，请稍后重试", "Failed to acquire distributed lock")
                );
            }

            _logger.LogInformation("Lock acquired for product: {ProductId}", cmd.ProductId);
        }

        _logger.LogInformation("Creating order for product: {ProductId}", cmd.ProductId);

        // 模拟业务逻辑：检查库存、扣减库存、创建订单
        var orderId = Guid.NewGuid().ToString();
        var totalPrice = 99.99m * cmd.Quantity;

        return CatgaResult<OrderResponse>.Success(
            new OrderResponse(orderId, cmd.ProductId, cmd.Quantity, totalPrice));
    }
}

// 查询订单 - 使用分布式缓存提升性能
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

        // 📦 先从缓存读取（如果可用）
        if (_cache != null)
        {
            try
            {
                var cached = await _cache.GetAsync<OrderResponse>(cacheKey, ct);
                if (cached != null)
                {
                    _logger.LogInformation("✨ Order found in cache: {OrderId}", query.OrderId);
                    return CatgaResult<OrderResponse>.Success(cached with { FromCache = true });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache read failed, falling back to database");
            }
        }

        _logger.LogInformation("Order not in cache, querying database: {OrderId}", query.OrderId);

        // 模拟：订单不存在
        if (query.OrderId == "999")
        {
            return CatgaResult<OrderResponse>.Failure(
                CatgaError.NotFound("ORD_003", $"订单 '{query.OrderId}' 不存在")
            );
        }

        // 从数据库查询
        var response = new OrderResponse(query.OrderId, "PROD-001", 2, 199.98m, FromCache: false);

        // 💾 写入缓存（1小时过期，如果可用）
        if (_cache != null)
        {
            try
            {
                await _cache.SetAsync(cacheKey, response, TimeSpan.FromHours(1), ct);
                _logger.LogInformation("Order cached: {OrderId}", query.OrderId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache write failed, but operation succeeded");
            }
        }

        return CatgaResult<OrderResponse>.Success(response);
    }
}
