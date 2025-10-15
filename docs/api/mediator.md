# ICatgaMediator

核心调度器接口，用于发送命令、查询和发布事件。

## 命名空间

```csharp
Catga
```

## 接口定义

```csharp
public interface ICatgaMediator
{
    Task<CatgaResult<TResponse>> SendAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>;

    Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent;
}
```

## 方法

### SendAsync

发送请求（命令或查询）并等待响应。

**签名**

```csharp
Task<CatgaResult<TResponse>> SendAsync<TRequest, TResponse>(
    TRequest request,
    CancellationToken cancellationToken = default)
    where TRequest : IRequest<TResponse>
```

**类型参数**

- `TRequest` - 请求类型，必须实现 `IRequest<TResponse>`
- `TResponse` - 响应类型

**参数**

- `request` - 要发送的请求对象
- `cancellationToken` - 取消令牌（可选）

**返回值**

返回 `Task<CatgaResult<TResponse>>`，包含操作结果。

**示例**

```csharp
public class CreateOrderCommand : MessageBase, IRequest<OrderResult>
{
    public string ProductId { get; init; } = string.Empty;
    public int Quantity { get; init; }
}

public class OrderResult
{
    public string OrderId { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
}

// 使用
var command = new CreateOrderCommand
{
    ProductId = "PROD-001",
    Quantity = 2
};

var result = await mediator.SendAsync<CreateOrderCommand, OrderResult>(command);

if (result.IsSuccess)
{
    Console.WriteLine($"Order created: {result.Value.OrderId}");
}
else
{
    Console.WriteLine($"Error: {result.Error}");
}
```

### PublishAsync

发布事件到所有订阅的处理器。

**签名**

```csharp
Task<CatgaResult> PublishAsync<TEvent>(
    TEvent @event,
    CancellationToken cancellationToken = default)
    where TEvent : IEvent
```

**类型参数**

- `TEvent` - 事件类型，必须实现 `IEvent`

**参数**

- `@event` - 要发布的事件对象
- `cancellationToken` - 取消令牌（可选）

**返回值**

返回 `Task<CatgaResult>`，表示发布操作的结果。

**示例**

```csharp
public class OrderCreatedEvent : EventBase
{
    public string OrderId { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
}

// 使用
var @event = new OrderCreatedEvent
{
    OrderId = "ORD-12345",
    TotalAmount = 199.99m
};

var result = await mediator.PublishAsync(@event);

if (result.IsSuccess)
{
    Console.WriteLine("Event published successfully");
}
```

## CatgaMediator

`ICatgaMediator` 的默认实现。

### 构造函数

```csharp
public CatgaMediator(
    IServiceProvider serviceProvider,
    ILogger<CatgaMediator> logger)
```

**参数**

- `serviceProvider` - 依赖注入服务提供器
- `logger` - 日志记录器

### 特性

- ✅ 自动从 DI 容器解析处理器
- ✅ 支持 Pipeline Behaviors
- ✅ 完整的日志记录
- ✅ 异常处理
- ✅ 100% AOT 兼容

### 内部实现

1. **处理器解析**
   ```csharp
   var handlerType = typeof(IRequestHandler<,>)
       .MakeGenericType(typeof(TRequest), typeof(TResponse));
   var handler = serviceProvider.GetService(handlerType);
   ```

2. **Pipeline 执行**
   - 按顺序执行所有注册的 Pipeline Behaviors
   - 最后执行实际的处理器

3. **异常处理**
   - 捕获并包装为 `CatgaResult`
   - 记录错误日志

## 依赖注入配置

```csharp
// 注册核心服务
services.AddCatga();

// 注册处理器
services.AddScoped<IRequestHandler<CreateOrderCommand, OrderResult>, CreateOrderHandler>();
services.AddScoped<IEventHandler<OrderCreatedEvent>, OrderCreatedEventHandler>();

// 注册 Pipeline Behaviors
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
```

## 最佳实践

### 1. 使用依赖注入

✅ **推荐**

```csharp
public class OrderController : ControllerBase
{
    private readonly ICatgaMediator _mediator;

    public OrderController(ICatgaMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder(CreateOrderCommand command)
    {
        var result = await _mediator.SendAsync<CreateOrderCommand, OrderResult>(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
}
```

❌ **不推荐**

```csharp
// 不要直接 new CatgaMediator
var mediator = new CatgaMediator(serviceProvider, logger);
```

### 2. 处理结果

✅ **推荐**

```csharp
var result = await mediator.SendAsync<GetOrderQuery, OrderDto>(query);

if (result.IsSuccess)
{
    // 处理成功情况
    var order = result.Value;
}
else
{
    // 处理错误情况
    _logger.LogError(result.Exception, result.Error);
}
```

### 3. 使用取消令牌

✅ **推荐**

```csharp
public async Task<IActionResult> GetOrder(
    string orderId,
    CancellationToken cancellationToken)
{
    var query = new GetOrderQuery { OrderId = orderId };
    var result = await _mediator.SendAsync<GetOrderQuery, OrderDto>(
        query,
        cancellationToken); // 传递取消令牌

    return Ok(result.Value);
}
```

## 性能特性

- **零分配**: 在热路径上避免不必要的分配
- **缓存优化**: 处理器类型信息被缓存
- **并发安全**: 线程安全的实现

## 相关文档

- [消息类型](messages.md)
- [处理器](handlers.md)
- [Pipeline Behaviors](pipeline.md)
- [结果类型](results.md)

