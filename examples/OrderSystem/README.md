# Catga OrderSystem Example

Minimal CQRS demo supporting InMemory, Redis, and NATS backends.

## Quick Start

```bash
# InMemory (default)
dotnet run

# Redis
dotnet run -- --transport redis --persistence redis --redis localhost:6379

# NATS
dotnet run -- --transport nats --persistence nats --nats nats://localhost:4222
```

## Docker Compose

```bash
# Start Redis + NATS
docker-compose up -d

# Run with Redis
dotnet run -- --transport redis --persistence redis

# Run with NATS
dotnet run -- --transport nats --persistence nats
```

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/` | System info |
| GET | `/health` | Health check |
| POST | `/orders` | Create order |
| GET | `/orders` | List all orders |
| GET | `/orders/{id}` | Get order |
| POST | `/orders/{id}/pay` | Pay order |
| POST | `/orders/{id}/ship` | Ship order |
| POST | `/orders/{id}/cancel` | Cancel order |
| GET | `/orders/{id}/history` | Event history |

## Example Requests

```bash
# Create order
curl -X POST http://localhost:5000/orders \
  -H "Content-Type: application/json" \
  -d '{"customerId":"c1","items":[{"productId":"p1","name":"Widget","quantity":2,"price":9.99}]}'

# Pay order
curl -X POST http://localhost:5000/orders/{id}/pay \
  -H "Content-Type: application/json" \
  -d '{"paymentMethod":"credit_card"}'

# Ship order
curl -X POST http://localhost:5000/orders/{id}/ship \
  -H "Content-Type: application/json" \
  -d '{"trackingNumber":"TRK123"}'

# Get order history
curl http://localhost:5000/orders/{id}/history
```

## Cluster Mode

Run multiple instances with different ports:

```bash
# Node 1 (Leader)
dotnet run -- --cluster --node-id node1 --port 5001 --transport redis --persistence redis

# Node 2 (Follower)
dotnet run -- --cluster --node-id node2 --port 5002 --transport redis --persistence redis

# Node 3 (Follower)
dotnet run -- --cluster --node-id node3 --port 5003 --transport redis --persistence redis
```

## AOT Compilation

```bash
dotnet publish -c Release
```
