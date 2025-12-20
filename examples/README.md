# OrderSystem - Best Practices Example

A complete e-commerce order system demonstrating how to build business applications with Catga. **Focus on your domain logic, not framework boilerplate.**

## Quick Start

```bash
cd examples/OrderSystem.Api
dotnet run
```

Open http://localhost:5275/swagger

## Project Structure

```
OrderSystem.Api/
├── Domain/           # Business entities (Order, Customer, Product)
├── Messages/         # Commands, Queries, Events
├── Handlers/         # Business logic handlers
├── Flows/            # Distributed workflows (Sagas)
├── Services/         # Domain services
└── Program.cs        # Minimal configuration
```

## Business-First Design

### 1. Define Your Domain

```csharp
// Domain/Order.cs
public class Order
{
    public string Id { get; set; }
    public string CustomerId { get; set; }
    public List<OrderItem> Items { get; set; } = [];
    public OrderStatus Status { get; set; }
    public decimal Total => Items.Sum(i => i.Price * i.Quantity);
}
```

### 2. Define Messages (What Happens)

```csharp
// Messages/OrderCommands.cs
public record CreateOrder(string CustomerId, List<OrderItem> Items) : IRequest<Order>;
public record ShipOrder(string OrderId, string TrackingNumber) : IRequest;

// Messages/OrderQueries.cs
public record GetOrder(string OrderId) : IRequest<Order>;
public record GetCustomerOrders(string CustomerId) : IRequest<List<Order>>;

// Messages/OrderEvents.cs
public record OrderCreated(string OrderId, string CustomerId, decimal Total) : IEvent;
public record OrderShipped(string OrderId, string TrackingNumber) : IEvent;
```

### 3. Implement Business Logic

```csharp
// Handlers/CreateOrderHandler.cs
public class CreateOrderHandler : IRequestHandler<CreateOrder, Order>
{
    private readonly IOrderRepository _orders;
    private readonly ICatgaMediator _mediator;

    public async ValueTask<CatgaResult<Order>> HandleAsync(CreateOrder cmd, CancellationToken ct)
    {
        // Business logic - validate, create, save
        var order = new Order
        {
            Id = Guid.NewGuid().ToString(),
            CustomerId = cmd.CustomerId,
            Items = cmd.Items,
            Status = OrderStatus.Created
        };

        await _orders.SaveAsync(order);

        // Publish domain event
        await _mediator.PublishAsync(new OrderCreated(order.Id, order.CustomerId, order.Total));

        return CatgaResult<Order>.Success(order);
    }
}
```

### 4. Define Workflows (Optional)

For complex multi-step operations, use Flow DSL:

```csharp
// Flows/OrderProcessingFlow.cs
public class OrderProcessingFlow : FlowConfig<OrderFlowState>
{
    protected override void Configure(IFlowBuilder<OrderFlowState> flow)
    {
        // Step 1: Reserve inventory (with compensation)
        flow.Send(s => new ReserveInventory(s.OrderId, s.Items))
            .Into(s => s.ReservationId)
            .IfFail(s => new ReleaseInventory(s.ReservationId));

        // Step 2: Process payment (with compensation)
        flow.Send(s => new ProcessPayment(s.OrderId, s.Total))
            .Into(s => s.PaymentId)
            .IfFail(s => new RefundPayment(s.PaymentId));

        // Step 3: Complete order
        flow.Publish(s => new OrderCompleted(s.OrderId));
    }
}
```

### 5. Minimal Configuration

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// One line setup
builder.Services.AddCatga().UseMemoryPack();
builder.Services.AddInMemoryTransport();
builder.Services.AddInMemoryPersistence();

var app = builder.Build();
app.MapCatgaEndpoints(); // Auto-register endpoints
app.Run();
```

## Key Patterns

| Pattern | When to Use |
|---------|-------------|
| **Command** | Change state (CreateOrder, ShipOrder) |
| **Query** | Read state (GetOrder, GetCustomerOrders) |
| **Event** | Notify about changes (OrderCreated, OrderShipped) |
| **Flow** | Multi-step operations with compensation |

## Configuration Options

```bash
# Development (default)
CATGA_PERSISTENCE=InMemory
CATGA_TRANSPORT=InMemory

# Production with Redis
CATGA_PERSISTENCE=Redis
CATGA_TRANSPORT=Redis
REDIS_CONNECTION=localhost:6379

# Production with NATS
CATGA_PERSISTENCE=Nats
CATGA_TRANSPORT=Nats
NATS_URL=nats://localhost:4222
```

## API Endpoints

| Endpoint | Description |
|----------|-------------|
| `POST /api/orders` | Create order |
| `GET /api/orders/{id}` | Get order |
| `POST /api/orders/{id}/ship` | Ship order |
| `GET /api/orders/customer/{id}` | Get customer orders |
| `POST /api/orders/flow` | Process order (workflow) |

## Testing

```bash
# Create order
curl -X POST http://localhost:5275/api/orders \
  -H "Content-Type: application/json" \
  -d '{"customerId":"C001","items":[{"productId":"P001","quantity":2,"price":99.99}]}'

# Get order
curl http://localhost:5275/api/orders/{orderId}
```
