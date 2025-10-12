# Catga Microservices RPC Demo

High-performance, lock-free, AOT-compatible microservice RPC communication.

## Architecture

```
OrderService (Client)  ──RPC──>  UserService (Server)
     │                                │
     └────────── NATS Transport ──────┘
```

## Features

- **Multi-Transport**: NATS, Redis, HTTP (extensible)
- **AOT Compatible**: Full Native AOT support
- **Lock-Free**: Zero-lock design for maximum performance
- **Type-Safe**: Strongly typed contracts with MemoryPack
- **Simple API**: Clean, minimal configuration

## Quick Start

### 1. Start NATS

```bash
docker run -p 4222:4222 nats:latest
```

### 2. Run UserService

```bash
cd UserService
dotnet run
```

### 3. Run OrderService

```bash
cd OrderService
dotnet run
```

### 4. Test

```bash
curl -X POST http://localhost:5001/orders \
  -H "Content-Type: application/json" \
  -d '{
    "userId": 123,
    "items": ["Item1", "Item2"],
    "totalAmount": 99.99
  }'
```

## Code Example

### Server (UserService)

```csharp
builder.Services.AddCatgaRpcServer(options => {
    options.ServiceName = "UserService";
});

rpcServer.RegisterHandler<GetUserRequest, GetUserResponse>(
    "GetUser",
    async (request, ct) => new GetUserResponse {
        UserId = request.UserId,
        UserName = $"User_{request.UserId}"
    });
```

### Client (OrderService)

```csharp
builder.Services.AddCatgaRpcClient(options => {
    options.ServiceName = "OrderService";
});

var result = await rpcClient.CallAsync<GetUserRequest, GetUserResponse>(
    "UserService",
    "GetUser",
    new GetUserRequest { UserId = 123 });
```

## Performance

- **Zero allocations** in hot path
- **Lock-free** concurrent request handling
- **< 1ms** latency for local NATS calls
- **10K+ RPS** per service instance

## Transport Options

### NATS (Recommended)
```csharp
builder.Services.AddNatsTransport("nats://localhost:4222");
```

### Redis
```csharp
builder.Services.AddRedisTransport("localhost:6379");
```

### Custom
Implement `IMessageTransport` for any transport layer.

