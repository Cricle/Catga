# Catga Project Structure

## 📁 Root Directory

```
Catga/
├── src/                    # Source code
├── tests/                  # Test projects
├── examples/               # Example projects
├── benchmarks/             # Performance benchmarks
├── docs/                   # Documentation
├── Catga.sln              # Solution file
├── Directory.Build.props  # Global build properties
├── Directory.Packages.props # Centralized package versions
└── README.md              # Main documentation
```

## 🔧 Source Projects (`src/`)

### Core Libraries
- **Catga** - Core abstractions and types
  - `Abstractions/` - Interfaces (ICatgaMediator, IMessageTransport, IRpcClient, etc.)
  - `Core/` - Base types (CatgaResult, CatgaOptions, QoS, etc.)
  - `Messages/` - Message contracts
  - `Handlers/` - Handler contracts
  - `Rpc/` - RPC client/server implementation

- **Catga.InMemory** - In-memory implementation
  - Core mediator and message transport
  - Pipeline behaviors (Caching, Retry, Logging, etc.)
  - Stores (Idempotency, Inbox, Outbox, DLQ)
  - DI extensions

### Distributed Features
- **Catga.Distributed** - Distributed mediator core
  - Routing strategies (RoundRobin, ConsistentHash, LoadBased, etc.)
  - Node discovery interface
  - Heartbeat service

- **Catga.Distributed.Nats** - NATS-based cluster
  - JetStream KV node discovery
  - NATS PubSub node discovery

- **Catga.Distributed.Redis** - Redis-based cluster
  - Redis node discovery
  - Redis Stream transport

### Transport & Serialization
- **Catga.Transport.Nats** - NATS message transport
  - QoS 0: Core Pub/Sub
  - QoS 1: JetStream with ACK
  - QoS 2: Deduplication

- **Catga.Serialization.Json** - System.Text.Json serializer
- **Catga.Serialization.MemoryPack** - MemoryPack serializer (AOT-friendly)

### Persistence
- **Catga.Persistence.Redis** - Redis-based stores
  - Idempotency store
  - Distributed lock
  - Distributed cache
  - Dead letter queue

### Integration
- **Catga.AspNetCore** - ASP.NET Core integration
  - Endpoint extensions
  - Result conversions
  - Swagger integration
  - RPC extensions

### Tooling
- **Catga.Analyzers** - Roslyn analyzers
  - AOT compatibility checks
  - Performance analyzers
  - Best practice enforcers

- **Catga.SourceGenerator** - Source generators
  - Handler registration

## 📚 Documentation (`docs/`)

```
docs/
├── api/                   # API documentation
├── architecture/          # Architecture docs
├── distributed/           # Distributed features
├── examples/              # Example guides
├── guides/                # How-to guides
├── QUICK_START_RPC.md     # RPC quick start
├── RPC_IMPLEMENTATION.md  # RPC implementation details
└── README.md              # Documentation index
```

## 🎯 Examples (`examples/`)

- **MicroservicesDemo** - RPC microservices example
  - UserService (RPC server)
  - OrderService (RPC client)
  - Shared contracts
  - Docker Compose setup

- **OrderSystem** - SQLite distributed order system
  - Demonstrates cluster capabilities
  - Uses NATS for messaging
  - Aspire orchestration

- **RedisExample** - Redis-based messaging

## 🧪 Tests (`tests/`)

- **Catga.Tests** - Core unit tests

## ⚡ Benchmarks (`benchmarks/`)

- **Catga.Benchmarks** - BenchmarkDotNet performance tests
  - Serialization benchmarks
  - ID generation benchmarks
  - Allocation benchmarks

## 🎯 Design Principles

1. **Flat Structure** - Minimal nesting, files are easy to find
2. **Feature-Based** - Projects organized by feature, not layer
3. **AOT-First** - All code is AOT-compatible
4. **Zero-Lock** - Lock-free concurrent data structures
5. **High Performance** - Optimized for throughput and latency

## 📊 Project Statistics

- **Total Projects**: 12
- **Core Libraries**: 5
- **Transport/Serialization**: 3
- **Distributed**: 3
- **Integration**: 1
- **Tooling**: 2

## 🚀 Quick Navigation

| Need | Go to |
|------|-------|
| Core types | `src/Catga/` |
| CQRS implementation | `src/Catga.InMemory/` |
| Distributed cluster | `src/Catga.Distributed/` |
| RPC functionality | `src/Catga/Rpc/` + `src/Catga.AspNetCore/Rpc/` |
| NATS support | `src/Catga.Transport.Nats/` + `src/Catga.Distributed.Nats/` |
| Redis support | `src/Catga.Persistence.Redis/` + `src/Catga.Distributed.Redis/` |
| ASP.NET Core | `src/Catga.AspNetCore/` |
| Examples | `examples/MicroservicesDemo/` |
| Documentation | `docs/` |

## 📝 Notes

- All source projects support **Native AOT**
- Centralized package management via `Directory.Packages.props`
- Consistent code style across all projects
- Minimal dependencies, maximum performance

