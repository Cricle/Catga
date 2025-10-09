# Catga 分布式应用模板

基于 Catga CQRS 框架的完整分布式应用模板。提供开箱即用的分布式 ID、消息队列、缓存、分布式锁、Saga 事务等完整分布式能力。

## 核心特性

- ✅ **CQRS 架构** - 命令查询职责分离
- ✅ **分布式 ID** - Snowflake 算法，8.5M IDs/秒，500+ 年使用寿命
- ✅ **NATS 消息队列** - 高性能事件驱动通信
- ✅ **Redis 分布式缓存** - 分布式缓存和分布式锁
- ✅ **Outbox/Inbox 模式** - 可靠消息投递，最终一致性
- ✅ **Saga 编排** - 分布式事务协调
- ✅ **事件溯源** - 完整的事件历史和状态重建
- ✅ **熔断器** - 弹性设计和故障隔离
- ✅ **健康检查** - 完整的监控和诊断
- ✅ **零 GC** - 关键路径零内存分配
- ✅ **AOT 编译** - 快速启动，低内存占用
- ✅ **OpenAPI/Swagger** - 完整的 API 文档

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

