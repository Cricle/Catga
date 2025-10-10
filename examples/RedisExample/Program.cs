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

// 💾 Redis 连接
var redisConnection = builder.Configuration.GetValue<string>("Redis:Connection") ?? "localhost:6379";
var redis = ConnectionMultiplexer.Connect(redisConnection);
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

// 🔐 Redis 分布式锁
builder.Services.AddRedisDistributedLock();

// 📦 Redis 分布式缓存
builder.Services.AddRedisDistributedCache();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();

// API
app.MapPost("/orders", async (ICatgaMediator mediator, CreateOrderCommand cmd) =>
    await mediator.SendAsync<CreateOrderCommand, OrderResponse>(cmd) is var result && result.IsSuccess
        ? Results.Ok(result.Value)
        : Results.BadRequest(result.Error));

app.MapGet("/orders/{id}", async (ICatgaMediator mediator, string id) =>
    await mediator.SendAsync<GetOrderQuery, OrderResponse>(new(id)) is var result && result.IsSuccess
        ? Results.Ok(result.Value)
        : Results.NotFound());

app.Run();

// ==================== 消息 ====================

public record CreateOrderCommand(string ProductId, int Quantity) : MessageBase, IRequest<OrderResponse>;
public record GetOrderQuery(string OrderId) : MessageBase, IRequest<OrderResponse>;
public record OrderResponse(string OrderId, string ProductId, int Quantity, decimal TotalPrice);

// ==================== Handler（自动注册）====================

// 创建订单 - 使用分布式锁防止重复下单
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderResponse>
{
    private readonly ILogger<CreateOrderHandler> _logger;
    private readonly IDistributedLock _lock;

    public CreateOrderHandler(ILogger<CreateOrderHandler> logger, IDistributedLock distributedLock)
    {
        _logger = logger;
        _lock = distributedLock;
    }

    public async Task<CatgaResult<OrderResponse>> HandleAsync(CreateOrderCommand cmd, CancellationToken ct = default)
    {
        // 🔐 使用分布式锁防止并发问题
        var lockKey = $"order:product:{cmd.ProductId}";
        await using var lockHandle = await _lock.TryAcquireAsync(lockKey, TimeSpan.FromSeconds(10), ct);

        if (lockHandle == null)
        {
            return CatgaResult<OrderResponse>.Failure("系统繁忙，请稍后重试");
        }

        _logger.LogInformation("Creating order for product: {ProductId}", cmd.ProductId);

        // 业务逻辑：检查库存、扣减库存、创建订单
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
    private readonly IDistributedCache _cache;

    public GetOrderHandler(ILogger<GetOrderHandler> logger, IDistributedCache cache)
    {
        _logger = logger;
        _cache = cache;
    }

    public async Task<CatgaResult<OrderResponse>> HandleAsync(GetOrderQuery query, CancellationToken ct = default)
    {
        var cacheKey = $"order:{query.OrderId}";

        // 📦 先从缓存读取
        var cached = await _cache.GetAsync<OrderResponse>(cacheKey, ct);
        if (cached != null)
        {
            _logger.LogInformation("Order found in cache: {OrderId}", query.OrderId);
            return CatgaResult<OrderResponse>.Success(cached);
        }

        _logger.LogInformation("Order not in cache, querying database: {OrderId}", query.OrderId);

        // 从数据库查询
        var response = new OrderResponse(query.OrderId, "PROD-001", 2, 199.98m);

        // 💾 写入缓存（1小时过期）
        await _cache.SetAsync(cacheKey, response, TimeSpan.FromHours(1), ct);

        return CatgaResult<OrderResponse>.Success(response);
    }
}
