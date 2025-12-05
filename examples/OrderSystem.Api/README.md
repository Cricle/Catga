# OrderSystem.Api - Catga Best Practices Example

Complete order system demonstrating Catga framework best practices.

## Quick Start

```bash
cd examples/OrderSystem.Api
dotnet run
```

Open http://localhost:5275/swagger for API docs.

## Features

| Feature | Description |
|---------|-------------|
| **CQRS Pattern** | Commands and queries separation |
| **Mediator Pattern** | ICatgaMediator for handler dispatch |
| **Flow Pattern** | Multi-step operations with automatic compensation |
| **Event Publishing** | Multiple handlers per event |
| **Pipeline Behaviors** | Cross-cutting concerns (logging, validation) |
| **MemoryPack** | High-performance binary serialization |

## Project Structure

```
OrderSystem.Api/
├── Behaviors/           # Pipeline behaviors
│   └── ValidationBehavior.cs
├── Domain/              # Domain models
│   └── Order.cs
├── Handlers/            # Command/Query/Event handlers
│   ├── OrderHandlers.cs
│   └── EventHandlers.cs
├── Messages/            # Commands, Queries, Events
│   ├── Commands.cs
│   └── Events.cs
├── Services/            # Infrastructure services
│   └── InMemoryOrderRepository.cs
└── Program.cs           # Application entry point
```

## Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/orders` | POST | Create order (simple) |
| `/api/orders/flow` | POST | Create order (Flow pattern) |
| `/api/orders/{id}` | GET | Get order |
| `/api/orders/{id}/cancel` | POST | Cancel order |
| `/api/users/{id}/orders` | GET | Get user orders |
| `/health` | GET | Health check |

## Best Practices Demonstrated

### 1. Pipeline Behaviors

Cross-cutting concerns like logging and validation:

```csharp
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<CatgaResult<TResponse>> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        logger.LogInformation("Handling {Request}", typeof(TRequest).Name);
        var result = await next();
        logger.LogInformation("{Request} completed in {Ms}ms", typeof(TRequest).Name, sw.ElapsedMilliseconds);
        return result;
    }
}
```

### 2. Event Publishing

Commands publish events, multiple handlers react:

```csharp
// In CreateOrderHandler
await mediator.PublishAsync(new OrderCreatedEvent(order.OrderId, ...));

// Multiple handlers receive the event
public class OrderCreatedEventHandler : IEventHandler<OrderCreatedEvent> { ... }
public class SendOrderNotificationHandler : IEventHandler<OrderCreatedEvent> { ... }
```

### 3. Flow DSL (Saga Pattern)

Multi-step operations with automatic compensation using fluent DSL:

```csharp
var result = await Flow.Create("CreateOrderFlow")
    .Step(async _ =>
    {
        // Step 1: Create order
        order = new Order { ... };
        await orderRepository.SaveAsync(order, ct);
    })
    .Step(
        _ => { /* Step 2: Reserve stock */ return Task.CompletedTask; },
        _ => { /* Compensation: Release stock */ return Task.CompletedTask; })
    .Step(
        async _ => { /* Step 3: Confirm order */ },
        async _ => { /* Compensation: Mark as failed */ })
    .ExecuteAsync(ct);
```

### 4. DI Registration

```csharp
// Pipeline behaviors (executed in order)
builder.Services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
builder.Services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

// Command/Query handlers
builder.Services.AddScoped<IRequestHandler<CreateOrderCommand, OrderCreatedResult>, CreateOrderHandler>();

// Event handlers (multiple per event)
builder.Services.AddScoped<IEventHandler<OrderCreatedEvent>, OrderCreatedEventHandler>();
builder.Services.AddScoped<IEventHandler<OrderCreatedEvent>, SendOrderNotificationHandler>();
```

## Example Requests

```bash
# Create order (simple)
curl -X POST http://localhost:5275/api/orders \
  -H "Content-Type: application/json" \
  -d '{"customerId":"C001","items":[{"productId":"P1","productName":"Laptop","quantity":1,"unitPrice":999}]}'

# Create order (Flow pattern)
curl -X POST http://localhost:5275/api/orders/flow \
  -H "Content-Type: application/json" \
  -d '{"customerId":"C001","items":[{"productId":"P1","productName":"Laptop","quantity":1,"unitPrice":999}]}'

# Get order
curl http://localhost:5275/api/orders/{orderId}

# Cancel order
curl -X POST http://localhost:5275/api/orders/{orderId}/cancel \
  -H "Content-Type: application/json" \
  -d '{"reason":"Customer request"}'
```
