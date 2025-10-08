using Catga;
using Catga.Configuration;
using Catga.DependencyInjection;
using Catga.Handlers;
using Catga.Messages;
using Catga.Results;
using Catga.Serialization.MemoryPack;
using Catga.Transport.Nats;
using MemoryPack;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 🎯 Configure Catga for Distributed Cluster
builder.Services.AddCatga(options =>
{
    options.EnableLogging = true;
    options.EnableIdempotency = true;  // Prevent duplicate message processing
});

// ✨ Use MemoryPack for high-performance serialization (AOT-friendly)
builder.Services.AddSingleton<Catga.Serialization.IMessageSerializer, MemoryPackMessageSerializer>();

// 🚀 Add NATS Transport for distributed messaging
builder.Services.AddNatsTransport(options =>
{
    options.Url = builder.Configuration.GetValue<string>("Nats:Url") ?? "nats://localhost:4222";
    options.SubjectPrefix = "catga.cluster.";
    options.EnableJetStream = true;
    options.StreamName = "CATGA_CLUSTER";
});

// 💾 Add Redis for Outbox/Inbox persistence (optional but recommended)
// builder.Services.AddRedisPersistence(options =>
// {
//     options.ConnectionString = builder.Configuration.GetValue<string>("Redis:Connection") ?? "localhost:6379";
// });

// ✨ Auto-register all handlers using source generator
builder.Services.AddGeneratedHandlers();

var app = builder.Build();

// Configure middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// 📝 API Endpoints

app.MapPost("/orders", async (ICatgaMediator mediator, CreateOrderCommand command) =>
{
    var result = await mediator.SendAsync<CreateOrderCommand, OrderCreatedResponse>(command);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
})
.WithName("CreateOrder")
.WithDescription("Create a new order - will be processed by this or another node in the cluster");

app.MapGet("/orders/{orderId}", async (ICatgaMediator mediator, string orderId) =>
{
    var query = new GetOrderQuery { OrderId = orderId };
    var result = await mediator.SendAsync<GetOrderQuery, OrderDto>(query);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound();
})
.WithName("GetOrder");

app.MapPost("/orders/{orderId}/ship", async (ICatgaMediator mediator, string orderId) =>
{
    var command = new ShipOrderCommand { OrderId = orderId };
    var result = await mediator.SendAsync<ShipOrderCommand, Unit>(command);
    return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
})
.WithName("ShipOrder");

app.MapGet("/health", () => Results.Ok(new
{
    Status = "Healthy",
    Node = Environment.MachineName,
    Version = "1.0.0"
}))
.WithName("HealthCheck");

app.Run();

// ========================
// Domain Messages
// ========================

[MemoryPackable]
public partial class CreateOrderCommand : IRequest<OrderCreatedResponse>
{
    public string MessageId { get; set; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public required string CustomerId { get; set; }
    public required List<OrderItemDto> Items { get; set; }
}

[MemoryPackable]
public partial class OrderItemDto
{
    public required string ProductId { get; set; }
    public required string ProductName { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

[MemoryPackable]
public partial class OrderCreatedResponse
{
    public required string OrderId { get; set; }
    public required string CustomerId { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAt { get; set; }
}

[MemoryPackable]
public partial class GetOrderQuery : IRequest<OrderDto>
{
    public string MessageId { get; set; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public required string OrderId { get; set; }
}

[MemoryPackable]
public partial class OrderDto
{
    public required string OrderId { get; set; }
    public required string CustomerId { get; set; }
    public required List<OrderItemDto> Items { get; set; }
    public decimal TotalAmount { get; set; }
    public required string Status { get; set; }
    public DateTime CreatedAt { get; set; }
}

[MemoryPackable]
public partial class ShipOrderCommand : IRequest<Unit>
{
    public string MessageId { get; set; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public required string OrderId { get; set; }
}

[MemoryPackable]
public partial class Unit
{
    public static readonly Unit Value = new();
}

// Domain Events (published via NATS to all nodes)
[MemoryPackable]
public partial class OrderCreatedEvent : IEvent
{
    public string MessageId { get; set; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    
    public required string OrderId { get; set; }
    public required string CustomerId { get; set; }
    public decimal TotalAmount { get; set; }
}

[MemoryPackable]
public partial class OrderShippedEvent : IEvent
{
    public string MessageId { get; set; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    
    public required string OrderId { get; set; }
    public DateTime ShippedAt { get; set; }
}

// ========================
// Handlers (Auto-discovered by Source Generator!)
// ========================

/// <summary>
/// Handles order creation - can run on any node in the cluster
/// </summary>
public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, OrderCreatedResponse>
{
    private readonly ILogger<CreateOrderCommandHandler> _logger;
    private readonly ICatgaMediator _mediator;
    
    // Simulate database - in production use real database
    private static readonly Dictionary<string, OrderDto> _orders = new();

    public CreateOrderCommandHandler(
        ILogger<CreateOrderCommandHandler> logger,
        ICatgaMediator mediator)
    {
        _logger = logger;
        _mediator = mediator;
    }

    public async Task<CatgaResult<OrderCreatedResponse>> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Node {Node} processing order creation for customer {CustomerId}",
            Environment.MachineName, request.CustomerId);

        var orderId = Guid.NewGuid().ToString();
        var totalAmount = request.Items.Sum(i => i.Quantity * i.UnitPrice);

        // Save order
        var order = new OrderDto
        {
            OrderId = orderId,
            CustomerId = request.CustomerId,
            Items = request.Items,
            TotalAmount = totalAmount,
            Status = "Created",
            CreatedAt = DateTime.UtcNow
        };
        
        _orders[orderId] = order;

        // Publish event to all nodes via NATS
        var @event = new OrderCreatedEvent
        {
            OrderId = orderId,
            CustomerId = request.CustomerId,
            TotalAmount = totalAmount,
            CorrelationId = request.CorrelationId
        };

        await _mediator.PublishAsync(@event, cancellationToken);

        _logger.LogInformation("Order {OrderId} created successfully on node {Node}", orderId, Environment.MachineName);

        return CatgaResult<OrderCreatedResponse>.Success(new OrderCreatedResponse
        {
            OrderId = orderId,
            CustomerId = request.CustomerId,
            TotalAmount = totalAmount,
            CreatedAt = order.CreatedAt
        });
    }
}

/// <summary>
/// Handles order queries - can run on any node
/// </summary>
public class GetOrderQueryHandler : IRequestHandler<GetOrderQuery, OrderDto>
{
    private readonly ILogger<GetOrderQueryHandler> _logger;
    
    // Simulate database - in production use real distributed database
    private static readonly Dictionary<string, OrderDto> _orders = new();

    public GetOrderQueryHandler(ILogger<GetOrderQueryHandler> logger)
    {
        _logger = logger;
    }

    public Task<CatgaResult<OrderDto>> HandleAsync(
        GetOrderQuery request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Node {Node} querying order {OrderId}",
            Environment.MachineName, request.OrderId);

        if (_orders.TryGetValue(request.OrderId, out var order))
        {
            return Task.FromResult(CatgaResult<OrderDto>.Success(order));
        }

        return Task.FromResult(CatgaResult<OrderDto>.Failure($"Order {request.OrderId} not found"));
    }
}

/// <summary>
/// Handles order shipping
/// </summary>
public class ShipOrderCommandHandler : IRequestHandler<ShipOrderCommand, Unit>
{
    private readonly ILogger<ShipOrderCommandHandler> _logger;
    private readonly ICatgaMediator _mediator;
    
    // Simulate database - in production use real distributed database
    private static readonly Dictionary<string, OrderDto> _orders = new();

    public ShipOrderCommandHandler(
        ILogger<ShipOrderCommandHandler> logger,
        ICatgaMediator mediator)
    {
        _logger = logger;
        _mediator = mediator;
    }

    public async Task<CatgaResult<Unit>> HandleAsync(
        ShipOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Node {Node} shipping order {OrderId}",
            Environment.MachineName, request.OrderId);

        if (!_orders.TryGetValue(request.OrderId, out var order))
        {
            return CatgaResult<Unit>.Failure($"Order {request.OrderId} not found");
        }

        order.Status = "Shipped";

        // Publish event to all nodes
        var @event = new OrderShippedEvent
        {
            OrderId = request.OrderId,
            ShippedAt = DateTime.UtcNow,
            CorrelationId = request.CorrelationId
        };

        await _mediator.PublishAsync(@event, cancellationToken);

        return CatgaResult<Unit>.Success(Unit.Value);
    }
}

/// <summary>
/// Event handler for order created - runs on ALL nodes via NATS pub/sub
/// </summary>
public class OrderCreatedEventHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly ILogger<OrderCreatedEventHandler> _logger;

    public OrderCreatedEventHandler(ILogger<OrderCreatedEventHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "📢 Node {Node} received OrderCreatedEvent: Order {OrderId} for customer {CustomerId}, Amount: {Amount:C}",
            Environment.MachineName, @event.OrderId, @event.CustomerId, @event.TotalAmount);

        // In production: update read models, send notifications, update analytics, etc.
        return Task.CompletedTask;
    }
}

/// <summary>
/// Event handler for order shipped - runs on ALL nodes
/// </summary>
public class OrderShippedEventHandler : IEventHandler<OrderShippedEvent>
{
    private readonly ILogger<OrderShippedEventHandler> _logger;

    public OrderShippedEventHandler(ILogger<OrderShippedEventHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(OrderShippedEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "📦 Node {Node} received OrderShippedEvent: Order {OrderId} shipped at {ShippedAt}",
            Environment.MachineName, @event.OrderId, @event.ShippedAt);

        // In production: send shipping notifications, update tracking, etc.
        return Task.CompletedTask;
    }
}