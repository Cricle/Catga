# OrderSystem.Api - Catga Example

A complete order system demonstrating Catga framework features.

## Quick Start

```bash
# Single node
cd examples/OrderSystem.Api
dotnet run

# With Aspire (cluster mode)
cd examples/OrderSystem.AppHost
dotnet run
```

## Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/orders` | POST | Create order |
| `/api/orders/{id}` | GET | Get order |
| `/api/orders/{id}/cancel` | POST | Cancel order |
| `/api/users/{id}/orders` | GET | Get user orders |
| `/api/outbox/process` | POST | Process outbox |
| `/api/cluster/status` | GET | Cluster status |
| `/api/cluster/node` | GET | Node info |
| `/health` | GET | Health check |
| `/swagger` | GET | API docs |

## Features Demonstrated

- **CQRS Pattern** - Commands and queries separation
- **Saga Pattern** - Automatic compensation on failure
- **Event Publishing** - Order events via mediator
- **Distributed Lock** - Per-customer order locking
- **Structured Logging** - LoggerMessage source generation
- **Health Checks** - Kubernetes-ready endpoints

## Project Structure

```
OrderSystem.Api/
├── Domain/           # Order entity
├── Handlers/         # Command/Query handlers
├── Messages/         # Commands and events
├── Services/         # Repository, inventory, payment
└── Program.cs        # App configuration
```

## API Examples

```bash
# Create order
curl -X POST http://localhost:5275/api/orders \
  -H "Content-Type: application/json" \
  -d '{"customerId":"C001","items":[{"productId":"P1","quantity":2,"unitPrice":10}],"shippingAddress":"123 Main St","paymentMethod":"card"}'

# Get order
curl http://localhost:5275/api/orders/{orderId}

# Cancel order
curl -X POST http://localhost:5275/api/orders/{orderId}/cancel \
  -H "Content-Type: application/json" \
  -d '{"reason":"Customer request"}'
```

## Testing

```powershell
# Run demo tests
.\test-demo.ps1

# Cross-mode stress tests
.\cross-test.ps1
```
