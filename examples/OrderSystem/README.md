# Catga OrderSystem - Complete CQRS Example

A comprehensive example demonstrating all Catga features: CQRS, Event Sourcing, multiple backends, and cluster support.

## Features Demonstrated

✅ **CQRS Pattern** - Commands and Queries separation  
✅ **Event Sourcing** - Full event history tracking  
✅ **Multiple Backends** - InMemory, Redis, NATS  
✅ **Distributed Messaging** - Pub/Sub with multiple transports  
✅ **Hosted Services** - Automatic lifecycle management with RecoveryHostedService, TransportHostedService, OutboxProcessorService  
✅ **Health Checks** - Kubernetes-ready liveness and readiness probes  
✅ **Graceful Shutdown** - Proper message completion before shutdown  
✅ **Cluster Mode** - Multi-node deployment  
✅ **AOT Compilation** - Native AOT ready  
✅ **MemoryPack Serialization** - High-performance binary serialization  

## Quick Start

### 1. InMemory (Standalone)
```bash
dotnet run
```

### 2. Redis Backend
```bash
# Start Redis
docker run -d -p 6379:6379 redis:alpine

# Run with Redis
dotnet run -- --transport redis --persistence redis
```

### 3. NATS Backend
```bash
# Start NATS with JetStream
docker run -d -p 4222:4222 nats:alpine -js

# Run with NATS
dotnet run -- --transport nats --persistence nats
```

### 4. Cluster Mode (3 nodes with Redis)
```bash
# Terminal 1 - Node 1
dotnet run -- --cluster --node-id node1 --port 5001 --transport redis --persistence redis

# Terminal 2 - Node 2
dotnet run -- --cluster --node-id node2 --port 5002 --transport redis --persistence redis

# Terminal 3 - Node 3
dotnet run -- --cluster --node-id node3 --port 5003 --transport redis --persistence redis
```

## Command Line Options

| Option | Default | Description |
|--------|---------|-------------|
| `--transport` | `inmemory` | Transport backend: `inmemory`, `redis`, `nats` |
| `--persistence` | `inmemory` | Persistence backend: `inmemory`, `redis`, `nats` |
| `--redis` | `localhost:6379` | Redis connection string |
| `--nats` | `nats://localhost:4222` | NATS server URL |
| `--cluster` | `false` | Enable cluster mode |
| `--node-id` | `auto` | Node identifier for cluster |
| `--port` | `5000` | HTTP port |

## API Endpoints

### System Endpoints
- `GET /` - System information and configuration
- `GET /health` - Overall health status (all checks)
- `GET /health/ready` - Readiness probe (for Kubernetes)
- `GET /health/live` - Liveness probe (for Kubernetes)
- `GET /stats` - Order statistics

### Order Management
- `POST /orders` - Create new order
- `GET /orders` - List all orders
- `GET /orders/{id}` - Get order details
- `POST /orders/{id}/pay` - Pay for order
- `POST /orders/{id}/ship` - Ship order
- `POST /orders/{id}/cancel` - Cancel order
- `GET /orders/{id}/history` - Get event history

## Example Usage

### Create Order
```bash
curl -X POST http://localhost:5000/orders \
  -H "Content-Type: application/json" \
  -d '{
    "customerId": "customer-123",
    "items": [
      {
        "productId": "prod-1",
        "name": "Laptop",
        "quantity": 1,
        "price": 999.99
      },
      {
        "productId": "prod-2",
        "name": "Mouse",
        "quantity": 2,
        "price": 29.99
      }
    ]
  }'
```

Response:
```json
{
  "orderId": "a1b2c3d4",
  "total": 1059.97,
  "createdAt": "2025-01-15T10:30:00Z"
}
```

### Pay Order
```bash
curl -X POST http://localhost:5000/orders/a1b2c3d4/pay \
  -H "Content-Type: application/json" \
  -d '{"paymentMethod": "credit_card"}'
```

### Ship Order
```bash
curl -X POST http://localhost:5000/orders/a1b2c3d4/ship \
  -H "Content-Type: application/json" \
  -d '{"trackingNumber": "TRACK-12345"}'
```

### Get Order History (Event Sourcing)
```bash
curl http://localhost:5000/orders/a1b2c3d4/history
```

Response:
```json
[
  {
    "orderId": "a1b2c3d4",
    "customerId": "customer-123",
    "total": 1059.97,
    "createdAt": "2025-01-15T10:30:00Z"
  },
  {
    "orderId": "a1b2c3d4",
    "paymentMethod": "credit_card",
    "paidAt": "2025-01-15T10:31:00Z"
  },
  {
    "orderId": "a1b2c3d4",
    "trackingNumber": "TRACK-12345",
    "shippedAt": "2025-01-15T10:32:00Z"
  }
]
```

### Get Statistics
```bash
curl http://localhost:5000/stats
```

Response:
```json
{
  "totalOrders": 10,
  "byStatus": {
    "Pending": 3,
    "Paid": 2,
    "Shipped": 4,
    "Cancelled": 1
  },
  "totalRevenue": 5299.85,
  "timestamp": "2025-01-15T10:35:00Z"
}
```

## Testing Different Configurations

### Test Script
Run the included test script to verify all configurations:

```bash
# Windows
.\test-all.ps1

# Linux/Mac
chmod +x test-all.sh
./test-all.sh
```

### Manual Testing

1. **InMemory (Development)**
   ```bash
   dotnet run
   ```

2. **Redis (Production-like)**
   ```bash
   docker run -d -p 6379:6379 redis:alpine
   dotnet run -- --transport redis --persistence redis
   ```

3. **NATS (High-performance)**
   ```bash
   docker run -d -p 4222:4222 nats:alpine -js
   dotnet run -- --transport nats --persistence nats
   ```

4. **Mixed Configuration**
   ```bash
   # Redis for persistence, NATS for transport
   dotnet run -- --transport nats --persistence redis
   ```

## Docker Compose

Start all infrastructure:

```bash
docker-compose up -d
```

This starts:
- Redis (port 6379)
- NATS with JetStream (port 4222)

## AOT Compilation

Build native executable:

```bash
dotnet publish -c Release
```

The compiled binary will be in `bin/Release/net9.0/publish/`

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    HTTP API Layer                       │
│  (Minimal API Endpoints - AOT Compatible)               │
└────────────────────┬────────────────────────────────────┘
                     │
┌────────────────────▼────────────────────────────────────┐
│                 Catga Mediator                          │
│  (Command/Query/Event Routing)                          │
└────────┬───────────────────────────┬────────────────────┘
         │                           │
┌────────▼────────┐         ┌────────▼────────────────────┐
│   Handlers      │         │   Event Handlers            │
│  - Commands     │         │  - OrderEventLogger         │
│  - Queries      │         │  - (Extensible)             │
└────────┬────────┘         └─────────────────────────────┘
         │
┌────────▼─────────────────────────────────────────────────┐
│              Hosted Services                             │
│  - RecoveryHostedService (Health Check & Auto Recovery) │
│  - TransportHostedService (Lifecycle Management)        │
│  - OutboxProcessorService (Background Processing)       │
└──────────────────────────────────────────────────────────┘
         │
┌────────▼─────────────────────────────────────────────────┐
│              Transport Layer                             │
│  InMemory | Redis Pub/Sub | NATS JetStream              │
└──────────────────────────────────────────────────────────┘
         │
┌────────▼─────────────────────────────────────────────────┐
│            Persistence Layer                             │
│  InMemory | Redis | NATS KV Store                        │
└──────────────────────────────────────────────────────────┘
```

## Hosted Services

This example demonstrates Catga's integration with Microsoft.Extensions.Hosting:

### RecoveryHostedService
- Automatically monitors component health every 30 seconds
- Attempts recovery when components become unhealthy
- Configurable retry logic with exponential backoff

### TransportHostedService
- Manages transport layer lifecycle (startup/shutdown)
- Handles graceful shutdown (stops accepting new messages, waits for completion)
- Integrates with IHostApplicationLifetime

### OutboxProcessorService
- Processes outbox messages in the background every 2 seconds
- Ensures reliable message delivery
- Configurable batch size and scan interval

### Health Checks
- `/health` - Overall health status
- `/health/ready` - Readiness probe (checks transport and persistence)
- `/health/live` - Liveness probe (checks recovery service)

Perfect for Kubernetes deployments!

## Order State Machine

```
┌─────────┐
│ Pending │
└────┬────┘
     │ Pay
┌────▼────┐
│  Paid   │
└────┬────┘
     │ Ship
┌────▼────┐
│ Shipped │
└────┬────┘
     │ Deliver
┌────▼─────────┐
│  Delivered   │
└──────────────┘

Cancel allowed from: Pending, Paid
```

## Performance Tips

1. **Use Redis/NATS for production** - Better performance and scalability
2. **Enable cluster mode** - Distribute load across multiple nodes
3. **Use AOT compilation** - Faster startup and lower memory usage
4. **Monitor with /stats endpoint** - Track system health

## Troubleshooting

### Redis Connection Failed
```bash
# Check Redis is running
docker ps | grep redis

# Test connection
redis-cli ping
```

### NATS Connection Failed
```bash
# Check NATS is running
docker ps | grep nats

# Test connection
nats server check
```

### Port Already in Use
```bash
# Use different port
dotnet run -- --port 5001
```

## Learn More

- [Catga Documentation](../../docs/README.md)
- [CQRS Pattern](../../docs/patterns/cqrs.md)
- [Event Sourcing](../../docs/patterns/event-sourcing.md)
- [Cluster Setup](../../docs/deployment/cluster.md)
