# Catga Documentation Index

## 🚀 Quick Start

| Document | Description | Audience |
|----------|-------------|----------|
| [Main README](../README.md) | Framework overview and quick start | Everyone |
| [Quick Reference](QUICK-REFERENCE.md) | API cheat sheet (in docs/) | Developers |
| [Framework Roadmap](FRAMEWORK-ROADMAP.md) | Feature roadmap and vision | Architects |

---

## 📚 Core Documentation

### Getting Started
- [README](README.md) - Documentation hub
- [Basic Usage](examples/basic-usage.md) - Your first Catga app
- [Quick Reference](QUICK-REFERENCE.md) - 5-minute cheat sheet

### Architecture
- [Architecture Overview](architecture/ARCHITECTURE.md) - System design
- [CQRS Pattern](architecture/cqrs.md) - Command Query Responsibility Segregation
- [Responsibility Boundary](architecture/RESPONSIBILITY-BOUNDARY.md) - Module boundaries

### API Reference
- [API Overview](api/README.md) - API documentation hub
- [Mediator](api/mediator.md) - ICatgaMediator usage
- [Messages](api/messages.md) - Command, Query, Event types

---

## 🎯 Feature Guides

### Core Features
- [Auto DI Registration](guides/auto-di-registration.md) - Zero-config dependency injection
- [Time-Travel Debugger](DEBUGGER.md) - Production-grade debugging with Vue 3 UI
- [Debugger Architecture](CATGA-DEBUGGER-PLAN.md) - Complete debugger design & implementation
- [Serialization](guides/serialization.md) - MemoryPack vs JSON
- [Source Generator](guides/source-generator.md) - Auto-code generation
- [Distributed ID](guides/distributed-id.md) - Snowflake ID generator

### Advanced Features
- [Analyzers](guides/analyzers.md) - Compile-time checks
- [Source Generator Usage](guides/source-generator-usage.md) - Advanced SG scenarios

---

## 🌐 Distributed Systems

### Architecture
- [Distributed Architecture](distributed/ARCHITECTURE.md) - Distributed system design
- [Kubernetes Integration](distributed/KUBERNETES.md) - K8s service discovery
- [Distributed Overview](distributed/README.md) - Distributed features

### Patterns
- [Distributed Transactions V2](patterns/DISTRIBUTED-TRANSACTION-V2.md) - Catga transaction model

---

## 🚢 Deployment

### Native AOT
- [Native AOT Publishing](deployment/native-aot-publishing.md) - AOT compilation guide
- [AOT Serialization](aot/serialization-aot-guide.md) - Serializer AOT config

### Kubernetes
- [Kubernetes Deployment](deployment/kubernetes.md) - Complete K8s guide

---

## 📖 Examples

### OrderSystem (Recommended)
- [OrderSystem.Api](../examples/OrderSystem.Api/README.md) - Complete CQRS example
- [OrderSystem.AppHost](../examples/OrderSystem.AppHost/README.md) - Aspire orchestration
- [Graceful Lifecycle](../examples/OrderSystem.AppHost/README-GRACEFUL.md) - Shutdown & recovery

### Other Examples
- [Examples Overview](../examples/README.md) - All examples

---

## 📝 Reference Documents

### Implementation Summaries
- [Implementation Complete](IMPLEMENTATION-COMPLETE.md) - Feature completion summary
- [OrderSystem Complete](ORDERSYSTEM-COMPLETE.md) - OrderSystem implementation
- [Optimization Plan](OPTIMIZATION-PLAN.md) - Optimization strategy
- [Optimization Execution](OPTIMIZATION-EXECUTION.md) - Optimization results

### Project Info
- [Changelog](CHANGELOG.md) - Version history
- [Contributing](../CONTRIBUTING.md) - Contribution guide
- [Release Checklist](RELEASE-READINESS-CHECKLIST.md) - Release criteria
- [Project Summary](PROJECT_SUMMARY.md) - Project overview
- [Project Status](PROJECT-STATUS.md) - Current status

---

## 🗂️ Archive

Historical process documents (for reference only):
- [Archive](archive/) - Process documents, analysis reports

---

## 🎯 Quick Navigation

### By Role

**Beginners**:
1. [Main README](../README.md)
2. [Basic Usage](examples/basic-usage.md)
3. [OrderSystem Example](../examples/OrderSystem.Api/README.md)

**Developers**:
1. [Quick Reference](QUICK-REFERENCE.md)
2. [Auto DI Guide](guides/auto-di-registration.md)
3. [Serialization Guide](guides/serialization.md)

**Architects**:
1. [Framework Roadmap](FRAMEWORK-ROADMAP.md)
2. [Architecture](architecture/ARCHITECTURE.md)
3. [Distributed Architecture](distributed/ARCHITECTURE.md)

**DevOps**:
1. [Native AOT Publishing](deployment/native-aot-publishing.md)
2. [Kubernetes Deployment](deployment/kubernetes.md)
3. [Graceful Lifecycle](../examples/OrderSystem.AppHost/README-GRACEFUL.md)

---

## 📊 Documentation Statistics

```
Total Documents: 76
Core Guides: 15
Examples: 3
API Reference: 8
Architecture: 6
Deployment: 4
Archive: 13+
```

---

<div align="center">

**📖 Find what you need, fast!**

[Main README](../README.md) · [Quick Reference](QUICK-REFERENCE.md) · [Examples](../examples/)

</div>

