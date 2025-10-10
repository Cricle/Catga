using Catga;
using Catga.DependencyInjection;
using Catga.Handlers;
using Catga.Messages;
using Catga.Results;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ✨ Catga 核心
builder.Services.AddCatga();
builder.Services.AddGeneratedHandlers();

// 🆔 分布式 ID
builder.Services.AddDistributedId();

// 💾 Redis 连接
var redisConnection = ConnectionMultiplexer.Connect("localhost:6379");
builder.Services.AddSingleton<IConnectionMultiplexer>(redisConnection);

// 🔐 Redis 分布式锁
builder.Services.AddRedisDistributedLock();

// 📦 Redis 分布式缓存
builder.Services.AddRedisDistributedCache();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ==================== API 端点 ====================

// 创建订单（带分布式锁）
app.MapPost("/orders", async (ICatgaMediator mediator, CreateOrderCommand cmd) =>
{
    var result = await mediator.SendAsync<CreateOrderCommand, OrderResponse>(cmd);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
});

// 获取订单（带 Redis 缓存）
app.MapGet("/orders/{id}", async (ICatgaMediator mediator, string id) =>
{
    var result = await mediator.SendAsync<GetOrderQuery, OrderResponse>(new(id));
    return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound();
});

app.Run();

// ==================== 消息 ====================

public record CreateOrderCommand(string ProductId, int Quantity) : MessageBase, IRequest<OrderResponse>;
public record GetOrderQuery(string OrderId) : MessageBase, IRequest<OrderResponse>;
public record OrderResponse(string OrderId, string ProductId, int Quantity, decimal TotalPrice);

// ==================== Handler ====================

using Catga.DistributedLock;
using Catga.DistributedCache;

// 创建订单 Handler（使用分布式锁）
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
        // 🔐 使用分布式锁防止重复下单
        var lockKey = $"order:product:{cmd.ProductId}";
        await using var lockHandle = await _lock.AcquireAsync(lockKey, TimeSpan.FromSeconds(10), ct);

        if (lockHandle == null)
        {
            return CatgaResult<OrderResponse>.Failure("系统繁忙，请稍后重试");
        }

        _logger.LogInformation("Creating order for product: {ProductId}", cmd.ProductId);

        // TODO: 检查库存、扣减库存、创建订单
        var orderId = Guid.NewGuid().ToString();
        var totalPrice = 99.99m * cmd.Quantity;

        var response = new OrderResponse(orderId, cmd.ProductId, cmd.Quantity, totalPrice);
        return CatgaResult<OrderResponse>.Success(response);
    }
}

// 查询订单 Handler（使用分布式缓存）
[CatgaHandler(AutoRegister = true)]
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

        // TODO: 从数据库查询
        var response = new OrderResponse(query.OrderId, "PROD-001", 2, 199.98m);

        // 💾 写入缓存（1小时过期）
        await _cache.SetAsync(cacheKey, response, TimeSpan.FromHours(1), ct);

        return CatgaResult<OrderResponse>.Success(response);
    }
}

