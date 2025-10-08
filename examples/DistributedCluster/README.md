# Distributed Cluster Example

This example demonstrates Catga in a **distributed microservices environment** with:
- 🚀 **NATS** for distributed messaging
- 💾 **Redis** for persistence (optional)
- 🤖 **Source Generator** for automatic handler registration
- ⚡ **MemoryPack** for high-performance serialization (AOT-friendly)
- 🎯 **Full Native AOT** compatibility

## 🏗 Architecture

```
┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│   Node 1    │     │   Node 2    │     │   Node 3    │
│  (API)      │     │  (API)      │     │  (Worker)   │
└──────┬──────┘     └──────┬──────┘     └──────┬──────┘
       │                   │                    │
       └───────────────────┼────────────────────┘
                           │
                    ┌──────▼──────┐
                    │   NATS      │
                    │ JetStream   │
                    └─────────────┘
```

### Features Demonstrated

1. **Distributed Commands**
   - Send command to any node
   - Processed by available node
   - Load balanced automatically

2. **Distributed Events**
   - Published via NATS
   - Received by ALL nodes
   - Pub/Sub pattern

3. **Idempotency**
   - Prevents duplicate processing
   - Works across all nodes

4. **High Performance**
   - MemoryPack serialization
   - AOT compilation
   - Zero reflection

## 🚀 Quick Start

### Prerequisites

- .NET 9.0 SDK
- Docker (for NATS)
- Redis (optional, for persistence)

### 1. Start NATS

```bash
docker run -d --name nats -p 4222:4222 nats:latest -js
```

### 2. Start Redis (Optional)

```bash
docker run -d --name redis -p 6379:6379 redis:alpine
```

### 3. Run Multiple Nodes

**Terminal 1 - Node 1 (Port 5001)**:
```bash
cd examples/DistributedCluster
dotnet run --urls="https://localhost:5001"
```

**Terminal 2 - Node 2 (Port 5002)**:
```bash
cd examples/DistributedCluster
dotnet run --urls="https://localhost:5002"
```

**Terminal 3 - Node 3 (Port 5003)**:
```bash
cd examples/DistributedCluster
dotnet run --urls="https://localhost:5003"
```

### 4. Test the Cluster

**Create Order (任意节点)**:
```bash
curl -X POST https://localhost:5001/orders \
  -H "Content-Type: application/json" \
  -d '{
    "customerId": "CUST-001",
    "items": [
      {
        "productId": "PROD-001",
        "productName": "Laptop",
        "quantity": 1,
        "unitPrice": 999.99
      }
    ]
  }'
```

**Get Order (任意节点)**:
```bash
curl https://localhost:5002/orders/{orderId}
```

**Ship Order**:
```bash
curl -X POST https://localhost:5003/orders/{orderId}/ship
```

### 5. Observe Distributed Behavior

Watch all 3 terminal windows:
- ✅ **Command** sent to Node 1
- ✅ **Event** received by ALL 3 nodes
- ✅ **Query** handled by any node

## 📊 How It Works

### 1. Command Processing (Point-to-Point)

```csharp
// Client sends to ANY node
POST https://localhost:5001/orders

// Node 1 processes the command
CreateOrderCommandHandler.HandleAsync()
  ↓
// Command processed ONCE
  ↓
// Publishes event to NATS
```

### 2. Event Distribution (Pub/Sub)

```csharp
// Event published to NATS
mediator.PublishAsync(new OrderCreatedEvent { ... })
  ↓
// NATS broadcasts to ALL nodes
  ↓
// ALL nodes receive and handle
Node 1: OrderCreatedEventHandler.HandleAsync()
Node 2: OrderCreatedEventHandler.HandleAsync()
Node 3: OrderCreatedEventHandler.HandleAsync()
```

### 3. Query Processing (Load Balanced)

```csharp
// Client queries ANY node
GET https://localhost:5002/orders/{id}

// That node handles it (load balanced)
GetOrderQueryHandler.HandleAsync()
  ↓
// Returns result
```

## 🎯 Configuration

### appsettings.json

```json
{
  "Nats": {
    "Url": "nats://localhost:4222"
  },
  "Redis": {
    "Connection": "localhost:6379"
  }
}
```

### Environment Variables

```bash
# Override NATS URL
export Nats__Url="nats://my-nats-server:4222"

# Override Redis
export Redis__Connection="my-redis:6379"
```

## 🔍 Monitoring

### Health Check

```bash
curl https://localhost:5001/health
```

Response:
```json
{
  "status": "Healthy",
  "node": "YOUR-MACHINE-NAME",
  "version": "1.0.0"
}
```

### Logs

Watch the console output:
- 📝 Command processing: `"Node {Node} processing order creation..."`
- 📢 Event received: `"Node {Node} received OrderCreatedEvent..."`
- 📦 Order shipped: `"Node {Node} received OrderShippedEvent..."`

## 💡 Key Code Sections

### 1. Distributed Setup

```csharp
// ✨ Simple configuration
builder.Services.AddCatga();
builder.Services.AddNatsTransport(options => {
    options.Url = "nats://localhost:4222";
});
builder.Services.AddGeneratedHandlers();
```

### 2. Command Handler (Runs on ONE node)

```csharp
public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, OrderCreatedResponse>
{
    public async Task<CatgaResult<OrderCreatedResponse>> HandleAsync(...)
    {
        // Process command
        var order = CreateOrder(request);

        // Publish event to ALL nodes
        await _mediator.PublishAsync(new OrderCreatedEvent { ... });

        return CatgaResult.Success(response);
    }
}
```

### 3. Event Handler (Runs on ALL nodes)

```csharp
public class OrderCreatedEventHandler : IEventHandler<OrderCreatedEvent>
{
    public Task HandleAsync(OrderCreatedEvent @event, ...)
    {
        // This runs on EVERY node!
        _logger.LogInformation("Node {Node} received event", Environment.MachineName);

        // Update read models, send notifications, etc.
        return Task.CompletedTask;
    }
}
```

## 🎓 Production Considerations

### 1. Database

Replace in-memory dictionary with real database:
```csharp
// Instead of:
private static readonly Dictionary<string, OrderDto> _orders = new();

// Use:
private readonly IOrderRepository _repository;
```

### 2. Persistence

Enable Redis Outbox/Inbox:
```csharp
builder.Services.AddRedisPersistence(options => {
    options.ConnectionString = "redis://production:6379";
});
```

### 3. Monitoring

Add:
- Health checks
- Metrics (Prometheus)
- Distributed tracing (OpenTelemetry)
- Logging (Serilog)

### 4. Security

- Enable authentication
- Use TLS for NATS
- Secure Redis connection
- API rate limiting

## 📚 Learn More

- [Source Generator Guide](../../docs/guides/source-generator.md)
- [AOT Compatibility](../../docs/aot/README.md)
- [NATS Documentation](https://docs.nats.io/)
- [Getting Started](../../docs/guides/GETTING_STARTED.md)

## 🐛 Troubleshooting

### NATS Connection Failed

```
Error: Could not connect to NATS
```

**Solution**: Ensure NATS is running:
```bash
docker ps | grep nats
# If not running:
docker start nats
```

### Events Not Received

**Solution**: Check JetStream is enabled:
```bash
docker logs nats | grep "JetStream"
```

### Port Already in Use

```
Error: Address already in use
```

**Solution**: Use different ports:
```bash
dotnet run --urls="https://localhost:6001"
```

## 🎯 Next Steps

1. ✅ Run the example
2. ✅ Watch distributed behavior
3. ✅ Try killing/restarting nodes
4. ✅ Test failover
5. ✅ Add your own handlers
6. ✅ Deploy to Kubernetes

---

**Status**: ✅ Production-Ready
**AOT Compatible**: ✅ Yes
**Dependencies**: NATS (required), Redis (optional)
