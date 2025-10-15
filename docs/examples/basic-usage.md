# 基本使用示例

本文档展示 Catga 的基本使用方法。

## 目录

- [简单的命令](#简单的命令)
- [查询数据](#查询数据)
- [发布事件](#发布事件)
- [错误处理](#错误处理)
- [Pipeline Behaviors](#pipeline-behaviors)

## 简单的命令

### 1. 定义命令和响应

```csharp
using Catga.Messages;

namespace MyApp.Orders.Commands
{
    // 命令
    public record CreateOrderCommand : MessageBase, IRequest<CreateOrderResult>
    {
        public string CustomerId { get; init; } = string.Empty;
        public string ProductId { get; init; } = string.Empty;
        public int Quantity { get; init; }
    }

    // 响应
    public record CreateOrderResult
    {
        public string OrderId { get; init; } = string.Empty;
        public decimal TotalAmount { get; init; }
        public DateTime CreatedAt { get; init; }
    }
}
```

### 2. 实现处理器

```csharp
using Catga.Handlers;
using Catga.Results;

namespace MyApp.Orders.Handlers
{
    public class CreateOrderHandler
        : IRequestHandler<CreateOrderCommand, CreateOrderResult>
    {
        private readonly IOrderRepository _orderRepository;
        private readonly IProductRepository _productRepository;
        private readonly ILogger<CreateOrderHandler> _logger;

        public CreateOrderHandler(
            IOrderRepository orderRepository,
            IProductRepository productRepository,
            ILogger<CreateOrderHandler> logger)
        {
            _orderRepository = orderRepository;
            _productRepository = productRepository;
            _logger = logger;
        }

        public async Task<CatgaResult<CreateOrderResult>> HandleAsync(
            CreateOrderCommand request,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // 1. 验证产品
                var product = await _productRepository.GetByIdAsync(
                    request.ProductId,
                    cancellationToken);

                if (product == null)
                {
                    return CatgaResult<CreateOrderResult>.Failure(
                        "Product not found");
                }

                // 2. 检查库存
                if (product.Stock < request.Quantity)
                {
                    return CatgaResult<CreateOrderResult>.Failure(
                        "Insufficient stock");
                }

                // 3. 创建订单
                var order = new Order
                {
                    OrderId = Guid.NewGuid().ToString("N"),
                    CustomerId = request.CustomerId,
                    ProductId = request.ProductId,
                    Quantity = request.Quantity,
                    TotalAmount = product.Price * request.Quantity,
                    CreatedAt = DateTime.UtcNow
                };

                await _orderRepository.AddAsync(order, cancellationToken);

                // 4. 更新库存
                product.Stock -= request.Quantity;
                await _productRepository.UpdateAsync(product, cancellationToken);

                _logger.LogInformation(
                    "Order created: {OrderId}",
                    order.OrderId);

                // 5. 返回结果
                return CatgaResult<CreateOrderResult>.Success(
                    new CreateOrderResult
                    {
                        OrderId = order.OrderId,
                        TotalAmount = order.TotalAmount,
                        CreatedAt = order.CreatedAt
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create order");
                return CatgaResult<CreateOrderResult>.Failure(
                    "Failed to create order",
                    new CatgaException("Order creation failed", ex));
            }
        }
    }
}
```

### 3. 配置服务

```csharp
// Program.cs 或 Startup.cs
using Catga.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// 注册 Catga
builder.Services.AddCatga();

// 注册处理器
builder.Services.AddScoped<IRequestHandler<CreateOrderCommand, CreateOrderResult>,
    CreateOrderHandler>();

// 注册其他服务
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IProductRepository, ProductRepository>();

var app = builder.Build();
```

### 4. 使用 Mediator

```csharp
using Catga;
using Microsoft.AspNetCore.Mvc;

namespace MyApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
        private readonly ICatgaMediator _mediator;

        public OrdersController(ICatgaMediator mediator)
        {
            _mediator = mediator;
        }

        [HttpPost]
        public async Task<IActionResult> CreateOrder(
            [FromBody] CreateOrderCommand command,
            CancellationToken cancellationToken)
        {
            var result = await _mediator.SendAsync<CreateOrderCommand, CreateOrderResult>(
                command,
                cancellationToken);

            if (result.IsSuccess)
            {
                return Ok(result.Value);
            }

            return BadRequest(new
            {
                error = result.Error,
                details = result.Exception?.Message
            });
        }
    }
}
```

## 查询数据

### 1. 定义查询

```csharp
using Catga.Messages;

public record GetOrderQuery : MessageBase, IQuery<OrderDto>
{
    public string OrderId { get; init; } = string.Empty;
}

public record OrderDto
{
    public string OrderId { get; init; } = string.Empty;
    public string CustomerId { get; init; } = string.Empty;
    public string ProductId { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal TotalAmount { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}
```

### 2. 实现查询处理器

```csharp
public class GetOrderQueryHandler : IRequestHandler<GetOrderQuery, OrderDto>
{
    private readonly IOrderRepository _orderRepository;

    public GetOrderQueryHandler(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public async Task<CatgaResult<OrderDto>> HandleAsync(
        GetOrderQuery request,
        CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(
            request.OrderId,
            cancellationToken);

        if (order == null)
        {
            return CatgaResult<OrderDto>.Failure("Order not found");
        }

        var dto = new OrderDto
        {
            OrderId = order.OrderId,
            CustomerId = order.CustomerId,
            ProductId = order.ProductId,
            Quantity = order.Quantity,
            TotalAmount = order.TotalAmount,
            Status = order.Status,
            CreatedAt = order.CreatedAt
        };

        return CatgaResult<OrderDto>.Success(dto);
    }
}
```

### 3. 使用查询

```csharp
[HttpGet("{orderId}")]
public async Task<IActionResult> GetOrder(string orderId)
{
    var query = new GetOrderQuery { OrderId = orderId };
    var result = await _mediator.SendAsync<GetOrderQuery, OrderDto>(query);

    return result.IsSuccess
        ? Ok(result.Value)
        : NotFound(result.Error);
}
```

## 发布事件

### 1. 定义事件

```csharp
using Catga.Messages;

public record OrderCreatedEvent : EventBase
{
    public string OrderId { get; init; } = string.Empty;
    public string CustomerId { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
}
```

### 2. 实现事件处理器

```csharp
using Catga.Handlers;
using Catga.Results;

// 发送邮件通知
public class SendOrderEmailHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly IEmailService _emailService;
    private readonly ILogger<SendOrderEmailHandler> _logger;

    public SendOrderEmailHandler(
        IEmailService emailService,
        ILogger<SendOrderEmailHandler> logger)
    {
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<CatgaResult> HandleAsync(
        OrderCreatedEvent @event,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _emailService.SendOrderConfirmationAsync(
                @event.CustomerId,
                @event.OrderId,
                cancellationToken);

            _logger.LogInformation(
                "Order confirmation email sent for {OrderId}",
                @event.OrderId);

            return CatgaResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send order email");
            return CatgaResult.Failure("Failed to send email");
        }
    }
}

// 更新库存
public class UpdateInventoryHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly IInventoryService _inventoryService;

    public UpdateInventoryHandler(IInventoryService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    public async Task<CatgaResult> HandleAsync(
        OrderCreatedEvent @event,
        CancellationToken cancellationToken = default)
    {
        await _inventoryService.ReserveStockAsync(
            @event.OrderId,
            cancellationToken);

        return CatgaResult.Success();
    }
}
```

### 3. 发布事件

```csharp
// 在命令处理器中发布事件
public async Task<CatgaResult<CreateOrderResult>> HandleAsync(
    CreateOrderCommand request,
    CancellationToken cancellationToken = default)
{
    // ... 创建订单 ...

    // 发布事件
    var @event = new OrderCreatedEvent
    {
        OrderId = order.OrderId,
        CustomerId = order.CustomerId,
        TotalAmount = order.TotalAmount
    };

    await _mediator.PublishAsync(@event, cancellationToken);

    return CatgaResult<CreateOrderResult>.Success(result);
}
```

## 错误处理

### 方式 1: 返回失败结果

```csharp
public async Task<CatgaResult<OrderDto>> HandleAsync(
    GetOrderQuery request,
    CancellationToken cancellationToken = default)
{
    var order = await _orderRepository.GetByIdAsync(request.OrderId);

    if (order == null)
    {
        // 返回失败结果
        return CatgaResult<OrderDto>.Failure("Order not found");
    }

    return CatgaResult<OrderDto>.Success(MapToDto(order));
}
```

### 方式 2: 使用异常

```csharp
public async Task<CatgaResult<OrderDto>> HandleAsync(
    GetOrderQuery request,
    CancellationToken cancellationToken = default)
{
    try
    {
        var order = await _orderRepository.GetByIdAsync(request.OrderId);
        return CatgaResult<OrderDto>.Success(MapToDto(order));
    }
    catch (OrderNotFoundException ex)
    {
        return CatgaResult<OrderDto>.Failure(
            "Order not found",
            new CatgaException("Order not found", ex));
    }
}
```

### 方式 3: 在控制器中处理

```csharp
[HttpGet("{orderId}")]
public async Task<IActionResult> GetOrder(string orderId)
{
    var query = new GetOrderQuery { OrderId = orderId };
    var result = await _mediator.SendAsync<GetOrderQuery, OrderDto>(query);

    if (result.IsSuccess)
    {
        return Ok(result.Value);
    }

    // 根据错误类型返回不同的状态码
    if (result.Error.Contains("not found", StringComparison.OrdinalIgnoreCase))
    {
        return NotFound(result.Error);
    }

    return BadRequest(result.Error);
}
```

## Pipeline Behaviors

### 添加日志记录

```csharp
builder.Services.AddCatga(options =>
{
    options.AddLogging();
    options.AddTracing();
});
```

### 添加验证

```csharp
using System.ComponentModel.DataAnnotations;

public record CreateOrderCommand : MessageBase, IRequest<CreateOrderResult>
{
    [Required]
    [StringLength(50)]
    public string CustomerId { get; init; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string ProductId { get; init; } = string.Empty;

    [Range(1, 1000)]
    public int Quantity { get; init; }
}

// 启用验证
builder.Services.AddCatga(options =>
{
    options.AddValidation();
});
```

### 添加重试

```csharp
builder.Services.AddCatga(options =>
{
    options.AddRetry(maxAttempts: 3);
});
```

## 完整示例

参考 [examples](../../examples/) 文件夹中的完整示例项目。

## 相关文档

- [API 参考](../api/)
- [高级用法](advanced-usage.md)
- [最佳实践](../guides/best-practices.md)

