# Catga Documentation

Complete documentation for the Catga CQRS/Mediator framework.

## 📚 Table of Contents

### Getting Started
- [Quick Start Guide](guides/quick-start.md) - Get up and running in 5 minutes
- [Getting Started](guides/GETTING_STARTED.md) - Detailed introduction
- [Basic Usage Examples](examples/basic-usage.md) - Common use cases
- [Quick Reference](QUICK_REFERENCE.md) - API reference at a glance

### Architecture
- [Architecture Overview](architecture/overview.md) - System design and concepts
- [CQRS Pattern](architecture/cqrs.md) - Command Query Responsibility Segregation
- [Architecture Documentation](architecture/ARCHITECTURE.md) - Detailed architecture
- [Transport & Storage Separation](architecture/TRANSPORT_STORAGE_SEPARATION.md) - MassTransit-inspired design

### Core Features

#### API Documentation
- [Mediator API](api/mediator.md) - Core mediator interface
- [Messages](api/messages.md) - Commands, Queries, Events

#### Patterns
- [Outbox/Inbox Pattern](patterns/outbox-inbox.md) - Reliable messaging
- [Implementation Guide](patterns/OUTBOX_INBOX_IMPLEMENTATION.md) - Detailed implementation

#### Serialization
- [Serialization Overview](serialization/README.md) - JSON, MemoryPack, and custom serializers

### Advanced Topics

#### Native AOT Support
- [AOT Overview](aot/README.md) - Native AOT compatibility
- [AOT Best Practices](aot/AOT_BEST_PRACTICES.md) - Guidelines for AOT-friendly code
- [Native AOT Guide](aot/native-aot-guide.md) - Complete AOT guide
- [AOT Verification Report](aot/AOT_VERIFICATION_REPORT.md) - 100% AOT compatibility proof
- [AOT Complete Summary (中文)](aot/AOT_COMPLETE_SUMMARY.md) - Chinese summary

#### Performance
- [Performance Overview](performance/README.md) - Performance characteristics
- [Benchmarking Guide](performance/benchmarking.md) - How to benchmark
- [Optimization Techniques](performance/optimization.md) - Performance tuning
- [Zero Allocation Guide](performance/zero-allocation.md) - Memory optimization
- [Memory Management](performance/memory-management.md) - GC optimization
- [CPU Optimization](performance/cpu-optimization.md) - CPU performance
- [Concurrency Guide](performance/concurrency.md) - Concurrent processing
- [Profiling Guide](performance/profiling.md) - Performance profiling

#### Distributed Systems
- [Distributed Overview](distributed/README.md) - Distributed capabilities
- [Cluster Architecture](distributed/CLUSTER_ARCHITECTURE_ANALYSIS.md) - Cluster design
- [Cluster Support](distributed/DISTRIBUTED_CLUSTER_SUPPORT.md) - Distributed features
- [P2P Architecture](distributed/PEER_TO_PEER_ARCHITECTURE.md) - Peer-to-peer patterns

#### Observability
- [Observability Overview](observability/README.md) - Monitoring and logging
- [Complete Guide](observability/OBSERVABILITY_COMPLETE.md) - Full observability guide

#### Testing
- [API Testing Guide](guides/API_TESTING_GUIDE.md) - How to test your APIs

### Project Information
- [Project Structure](PROJECT_STRUCTURE.md) - Repository organization

## 🚀 Quick Links

### For Beginners
1. Start with [Quick Start Guide](guides/quick-start.md)
2. Read [Basic Usage Examples](examples/basic-usage.md)
3. Check out [examples/](../examples/) directory

### For Advanced Users
1. Review [Architecture Overview](architecture/overview.md)
2. Explore [Native AOT Support](aot/README.md)
3. Study [Performance Optimization](performance/README.md)

### For Contributors
1. Read [CONTRIBUTING.md](../CONTRIBUTING.md)
2. Check [Project Structure](PROJECT_STRUCTURE.md)
3. Review [Architecture Documentation](architecture/ARCHITECTURE.md)

## 📦 Examples

All examples are located in the [`examples/`](../examples/) directory:

- **AotDemo** - Native AOT demonstration (4.84MB, 55ms startup)
- **ComprehensiveDemo** - Full feature showcase
- See [examples/README.md](../examples/README.md) for more

## 🎯 Key Features Documented

- ✅ **CQRS Pattern** - Command Query separation
- ✅ **Mediator Pattern** - Decoupled messaging
- ✅ **Pipeline Behaviors** - Middleware-style processing
- ✅ **Outbox/Inbox** - Reliable messaging patterns
- ✅ **Native AOT** - 100% AOT compatible
- ✅ **High Performance** - Zero-allocation paths
- ✅ **Distributed** - Cluster and P2P support
- ✅ **Observability** - Logging, metrics, tracing
- ✅ **Extensible** - Plugin architecture

## 📝 Document Organization

```
docs/
├── README.md (this file)          # Documentation index
├── QUICK_REFERENCE.md             # API quick reference
├── PROJECT_STRUCTURE.md           # Project organization
│
├── guides/                        # Getting started guides
├── architecture/                  # Architecture documentation
├── api/                          # API documentation
├── patterns/                     # Design patterns
├── serialization/                # Serialization guides
├── aot/                          # Native AOT documentation
├── performance/                  # Performance optimization
├── distributed/                  # Distributed systems
├── observability/                # Observability guides
└── examples/                     # Usage examples
```

## 🔗 External Resources

- [GitHub Repository](https://github.com/yourusername/Catga)
- [NuGet Packages](https://www.nuget.org/packages?q=Catga)
- [Issue Tracker](https://github.com/yourusername/Catga/issues)

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](../LICENSE) file for details.

---

**Need help?** Check the [Quick Start Guide](guides/quick-start.md) or open an issue on GitHub.
