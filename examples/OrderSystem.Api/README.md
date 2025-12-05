# OrderSystem.Api - Catga Example

Simple order system demonstrating Catga with source-generated endpoints.

## Quick Start

```bash
cd examples/OrderSystem.Api
dotnet run
```

Open http://localhost:5275/swagger for API docs.

## Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/orders` | POST | Create order |
| `/api/orders/{id}` | GET | Get order |
| `/api/orders/{id}/cancel` | POST | Cancel order |
| `/api/users/{id}/orders` | GET | Get user orders |
| `/health` | GET | Health check |

## Features

- **Source Generated Endpoints** - `[Route]` attribute auto-generates API endpoints
- **CQRS Pattern** - Commands and queries separation
- **MemoryPack** - Fast serialization

## Example

```bash
# Create order
curl -X POST http://localhost:5275/api/orders \
  -H "Content-Type: application/json" \
  -d '{"customerId":"C001","items":[{"productId":"P1","productName":"Laptop","quantity":1,"unitPrice":999}]}'

# Get order
curl http://localhost:5275/api/orders/{orderId}

# Cancel order
curl -X POST http://localhost:5275/api/orders/{orderId}/cancel \
  -d '{"reason":"Customer request"}'
```
