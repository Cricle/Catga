# OrderSystem - Feature Showcase

This example demonstrates all major Catga features in a simple, production-ready order management system.

## ✅ Implemented Features

### 1. CQRS Pattern
- **Commands**: CreateOrder, PayOrder, ShipOrder, CancelOrder
- **Queries**: GetOrder, GetAllOrders
- Clear separation between write and read operations
- Type-safe command/query handlers

### 2. Event Sourcing
- **Events**: OrderCreated, OrderPaid, OrderShipped, OrderCancelled
- Complete event history tracking per order
- Event replay capability via `/orders/{id}/history` endpoint
- Event-driven architecture with pub/sub

### 3. Multiple Backend Support

#### Transport Backends
- **InMemory**: For development and testing
- **Redis**: Production-ready with Pub/Sub
- **NATS**: High-performance JetStream messaging

#### Persistence Backends
- **InMemory**: Fast, ephemeral storage
- **Redis**: Distributed, persistent storage
- **NATS**: JetStream with KV store

### 4. Deployment Modes

#### Standalone Mode
```bash
dotnet run
```
Single instance, perfect for development

#### Cluster Mode
```bash
# Node 1
dotnet run -- --cluster --node-id node1 --port 5001 --transport redis --persistence redis

# Node 2
dotnet run -- --cluster --node-id node2 --port 5002 --transport redis --persistence redis

# Node 3
dotnet run -- --cluster --node-id node3 --port 5003 --transport redis --persistence redis
```
Multi-node deployment with shared state

### 5. Serialization
- **MemoryPack**: High-performance binary serialization
- AOT-compatible
- Zero-allocation for hot paths

### 6. API Endpoints

#### System Endpoints
- `GET /` - System information
- `GET /health` - Health check
- `GET /stats` - Order statistics

#### Order Management
- `POST /orders` - Create order
- `GET /orders` - List all orders
- `GET /orders/{id}` - Get order details
- `POST /orders/{id}/pay` - Pay order
- `POST /orders/{id}/ship` - Ship order
- `POST /orders/{id}/cancel` - Cancel order
- `GET /orders/{id}/history` - Event history

### 7. Order State Machine

```
Pending → Pay → Paid → Ship → Shipped → Delivered
   ↓                ↓
Cancel          Cancel
```

### 8. AOT Compilation
- Native AOT ready
- Fast startup time
- Low memory footprint
- No JIT compilation overhead

### 9. Docker Support
- Dockerfile for containerization
- docker-compose.yml for infrastructure
- Multi-profile support (inmemory, redis, nats, cluster)

### 10. Testing
- `quick-test.ps1` - Fast validation
- `test-all.ps1` - Comprehensive test suite
- Tests all configurations and modes

## 🎯 Use Cases Demonstrated

### 1. Development
```bash
dotnet run
```
Fast iteration with InMemory backend

### 2. Integration Testing
```bash
docker-compose up -d redis nats
dotnet run -- --transport redis --persistence redis
```
Test with real infrastructure

### 3. Production Deployment
```bash
docker-compose --profile cluster up -d
```
3-node cluster with Redis backend

### 4. Mixed Configuration
```bash
dotnet run -- --transport nats --persistence redis
```
NATS for messaging, Redis for storage

## 📊 Performance Characteristics

### InMemory
- **Latency**: < 1ms
- **Throughput**: 100k+ ops/sec
- **Use Case**: Development, testing

### Redis
- **Latency**: 1-5ms
- **Throughput**: 10k-50k ops/sec
- **Use Case**: Production, distributed systems

### NATS
- **Latency**: < 2ms
- **Throughput**: 50k-100k ops/sec
- **Use Case**: High-performance messaging

## 🔧 Configuration Options

| Option | Values | Default | Description |
|--------|--------|---------|-------------|
| `--transport` | inmemory, redis, nats | inmemory | Message transport |
| `--persistence` | inmemory, redis, nats | inmemory | Data persistence |
| `--redis` | connection string | localhost:6379 | Redis server |
| `--nats` | URL | nats://localhost:4222 | NATS server |
| `--cluster` | flag | false | Enable cluster mode |
| `--node-id` | string | auto-generated | Node identifier |
| `--port` | number | 5000 | HTTP port |

## 🚀 Quick Start Examples

### Example 1: Simple Development
```bash
dotnet run
curl http://localhost:5000/
```

### Example 2: Redis Backend
```bash
docker run -d -p 6379:6379 redis:alpine
dotnet run -- --transport redis --persistence redis
```

### Example 3: NATS Backend
```bash
docker run -d -p 4222:4222 nats:alpine -js
dotnet run -- --transport nats --persistence nats
```

### Example 4: 3-Node Cluster
```bash
docker run -d -p 6379:6379 redis:alpine

# Terminal 1
dotnet run -- --cluster --node-id node1 --port 5001 --transport redis --persistence redis

# Terminal 2
dotnet run -- --cluster --node-id node2 --port 5002 --transport redis --persistence redis

# Terminal 3
dotnet run -- --cluster --node-id node3 --port 5003 --transport redis --persistence redis
```

## 📝 Code Highlights

### Minimal Code, Maximum Features
- **Single file**: Program.cs (~250 lines)
- **No boilerplate**: Catga handles infrastructure
- **Type-safe**: Full compile-time checking
- **AOT-ready**: Native compilation support

### Handler Example
```csharp
public sealed class CreateOrderHandler(OrderStore store, ICatgaMediator mediator) 
    : IRequestHandler<CreateOrderCommand, OrderCreatedResult>
{
    public async ValueTask<CatgaResult<OrderCreatedResult>> HandleAsync(
        CreateOrderCommand cmd, CancellationToken ct = default)
    {
        var orderId = Guid.NewGuid().ToString("N")[..8];
        var total = cmd.Items.Sum(i => i.Price * i.Quantity);
        var now = DateTime.UtcNow;
        
        store.Save(new Order(orderId, cmd.CustomerId, cmd.Items, OrderStatus.Pending, total, now));
        store.AppendEvent(orderId, new OrderCreatedEvent(orderId, cmd.CustomerId, total, now));
        
        await mediator.PublishAsync(new OrderCreatedEvent(orderId, cmd.CustomerId, total, now), ct);
        
        return CatgaResult<OrderCreatedResult>.Success(new OrderCreatedResult(orderId, total, now));
    }
}
```

### Event Handler Example
```csharp
public sealed class OrderEventLogger : 
    IEventHandler<OrderCreatedEvent>, 
    IEventHandler<OrderPaidEvent>
{
    public ValueTask HandleAsync(OrderCreatedEvent evt, CancellationToken ct = default)
    {
        Console.WriteLine($"[Event] Order {evt.OrderId} created: ${evt.Total}");
        return ValueTask.CompletedTask;
    }
    
    public ValueTask HandleAsync(OrderPaidEvent evt, CancellationToken ct = default)
    {
        Console.WriteLine($"[Event] Order {evt.OrderId} paid via {evt.PaymentMethod}");
        return ValueTask.CompletedTask;
    }
}
```

## 🎓 Learning Path

1. **Start Simple**: Run with InMemory backend
2. **Add Persistence**: Try Redis or NATS
3. **Scale Out**: Deploy cluster mode
4. **Optimize**: Use AOT compilation
5. **Monitor**: Check /stats endpoint
6. **Extend**: Add your own commands/events

## 📚 Related Documentation

- [Catga Core Concepts](../../docs/README.md)
- [CQRS Pattern](../../docs/patterns/cqrs.md)
- [Event Sourcing](../../docs/patterns/event-sourcing.md)
- [Deployment Guide](../../docs/deployment/README.md)
- [Performance Tuning](../../docs/performance-optimization-guide.md)

## 🤝 Contributing

This example is designed to be:
- **Simple**: Easy to understand
- **Complete**: Shows all features
- **Extensible**: Easy to modify
- **Production-ready**: Real-world patterns

Feel free to use it as a template for your own projects!
