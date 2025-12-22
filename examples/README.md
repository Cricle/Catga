# Catga Examples

This directory contains example applications demonstrating Catga framework features.

## OrderSystem

A comprehensive example showcasing all Catga features in a simple order management system.

**Location**: `OrderSystem/`

**Features**:
- ✅ CQRS Pattern (Commands & Queries)
- ✅ Event Sourcing (Complete event history)
- ✅ Multiple Backends (InMemory, Redis, NATS)
- ✅ Distributed Messaging (Pub/Sub)
- ✅ Cluster Mode (Multi-node deployment)
- ✅ AOT Compilation (Native compilation ready)
- ✅ MemoryPack Serialization (High-performance)

**Quick Start**:
```bash
cd OrderSystem
dotnet run
```

**Documentation**:
- [README.md](OrderSystem/README.md) - Complete usage guide
- [FEATURES.md](OrderSystem/FEATURES.md) - Feature showcase
- [quick-test.ps1](OrderSystem/quick-test.ps1) - Quick validation

**Deployment Modes**:
1. **InMemory** (Development)
   ```bash
   dotnet run
   ```

2. **Redis** (Production)
   ```bash
   docker run -d -p 6379:6379 redis:alpine
   dotnet run -- --transport redis --persistence redis
   ```

3. **NATS** (High-performance)
   ```bash
   docker run -d -p 4222:4222 nats:alpine -js
   dotnet run -- --transport nats --persistence nats
   ```

4. **Cluster** (3 nodes)
   ```bash
   docker run -d -p 6379:6379 redis:alpine
   
   # Terminal 1
   dotnet run -- --cluster --node-id node1 --port 5001 --transport redis --persistence redis
   
   # Terminal 2
   dotnet run -- --cluster --node-id node2 --port 5002 --transport redis --persistence redis
   
   # Terminal 3
   dotnet run -- --cluster --node-id node3 --port 5003 --transport redis --persistence redis
   ```

## Getting Started

1. **Clone the repository**
   ```bash
   git clone https://github.com/Cricle/Catga.git
   cd Catga/examples
   ```

2. **Run an example**
   ```bash
   cd OrderSystem
   dotnet run
   ```

3. **Test the API**
   ```bash
   # System info
   curl http://localhost:5000/
   
   # Create order
   curl -X POST http://localhost:5000/orders \
     -H "Content-Type: application/json" \
     -d '{"customerId":"c1","items":[{"productId":"p1","name":"Product","quantity":1,"price":99.99}]}'
   
   # Get statistics
   curl http://localhost:5000/stats
   ```

## Learn More

- [Catga Documentation](../docs/README.md)
- [CQRS Pattern](../docs/patterns/cqrs.md)
- [Event Sourcing](../docs/patterns/event-sourcing.md)
- [Deployment Guide](../docs/deployment/README.md)

## Contributing

Examples should be:
- **Simple**: Easy to understand
- **Complete**: Show real-world usage
- **Documented**: Clear README and comments
- **Tested**: Include test scripts

Feel free to contribute new examples!
