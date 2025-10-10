using Catga;
using Catga.DependencyInjection;
using Catga.Handlers;
using Catga.Messages;
using Catga.Results;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ✨ Catga - 只需 2 行！
builder.Services.AddCatga();
builder.Services.AddGeneratedHandlers();  // 自动发现并注册所有 Handler ✨

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ==================== API 端点 ====================

// 创建订单
app.MapPost("/orders", async (ICatgaMediator mediator, CreateOrderCommand cmd) =>
{
    var result = await mediator.SendAsync<CreateOrderCommand, OrderResponse>(cmd);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
});

// 获取订单
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
// 🎯 所有 Handler 自动发现并注册 - 零配置！

// 创建订单 Handler（自动注册）
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderResponse>
{
    private readonly ILogger<CreateOrderHandler> _logger;

    public CreateOrderHandler(ILogger<CreateOrderHandler> logger) => _logger = logger;

    public Task<CatgaResult<OrderResponse>> HandleAsync(CreateOrderCommand cmd, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating order for product: {ProductId}", cmd.ProductId);

        // TODO: 检查库存、扣减库存、创建订单
        var orderId = Guid.NewGuid().ToString();
        var totalPrice = 99.99m * cmd.Quantity;

        var response = new OrderResponse(orderId, cmd.ProductId, cmd.Quantity, totalPrice);
        return Task.FromResult(CatgaResult<OrderResponse>.Success(response));
    }
}

// 查询订单 Handler（自动注册）
public class GetOrderHandler : IRequestHandler<GetOrderQuery, OrderResponse>
{
    private readonly ILogger<GetOrderHandler> _logger;

    public GetOrderHandler(ILogger<GetOrderHandler> logger) => _logger = logger;

    public Task<CatgaResult<OrderResponse>> HandleAsync(GetOrderQuery query, CancellationToken ct = default)
    {
        _logger.LogInformation("Getting order: {OrderId}", query.OrderId);

        // TODO: 从数据库查询
        var response = new OrderResponse(query.OrderId, "PROD-001", 2, 199.98m);
        return Task.FromResult(CatgaResult<OrderResponse>.Success(response));
    }
}
