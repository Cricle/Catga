# Catga Examples

## OrderSystem.Api

Complete order management system demonstrating all Catga features.

### Quick Start

```bash
cd examples/OrderSystem.Api
dotnet run
```

Open http://localhost:5275/swagger for API documentation.

### Configuration

Environment variables for persistence and transport:

```bash
# Persistence: InMemory (default) | Redis | NATS
export CATGA_PERSISTENCE=InMemory

# Transport: InMemory (default) | Redis | NATS
export CATGA_TRANSPORT=InMemory

# Connection strings (when using Redis/NATS)
export REDIS_CONNECTION=localhost:6379
export NATS_URL=nats://localhost:4222
```

### Features Demonstrated

| Feature | Endpoint |
|---------|----------|
| **CQRS Pattern** | `POST /api/orders`, `GET /api/orders/{id}` |
| **Flow DSL (Saga)** | `POST /api/orders/flow` |
| **Event Sourcing** | `GET /api/timetravel/orders/{id}/history` |
| **Time Travel** | `GET /api/timetravel/orders/{id}/version/{v}` |
| **Projections** | `GET /api/projections/order-summary` |
| **Subscriptions** | `GET /api/subscriptions` |
| **Audit Logs** | `GET /api/audit/logs/{streamId}` |
| **GDPR Compliance** | `POST /api/audit/gdpr/erasure-request` |
| **Snapshots** | `POST /api/snapshots/orders/{id}` |

### Simplified Setup (New!)

Using unified `UseXxx` methods for one-call persistence registration:

```csharp
// InMemory (development/testing)
services.AddCatga(opt => opt.ForDevelopment())
    .UseInMemory();

// Redis (production)
services.AddCatga(opt => opt.Minimal())
    .UseRedis("localhost:6379");

// NATS (production)
services.AddNatsConnection("nats://localhost:4222");
services.AddCatga(opt => opt.Minimal())
    .UseNats();
```

## Project Structure

```
examples/
├── OrderSystem.Api/          # Main API application
│   ├── Handlers/             # CQRS command/query handlers
│   ├── Services/             # Business services
│   ├── Domain/               # Domain models & aggregates
│   └── Messages/             # Commands, queries, events
├── OrderSystem.AppHost/      # Aspire orchestration
└── OrderSystem.ServiceDefaults/ # Shared service defaults
```
