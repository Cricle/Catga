# OrderSystem.Api - Catga Example

Order system demonstrating Catga CQRS and Flow patterns.

## Quick Start

```bash
cd examples/OrderSystem.Api
dotnet run
```

Open http://localhost:5275/swagger for API docs.

## Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/orders` | POST | Create order (simple) |
| `/api/orders/flow` | POST | Create order (Flow pattern) |
| `/api/orders/{id}` | GET | Get order |
| `/api/orders/{id}/cancel` | POST | Cancel order |
| `/api/users/{id}/orders` | GET | Get user orders |
| `/health` | GET | Health check |

## Features

- **CQRS Pattern** - Commands and queries separation
- **Mediator Pattern** - ICatgaMediator for handler dispatch
- **Flow Pattern** - Multi-step operations with automatic compensation
- **MemoryPack** - Fast serialization

## Flow Pattern Example

The `/api/orders/flow` endpoint demonstrates the Flow pattern:

```csharp
public partial class CreateOrderFlowHandler
{
    [FlowStep(Order = 1)]
    private async Task CreateOrder(...) { ... }

    [FlowStep(Order = 2, Compensate = nameof(ReleaseStock))]
    private Task ReserveStock(...) { ... }

    [FlowStep(Order = 3, Compensate = nameof(MarkFailed))]
    private async Task ConfirmOrder(...) { ... }

    // Compensation methods called on failure (reverse order)
    private Task ReleaseStock(...) { ... }
    private async Task MarkFailed(...) { ... }
}
```

Flow steps execute in order. On failure, compensation methods run in reverse order.

## Example

```bash
# Create order (simple)
curl -X POST http://localhost:5275/api/orders \
  -H "Content-Type: application/json" \
  -d '{"customerId":"C001","items":[{"productId":"P1","productName":"Laptop","quantity":1,"unitPrice":999}]}'

# Create order (Flow pattern with compensation)
curl -X POST http://localhost:5275/api/orders/flow \
  -H "Content-Type: application/json" \
  -d '{"customerId":"C001","items":[{"productId":"P1","productName":"Laptop","quantity":1,"unitPrice":999}]}'
```
