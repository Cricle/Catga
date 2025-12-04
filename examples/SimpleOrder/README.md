# SimpleOrder - Minimal Catga Example

A minimal example showing Catga's core features in ~70 lines.

## Run

```bash
dotnet run
```

## Test

```bash
# Create order (success)
curl -X POST http://localhost:5000/orders \
  -H "Content-Type: application/json" \
  -d '{"customerId":"C001","amount":99.99}'

# Create order (payment fails - auto rollback)
curl -X POST http://localhost:5000/orders \
  -H "Content-Type: application/json" \
  -d '{"customerId":"C002","amount":50,"paymentMethod":"FAIL-Card"}'

# Get order
curl http://localhost:5000/orders/{orderId}
```

## Features Demonstrated

- **Flow** - Saga pattern with auto-compensation
- **MemoryPack** - High-performance serialization
- **InMemory Transport/Persistence** - For development

## Compare with OrderSystem.Api

| Aspect | SimpleOrder | OrderSystem.Api |
|--------|-------------|-----------------|
| Lines | ~70 | ~600 |
| Purpose | Learning | Production demo |
| Features | Core only | Full stack |
