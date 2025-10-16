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

### 🌟 创新特性 (Innovation)
- **[Time-Travel Debugger](DEBUGGER.md)** 🌟 - 完整的时间旅行调试系统（零开销）
- **[Debugger Architecture Plan](../CATGA-DEBUGGER-PLAN.md)** - 2900+ 行完整设计文档
- **[Source Generator Debug Capture](SOURCE-GENERATOR-DEBUG-CAPTURE.md)** - AOT 兼容的变量捕获
- **[Debugger AOT Compatibility](../src/Catga.Debugger/AOT-COMPATIBILITY.md)** - AOT 兼容性详解

### Core Features
- [Auto DI Registration](guides/auto-di-registration.md) - Zero-config dependency injection
- [Serialization](guides/serialization.md) - MemoryPack vs JSON
- [Source Generator](guides/source-generator.md) - Auto-code generation
- [Distributed ID](guides/distributed-id.md) - Snowflake ID generator
- [Graceful Lifecycle](guides/graceful-lifecycle.md) - Graceful shutdown & recovery

### Advanced Features
- [Analyzers](guides/analyzers.md) - Compile-time checks
- [Source Generator Usage](guides/source-generator-usage.md) - Advanced SG scenarios
- [Batch Operations](../src/Catga/Core/BatchOperationExtensions.cs) - High-performance batch processing

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

### 🌟 OrderSystem (Recommended - 完整演示)
- **[OrderSystem Complete Guide](../examples/README-ORDERSYSTEM.md)** 🌟 - 420+ 行完整指南
- [OrderSystem.Api README](../examples/OrderSystem.Api/README.md) - API 项目说明
- [OrderSystem.AppHost](../examples/OrderSystem.AppHost/README.md) - Aspire orchestration
- [Graceful Lifecycle Demo](../examples/OrderSystem.AppHost/README-GRACEFUL.md) - Shutdown & recovery

**OrderSystem 演示功能**：
- ✅ CQRS 完整实现（Commands + Queries + Events）
- ✅ 多 Event Handler（6 个 Handler 同时响应）
- ✅ Time-Travel Debugger 集成
- ✅ Graceful Lifecycle（优雅关闭和恢复）
- ✅ OpenTelemetry 分布式追踪
- ✅ .NET Aspire 集成

### Other Resources
- [Examples Overview](../examples/README.md) - All examples index

---

## 📝 Reference Documents

### 📊 Project Status & Planning
- **[Execution Summary](../EXECUTION-SUMMARY.md)** 🌟 - 最新完成状态（450+ 行）
- **[Final Improvement Plan](../FINAL-IMPROVEMENT-PLAN.md)** - 完整改进计划（410 行）
- **[Implementation Status](../IMPLEMENTATION-STATUS.md)** - 实现状态报告（370 行）
- [Current Status & Next Steps](../CURRENT-STATUS-AND-NEXT-STEPS.md) - 当前状态和下一步
- [Code Review Summary](../CODE-REVIEW-SUMMARY.md) - 370+ 行代码质量报告

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
Total Documents: 85+
Core Guides: 20+
Examples: 4 (OrderSystem complete)
API Reference: 8
Architecture: 6
Deployment: 4
Planning & Status: 5 (2,000+ lines)
Debugger Docs: 4 (3,500+ lines)
Archive: 13+
```

**Recent Additions** (2024 Latest):
- ✅ Time-Travel Debugger 完整文档（3,500+ 行）
- ✅ OrderSystem 完整演示指南（420 行）
- ✅ 项目状态和规划文档（2,000+ 行）
- ✅ Source Generator Debug Capture 文档
- ✅ AOT 兼容性详细说明

---

<div align="center">

**📖 Find what you need, fast!**

[Main README](../README.md) · [Quick Reference](QUICK-REFERENCE.md) · [Examples](../examples/)

</div>

