# OrderSystem.Api - Catga Best Practices Example

A comprehensive production-ready example demonstrating how to build applications with the Catga CQRS framework.

## Architecture

This example follows Clean Architecture principles with clear separation of concerns:

```
OrderSystem.Api/
├── Configuration/          # Options pattern configuration classes
│   └── CatgaConfiguration.cs
├── Domain/                 # Domain models, aggregates, value objects
│   ├── Order.cs
│   └── EventSourcing.cs
├── Endpoints/              # Minimal API endpoint definitions
│   ├── OrderEndpoints.cs
│   └── EventSourcingEndpoints.cs
├── Flows/                  # Flow DSL workflow definitions
│   └── ComprehensiveOrderFlow.cs
├── Handlers/               # CQRS handlers and pipeline behaviors
│   └── OrderHandlers.cs
├── Infrastructure/         # Cross-cutting concerns
│   ├── GlobalExceptionHandler.cs
│   ├── HealthChecks.cs
│   └── ObservabilityExtensions.cs
├── Messages/               # Commands, queries, events
│   └── Commands.cs
├── Services/               # Domain services
└── Program.cs              # Application entry point
```

## Best Practices Demonstrated

### 1. Configuration with Options Pattern
```csharp
// appsettings.json
{
  "Catga": {
    "Transport": "InMemory",
    "Persistence": "InMemory",
    "DevelopmentMode": true
  }
}

// Strongly-typed access
builder.Services.Configure<CatgaOptions>(
    builder.Configuration.GetSection(CatgaOptions.SectionName));
```

### 2. Clean Endpoint Organization
```csharp
// Endpoints are defined in separate files
app.MapOrderEndpoints();
app.MapEventSourcingEndpoints();
```

### 3. Global Exception Handling
```csharp
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
```

### 4. OpenTelemetry Observability
```csharp
builder.Services.AddObservability(builder.Configuration);
// Automatic tracing for Catga operations
// Metrics for performance monitoring
```

### 5. Comprehensive Health Checks
```csharp
app.MapOrderSystemHealthChecks();
// /health/live   - Liveness probe
// /health/ready  - Readiness probe
// /health        - Full health status
```

### 6. Source Generator Handler Registration
```csharp
// Zero-reflection, AOT-compatible
builder.Services.AddCatgaHandlers();
```

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

### Authentication
| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/auth/login` | POST | Login with email/password |
| `/api/auth/register` | POST | Register new user |
| `/api/auth/me` | GET | Get current user (requires auth) |
| `/api/auth/refresh` | POST | Refresh JWT token |

### Orders
| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/orders` | POST | Create order (simple) |
| `/api/orders/flow` | POST | Create order (Flow pattern) |
| `/api/orders/{id}` | GET | Get order |
| `/api/orders/{id}/cancel` | POST | Cancel order |
| `/api/orders/customer/{id}` | GET | Get user orders |
| `/api/orders/stats` | GET | Get order statistics |

### Payments
| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/payments/process` | POST | Process order payment |
| `/api/payments/{id}/refund` | POST | Refund payment |
| `/api/payments/{id}` | GET | Get payment details |

### System
| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/system/info` | GET | System information |
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
// 1. Define state class with [FlowState] attribute (source-generated IFlowState)
[FlowState]
public partial class CreateOrderFlowState
{
    public string? OrderId { get; set; }
    public decimal TotalAmount { get; set; }

    [FlowStateIgnore]  // Excluded from change tracking
    public string? CustomerId { get; set; }
}

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

### Authentication

```bash
# Register new user
curl -X POST http://localhost:5275/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "email":"user@example.com",
    "password":"password123",
    "fullName":"John Doe"
  }'

# Login
curl -X POST http://localhost:5275/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "email":"admin@ordersystem.local",
    "password":"admin123"
  }'

# Get current user (requires Bearer token)
curl -H "Authorization: Bearer YOUR_TOKEN" \
  http://localhost:5275/api/auth/me
```

### Orders

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

# Get user orders
curl http://localhost:5275/api/orders/customer/C001

# Get order statistics
curl http://localhost:5275/api/orders/stats
```

### Payments

```bash
# Process payment
curl -X POST http://localhost:5275/api/payments/process \
  -H "Content-Type: application/json" \
  -d '{
    "orderId":"order-123",
    "method":"Alipay",
    "transactionId":"txn-456"
  }'

# Refund payment
curl -X POST http://localhost:5275/api/payments/payment-id/refund \
  -H "Content-Type: application/json" \
  -d '{"reason":"Customer request"}'
```

## Docker Deployment

### Quick Start

```bash
# InMemory mode (development)
docker-compose -f docker-compose.prod.yml --profile memory up -d

# Redis mode (distributed)
docker-compose -f docker-compose.prod.yml --profile redis up -d

# NATS mode (high performance)
docker-compose -f docker-compose.prod.yml --profile nats up -d

# Cluster mode (3 nodes + nginx)
docker-compose -f docker-compose.prod.yml --profile cluster up -d

# Full stack (all services + monitoring)
docker-compose -f docker-compose.prod.yml --profile full up -d
```

### Profiles

| Profile | Services | Use Case |
|---------|----------|----------|
| `memory` | OrderSystem + SQLite | Development/Testing |
| `redis` | OrderSystem + Redis | Distributed deployment |
| `nats` | OrderSystem + NATS | High throughput |
| `cluster` | 3x OrderSystem + Redis + nginx | High availability |
| `monitoring` | Prometheus + Grafana | Observability |
| `full` | All services | Complete stack |

### Environment Variables

```bash
# Transport: InMemory | Redis | NATS
Catga__Transport=Redis

# Persistence: InMemory | Redis | NATS | SQLite
Catga__Persistence=Redis

# Redis connection
Catga__RedisConnection=redis:6379

# NATS URL
Catga__NatsUrl=nats://nats:4222

# SQLite path
Catga__SqliteConnection=Data Source=/data/orders.db

# Cluster mode
Catga__ClusterEnabled=true
Catga__ClusterNodes=node1:8080,node2:8080,node3:8080
```

### Build Image

```bash
# Build from project root
docker build -t ordersystem:latest -f examples/OrderSystem.Api/Dockerfile .

# Or use compose
docker-compose -f docker-compose.prod.yml build
```

### Health Checks

```bash
# Check health
curl http://localhost:5275/health

# Detailed health
curl http://localhost:5275/health/ready
```

## Frontend (Vue + Vuestic)

```bash
cd client-app

# Development
npm install
npm run dev

# Build for production
npm run build
```

Access the UI at http://localhost:5275
