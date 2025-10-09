# CatgaDistributed

A distributed application built with the Catga CQRS framework.

## Features

- ✅ **CQRS Pattern** - Command Query Responsibility Segregation
- ✅ **Distributed ID** - Snowflake ID generation
- ✅ **NATS Messaging** - Event-driven communication
- ✅ **Redis Cache** - Distributed caching and locking
- ✅ **Outbox Pattern** - Reliable message delivery
- ✅ **Circuit Breaker** - Resilience and fault tolerance
- ✅ **Health Checks** - Monitoring and diagnostics
- ✅ **OpenAPI/Swagger** - API documentation

## Quick Start

### Using Docker Compose (Recommended)

```bash
# Start all services
docker-compose up -d

# Check health
curl http://localhost:5000/health

# Create an order
curl -X POST http://localhost:5000/api/orders \
  -H "Content-Type: application/json" \
  -d '{
    "customerId": 123,
    "items": [
      {"productId": 1, "quantity": 2, "price": 29.99}
    ],
    "shippingAddress": "123 Main St"
  }'

# Generate a distributed ID
curl http://localhost:5000/api/id

# View metrics
curl http://localhost:5000/metrics
```

### Local Development

```bash
# Start dependencies
docker-compose up redis nats -d

# Run the application
dotnet run

# Navigate to Swagger UI
open https://localhost:7000/swagger
```

## Configuration

Edit `appsettings.json`:

```json
{
  "DistributedId": {
    "WorkerId": 1,
    "DataCenterId": 1
  },
  "Nats": {
    "Url": "nats://localhost:4222"
  },
  "ConnectionStrings": {
    "Redis": "localhost:6379"
  }
}
```

## Project Structure

```
CatgaDistributed/
├── Commands/           # Command handlers
├── Queries/            # Query handlers
├── Events/             # Domain events
├── Program.cs          # Application entry point
├── appsettings.json    # Configuration
├── docker-compose.yml  # Docker composition
└── Dockerfile          # Container image
```

## Architecture

This application follows the CQRS pattern with event-driven architecture:

1. **Commands** - Modify state (e.g., CreateOrder)
2. **Queries** - Read state (e.g., GetOrderById)
3. **Events** - Notify other services (e.g., OrderCreated)

## Distributed Features

### Snowflake ID Generator

```csharp
app.MapGet("/api/id", (ISnowflakeIdGenerator idGen) =>
{
    return Results.Ok(new { id = idGen.NextId() });
});
```

### NATS Messaging

```csharp
// Publish event
await mediator.PublishAsync(new OrderCreatedEvent(orderId));

// Subscribe to events
public class OrderCreatedEventHandler : IEventHandler<OrderCreatedEvent>
{
    public async ValueTask Handle(OrderCreatedEvent @event, CancellationToken ct)
    {
        // Handle event
    }
}
```

### Circuit Breaker

```csharp
public class MyHandler : IRequestHandler<MyCommand, MyResponse>
{
    private readonly ICircuitBreaker _circuitBreaker;
    
    public async ValueTask<MyResponse> Handle(MyCommand cmd, CancellationToken ct)
    {
        return await _circuitBreaker.ExecuteAsync(async () =>
        {
            // Protected operation
        });
    }
}
```

## Health Checks

- **Catga**: Framework health
- **Redis**: Cache connectivity
- **NATS**: Message broker connectivity

Access: `GET /health`

## Monitoring

Metrics are exposed at `/metrics`:

- Request throughput
- Success/failure rates
- Circuit breaker state
- Cache hit rates

## Learn More

- [Catga Documentation](https://github.com/yourorg/catga)
- [CQRS Pattern](https://martinfowler.com/bliki/CQRS.html)
- [Event Sourcing](https://martinfowler.com/eaaDev/EventSourcing.html)
- [Outbox Pattern](https://microservices.io/patterns/data/transactional-outbox.html)

