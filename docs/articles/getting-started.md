# Getting Started with Catga

Catga is a high-performance, 100% AOT-compatible CQRS framework for .NET 9.

## Quick Start

### 1. Installation

```bash
dotnet add package Catga
dotnet add package Catga.Transport.InMemory
dotnet add package Catga.Persistence.InMemory
```

### 2. Basic Setup

```csharp
using Catga;

var builder = WebApplication.CreateBuilder(args);

// Register Catga with InMemory transport and persistence
builder.Services
    .AddCatga()
    .AddInMemoryTransport()
    .AddInMemoryEventStore()
    .AddInMemoryOutbox()
    .AddInMemoryInbox();

var app = builder.Build();
app.Run();
```

### 3. Define Messages

```csharp
// Command
public record CreateOrderCommand : IRequest<Order>
{
    public required string CustomerId { get; init; }
    public required decimal Amount { get; init; }
}

// Event
public record OrderCreatedEvent : IEvent
{
    public required string OrderId { get; init; }
    public required string CustomerId { get; init; }
}
```

### 4. Create Handlers

```csharp
public class CreateOrderCommandHandler
    : IRequestHandler<CreateOrderCommand, Order>
{
    public async Task<Order> Handle(
        CreateOrderCommand request,
        CancellationToken cancellationToken)
    {
        var order = new Order
        {
            Id = Guid.NewGuid().ToString(),
            CustomerId = request.CustomerId,
            Amount = request.Amount
        };

        return order;
    }
}

public class OrderCreatedEventHandler
    : IEventHandler<OrderCreatedEvent>
{
    private readonly ILogger<OrderCreatedEventHandler> _logger;

    public OrderCreatedEventHandler(ILogger<OrderCreatedEventHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(
        OrderCreatedEvent @event,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Order created: {OrderId}",
            @event.OrderId);
        return Task.CompletedTask;
    }
}
```

### 5. Use the Mediator

```csharp
public class OrdersController : ControllerBase
{
    private readonly ICatgaMediator _mediator;

    public OrdersController(ICatgaMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateOrderCommand command)
    {
        var result = await _mediator.SendAsync(command);

        if (result.IsSuccess)
        {
            await _mediator.PublishAsync(new OrderCreatedEvent
            {
                OrderId = result.Value.Id,
                CustomerId = result.Value.CustomerId
            });

            return Ok(result.Value);
        }

        return BadRequest(result.Error);
    }
}
```

## Next Steps

- [Architecture Overview](architecture.md) - Understanding Catga's design
- [Configuration Guide](configuration.md) - Advanced configuration options
- [Transport Layer](transport-layer.md) - Choose your message transport
- [Persistence Layer](persistence-layer.md) - Event sourcing and outbox pattern

## Production Setup

For production environments, use distributed transport and persistence:

```csharp
builder.Services
    .AddCatga()
    .AddRedisTransport(options =>
    {
        options.ConnectionString = "redis:6379";
        options.DefaultQoS = QualityOfService.AtLeastOnce;
    })
    .AddNatsPersistence(options =>
    {
        options.EventStreamName = "PROD_EVENTS";
        options.EventStoreOptions = new NatsJSStoreOptions
        {
            Replicas = 3,  // High availability
            MaxAge = TimeSpan.FromDays(90)
        };
    });
```

See [Configuration Guide](configuration.md) for more details.

