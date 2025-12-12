# CQRS 架构详解

## CQRS 概述

CQRS (Command Query Responsibility Segregation) 是一种架构模式，它将应用程序分为两个独立的路径：
- **命令路径** (Command) - 处理写操作，改变系统状态
- **查询路径** (Query) - 处理读操作，返回数据

## Catga 中的 CQRS 实现

### 架构分层

```
┌─────────────────────────────────────────────────────┐
│                    应用层                            │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  │
│  │   Commands  │  │   Queries   │  │   Events    │  │
│  └─────────────┘  └─────────────┘  └─────────────┘  │
└─────────────────────────────────────────────────────┘
                         │
┌─────────────────────────────────────────────────────┐
│                 Catga 核心层                        │
│  ┌─────────────────────────────────────────────┐    │
│  │            ICatgaMediator                   │    │
│  │      (统一调度和协调中心)                    │    │
│  └─────────────────────────────────────────────┘    │
│                         │                           │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  │
│  │   Command   │  │    Query    │  │    Event    │  │
│  │  Handlers   │  │  Handlers   │  │  Handlers   │  │
│  └─────────────┘  └─────────────┘  └─────────────┘  │
└─────────────────────────────────────────────────────┘
                         │
┌─────────────────────────────────────────────────────┐
│                   数据层                            │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  │
│  │  Write DB   │  │   Read DB   │  │  Event Store│  │
│  │  (写模型)    │  │  (读模型)    │  │  (事件存储) │  │
│  └─────────────┘  └─────────────┘  └─────────────┘  │
└─────────────────────────────────────────────────────┘
```

## 消息类型设计

### 1. 基础消息接口

```csharp
public interface IMessage
{
    string MessageId { get; }
    string CorrelationId { get; }
    DateTime CreatedAt { get; }
}

public abstract record MessageBase : IMessage
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
```

### 2. 命令 (Commands)

命令表示用户的意图，用于改变系统状态：

```csharp
public interface IRequest<TResponse> : IRequest<TResponse> { }

// 示例：创建订单命令
public record CreateOrderCommand : MessageBase, IRequest<OrderCreatedResult>
{
    public string CustomerId { get; init; } = string.Empty;
    public string ProductId { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
}

public record OrderCreatedResult
{
    public string OrderId { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public DateTime CreatedAt { get; init; }
}
```

**命令特征**：
- ✅ 表达用户意图 ("Create Order", "Cancel Payment")
- ✅ 包含执行操作所需的所有数据
- ✅ 通常返回操作结果或确认信息
- ✅ 可能失败，需要错误处理

### 3. 查询 (Queries)

查询用于获取数据，不改变系统状态：

```csharp
public interface IQuery<TResponse> : IRequest<TResponse> { }

// 示例：获取订单查询
public record GetOrderByIdQuery : MessageBase, IQuery<OrderDto>
{
    public string OrderId { get; init; } = string.Empty;
}

// 复杂查询示例
public record GetOrdersQuery : MessageBase, IQuery<PagedResult<OrderSummaryDto>>
{
    public string? CustomerId { get; init; }
    public DateTime? FromDate { get; init; }
    public DateTime? ToDate { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string SortBy { get; init; } = "CreatedAt";
    public SortDirection SortDirection { get; init; } = SortDirection.Descending;
}
```

**查询特征**：
- ✅ 只读取数据，不修改状态
- ✅ 可以优化为专门的读模型
- ✅ 支持复杂的过滤和排序
- ✅ 通常不会失败（除了验证错误）

### 4. 事件 (Events)

事件表示已经发生的事情：

```csharp
public interface IEvent : IMessage
{
    DateTime OccurredAt { get; }
}

public abstract record EventBase : MessageBase, IEvent
{
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}

// 示例：订单创建事件
public record OrderCreatedEvent : EventBase
{
    public string OrderId { get; init; } = string.Empty;
    public string CustomerId { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public List<OrderItemDto> Items { get; init; } = new();
}
```

**事件特征**：
- ✅ 描述过去发生的事情 ("Order Created", "Payment Processed")
- ✅ 不可变，包含事件发生时的完整信息
- ✅ 可以有多个处理器
- ✅ 用于系统解耦和事件溯源

## 处理器实现模式

### 1. 命令处理器

```csharp
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderCreatedResult>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IProductRepository _productRepository;
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<CreateOrderHandler> _logger;

    public CreateOrderHandler(
        IOrderRepository orderRepository,
        IProductRepository productRepository,
        ICatgaMediator mediator,
        ILogger<CreateOrderHandler> logger)
    {
        _orderRepository = orderRepository;
        _productRepository = productRepository;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<CatgaResult<OrderCreatedResult>> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. 验证业务规则
            var product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);
            if (product == null)
                return CatgaResult<OrderCreatedResult>.Failure("Product not found");

            if (product.Stock < request.Quantity)
                return CatgaResult<OrderCreatedResult>.Failure("Insufficient stock");

            // 2. 执行业务逻辑
            var order = new Order
            {
                Id = Guid.NewGuid().ToString(),
                CustomerId = request.CustomerId,
                ProductId = request.ProductId,
                Quantity = request.Quantity,
                UnitPrice = request.UnitPrice,
                TotalAmount = request.Quantity * request.UnitPrice,
                Status = OrderStatus.Created,
                CreatedAt = DateTime.UtcNow
            };

            await _orderRepository.AddAsync(order, cancellationToken);

            // 3. 更新相关数据
            product.Stock -= request.Quantity;
            await _productRepository.UpdateAsync(product, cancellationToken);

            // 4. 发布领域事件
            var orderCreatedEvent = new OrderCreatedEvent
            {
                OrderId = order.Id,
                CustomerId = order.CustomerId,
                TotalAmount = order.TotalAmount,
                Items = new List<OrderItemDto>
                {
                    new(request.ProductId, request.Quantity, request.UnitPrice)
                }
            };

            await _mediator.PublishAsync(orderCreatedEvent, cancellationToken);

            _logger.LogInformation("Order {OrderId} created successfully", order.Id);

            return CatgaResult<OrderCreatedResult>.Success(new OrderCreatedResult
            {
                OrderId = order.Id,
                TotalAmount = order.TotalAmount,
                CreatedAt = order.CreatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create order");
            return CatgaResult<OrderCreatedResult>.Failure("Failed to create order", ex);
        }
    }
}
```

### 2. 查询处理器

```csharp
public class GetOrderByIdHandler : IRequestHandler<GetOrderByIdQuery, OrderDto>
{
    private readonly IOrderReadRepository _readRepository;
    private readonly IMapper _mapper;

    public GetOrderByIdHandler(IOrderReadRepository readRepository, IMapper mapper)
    {
        _readRepository = readRepository;
        _mapper = mapper;
    }

    public async Task<CatgaResult<OrderDto>> HandleAsync(
        GetOrderByIdQuery request,
        CancellationToken cancellationToken = default)
    {
        var order = await _readRepository.GetByIdAsync(request.OrderId, cancellationToken);

        if (order == null)
            return CatgaResult<OrderDto>.Failure("Order not found");

        var dto = _mapper.Map<OrderDto>(order);
        return CatgaResult<OrderDto>.Success(dto);
    }
}

// 复杂查询处理器
public class GetOrdersHandler : IRequestHandler<GetOrdersQuery, PagedResult<OrderSummaryDto>>
{
    private readonly IOrderReadRepository _readRepository;

    public async Task<CatgaResult<PagedResult<OrderSummaryDto>>> HandleAsync(
        GetOrdersQuery request,
        CancellationToken cancellationToken = default)
    {
        var specification = new OrderSpecification()
            .WithCustomerId(request.CustomerId)
            .WithDateRange(request.FromDate, request.ToDate)
            .WithPagination(request.PageNumber, request.PageSize)
            .WithSorting(request.SortBy, request.SortDirection);

        var orders = await _readRepository.GetPagedAsync(specification, cancellationToken);
        var totalCount = await _readRepository.CountAsync(specification, cancellationToken);

        var result = new PagedResult<OrderSummaryDto>
        {
            Items = orders.Select(o => new OrderSummaryDto
            {
                OrderId = o.Id,
                CustomerId = o.CustomerId,
                TotalAmount = o.TotalAmount,
                Status = o.Status.ToString(),
                CreatedAt = o.CreatedAt
            }).ToList(),
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize
        };

        return CatgaResult<PagedResult<OrderSummaryDto>>.Success(result);
    }
}
```

### 3. 事件处理器

```csharp
// 发送邮件通知
public class OrderCreatedEmailHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly IEmailService _emailService;
    private readonly ICustomerRepository _customerRepository;

    public async Task<CatgaResult> HandleAsync(
        OrderCreatedEvent @event,
        CancellationToken cancellationToken = default)
    {
        var customer = await _customerRepository.GetByIdAsync(@event.CustomerId, cancellationToken);
        if (customer?.Email != null)
        {
            await _emailService.SendOrderConfirmationAsync(
                customer.Email,
                @event.OrderId,
                @event.TotalAmount,
                cancellationToken);
        }

        return CatgaResult.Success();
    }
}

// 更新统计信息
public class OrderCreatedStatsHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly IStatsService _statsService;

    public async Task<CatgaResult> HandleAsync(
        OrderCreatedEvent @event,
        CancellationToken cancellationToken = default)
    {
        await _statsService.IncrementOrderCountAsync(@event.CustomerId, cancellationToken);
        await _statsService.UpdateRevenueAsync(@event.TotalAmount, cancellationToken);

        return CatgaResult.Success();
    }
}
```

## 读写分离

### 写模型 (Write Model)

专注于业务逻辑和数据一致性：

```csharp
// 写模型实体 - 包含业务逻辑
public class Order : AggregateRoot
{
    public string Id { get; private set; }
    public string CustomerId { get; private set; }
    public OrderStatus Status { get; private set; }
    private readonly List<OrderItem> _items = new();
    public IReadOnlyList<OrderItem> Items => _items.AsReadOnly();

    public decimal TotalAmount => _items.Sum(item => item.TotalPrice);

    public void AddItem(string productId, int quantity, decimal unitPrice)
    {
        if (Status != OrderStatus.Draft)
            throw new InvalidOperationException("Cannot modify confirmed order");

        var existingItem = _items.FirstOrDefault(i => i.ProductId == productId);
        if (existingItem != null)
        {
            existingItem.UpdateQuantity(existingItem.Quantity + quantity);
        }
        else
        {
            _items.Add(new OrderItem(productId, quantity, unitPrice));
        }

        AddDomainEvent(new OrderItemAddedEvent(Id, productId, quantity));
    }

    public void Confirm()
    {
        if (Status != OrderStatus.Draft)
            throw new InvalidOperationException("Order is already confirmed");

        if (!_items.Any())
            throw new InvalidOperationException("Cannot confirm empty order");

        Status = OrderStatus.Confirmed;
        AddDomainEvent(new OrderConfirmedEvent(Id, TotalAmount));
    }
}
```

### 读模型 (Read Model)

优化查询性能：

```csharp
// 读模型 - 扁平化结构，优化查询
public class OrderReadModel
{
    public string Id { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }

    // 反规范化的数据
    public int ItemCount { get; set; }
    public string ProductNames { get; set; } = string.Empty; // 逗号分隔
    public List<OrderItemReadModel> Items { get; set; } = new();
}

public class OrderItemReadModel
{
    public string ProductId { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
}
```

## 数据一致性策略

### 1. 最终一致性

```csharp
// 事件处理器更新读模型
public class OrderReadModelUpdater :
    IEventHandler<OrderCreatedEvent>,
    IEventHandler<OrderConfirmedEvent>,
    IEventHandler<OrderCancelledEvent>
{
    private readonly IOrderReadRepository _readRepository;

    public async Task<CatgaResult> HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken)
    {
        var readModel = new OrderReadModel
        {
            Id = @event.OrderId,
            CustomerId = @event.CustomerId,
            TotalAmount = @event.TotalAmount,
            Status = "Created",
            CreatedAt = @event.OccurredAt,
            Items = @event.Items.Select(item => new OrderItemReadModel
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                TotalPrice = item.Quantity * item.UnitPrice
            }).ToList()
        };

        await _readRepository.UpsertAsync(readModel, cancellationToken);
        return CatgaResult.Success();
    }

    // 其他事件处理方法...
}
```

### 2. 分布式事务 (使用 CatGa)

```csharp
public class ProcessOrderSaga : ICatGaTransaction
{
    public async Task ExecuteAsync(CatGaContext context)
    {
        // 步骤1：创建订单
        var createOrderResult = await context.ExecuteAsync(
            new CreateOrderStep(context.GetInput<CreateOrderCommand>()));

        if (!createOrderResult.IsSuccess)
            return;

        var orderId = createOrderResult.Value.OrderId;
        context.SetCompensation(() => new CancelOrderStep(orderId));

        // 步骤2：扣减库存
        await context.ExecuteAsync(new ReduceStockStep(orderId));
        context.SetCompensation(() => new RestoreStockStep(orderId));

        // 步骤3：处理支付
        await context.ExecuteAsync(new ProcessPaymentStep(orderId));
        context.SetCompensation(() => new RefundPaymentStep(orderId));
    }
}
```

## 性能优化

### 1. 查询优化

```csharp
// 使用投影避免加载不需要的数据
public class GetOrderSummariesHandler : IRequestHandler<GetOrderSummariesQuery, List<OrderSummaryDto>>
{
    private readonly IOrderReadRepository _repository;

    public async Task<CatgaResult<List<OrderSummaryDto>>> HandleAsync(
        GetOrderSummariesQuery request,
        CancellationToken cancellationToken = default)
    {
        // 只查询需要的字段
        var summaries = await _repository.Query()
            .Where(o => o.CustomerId == request.CustomerId)
            .Select(o => new OrderSummaryDto
            {
                OrderId = o.Id,
                TotalAmount = o.TotalAmount,
                Status = o.Status,
                CreatedAt = o.CreatedAt
            })
            .OrderByDescending(o => o.CreatedAt)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return CatgaResult<List<OrderSummaryDto>>.Success(summaries);
    }
}
```

### 2. 缓存策略

```csharp
public class CachedProductQueryHandler : IRequestHandler<GetProductQuery, ProductDto>
{
    private readonly IProductRepository _repository;
    private readonly IDistributedCache _cache;
    private readonly ILogger<CachedProductQueryHandler> _logger;

    public async Task<CatgaResult<ProductDto>> HandleAsync(
        GetProductQuery request,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"product:{request.ProductId}";

        // 尝试从缓存获取
        var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
        if (cached != null)
        {
            var cachedProduct = JsonSerializer.Deserialize<ProductDto>(cached);
            _logger.LogDebug("Product {ProductId} retrieved from cache", request.ProductId);
            return CatgaResult<ProductDto>.Success(cachedProduct);
        }

        // 从数据库查询
        var product = await _repository.GetByIdAsync(request.ProductId, cancellationToken);
        if (product == null)
            return CatgaResult<ProductDto>.Failure("Product not found");

        var dto = new ProductDto
        {
            Id = product.Id,
            Name = product.Name,
            Price = product.Price,
            Stock = product.Stock
        };

        // 缓存结果
        var serialized = JsonSerializer.Serialize(dto);
        await _cache.SetStringAsync(cacheKey, serialized, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15)
        }, cancellationToken);

        return CatgaResult<ProductDto>.Success(dto);
    }
}
```

## 测试策略

### 1. 单元测试

```csharp
public class CreateOrderHandlerTests
{
    private readonly Mock<IOrderRepository> _orderRepository;
    private readonly Mock<IProductRepository> _productRepository;
    private readonly Mock<ICatgaMediator> _mediator;
    private readonly CreateOrderHandler _handler;

    public CreateOrderHandlerTests()
    {
        _orderRepository = new Mock<IOrderRepository>();
        _productRepository = new Mock<IProductRepository>();
        _mediator = new Mock<ICatgaMediator>();
        _handler = new CreateOrderHandler(_orderRepository.Object, _productRepository.Object, _mediator.Object, Mock.Of<ILogger<CreateOrderHandler>>());
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_ShouldCreateOrder()
    {
        // Arrange
        var command = new CreateOrderCommand
        {
            CustomerId = "CUST-001",
            ProductId = "PROD-001",
            Quantity = 2,
            UnitPrice = 100m
        };

        var product = new Product { Id = "PROD-001", Stock = 10 };
        _productRepository.Setup(x => x.GetByIdAsync(command.ProductId, default))
            .ReturnsAsync(product);

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalAmount.Should().Be(200m);

        _orderRepository.Verify(x => x.AddAsync(It.IsAny<Order>(), default), Times.Once);
        _mediator.Verify(x => x.PublishAsync(It.IsAny<OrderCreatedEvent>(), default), Times.Once);
    }
}
```

### 2. 集成测试

```csharp
public class OrderIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public OrderIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task CreateOrder_ShouldReturnSuccess()
    {
        // Arrange
        var command = new CreateOrderCommand
        {
            CustomerId = "CUST-001",
            ProductId = "PROD-001",
            Quantity = 1,
            UnitPrice = 99.99m
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/orders", command);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<OrderCreatedResult>();
        result.Should().NotBeNull();
        result.OrderId.Should().NotBeEmpty();
        result.TotalAmount.Should().Be(99.99m);
    }
}
```

## 最佳实践

### 1. 命令设计原则

- ✅ **单一职责**：一个命令只做一件事
- ✅ **包含完整信息**：命令应包含执行操作所需的所有数据
- ✅ **验证在边界**：在命令处理器中进行业务验证
- ✅ **幂等性**：同一命令多次执行应产生相同结果

### 2. 查询优化建议

- ✅ **专用 DTO**：为不同查询创建专用的 DTO
- ✅ **投影查询**：只查询需要的字段
- ✅ **分页处理**：大结果集必须分页
- ✅ **缓存策略**：缓存频繁查询的数据

### 3. 事件设计原则

- ✅ **描述过去**：事件名称应使用过去时
- ✅ **包含完整上下文**：事件应包含处理所需的所有信息
- ✅ **版本兼容**：考虑事件模式的向后兼容性
- ✅ **去重处理**：事件处理器应支持重复事件

这种 CQRS 架构设计确保了 Catga 既能处理复杂的业务逻辑，又能提供高性能的查询能力，同时保持良好的可维护性和可扩展性。


