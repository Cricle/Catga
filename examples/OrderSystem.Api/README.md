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
├── Flows/               # Flow DSL configurations
│   └── CreateOrderFlowConfig.cs
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

Multi-step operations with automatic compensation using FlowConfig DSL:

```csharp
// 1. Define state class implementing IFlowState
public class CreateOrderFlowState : IFlowState { ... }

// 2. Define flow configuration
public class CreateOrderFlowConfig : FlowConfig<CreateOrderFlowState>
{
    protected override void Configure(IFlowBuilder<CreateOrderFlowState> flow)
    {
        flow.Name("create-order");
        flow.Timeout(TimeSpan.FromMinutes(5));

        // Step 1: Save order (with compensation)
        flow.Send(s => new SaveOrderCommand(s.OrderId!))
            .IfFail(s => new DeleteOrderCommand(s.OrderId!))
            .Tag("persistence");

        // Step 2: Reserve stock (with compensation)
        flow.Send(s => new ReserveStockCommand(s.OrderId!))
            .IfFail(s => new ReleaseStockCommand(s.OrderId!))
            .Tag("inventory");

        // Step 3: Publish event
        flow.Publish(s => new OrderConfirmedEvent(s.OrderId!));
    }
}
```

See `Flows/CreateOrderFlowConfig.cs` for the complete example.

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
