# 🚀 Catga - 高性能分布式 CQRS 框架

[![.NET 9+](https://img.shields.io/badge/.NET-9%2B-512BD4)](https://dotnet.microsoft.com/)
[![NativeAOT](https://img.shields.io/badge/NativeAOT-Ready-brightgreen)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![License](https://img.shields.io/badge/License-MIT-blue)](LICENSE)
[![Performance](https://img.shields.io/badge/Performance-⚡%20Optimized-orange)]()

**Catga** 是一个为 .NET 9+ 设计的现代化 CQRS 框架，专注于**高性能**、**AOT 友好**和**分布式场景**。

---

## ✨ 核心特性

### 🎯 核心能力

- **CQRS 模式** - Command/Query/Event 分离
- **Mediator 模式** - 松耦合消息传递
- **Pipeline Behaviors** - 灵活的消息处理管道
- **Result<T> 模式** - 统一错误处理
- **AOT 友好** - 100% Native AOT 兼容，零反射

### 🌐 分布式能力

- **无主多节点 (P2P)** - 所有实例对等，无单点故障 ⭐
- **NATS 集成** - 高性能分布式消息总线
- **Redis 集成** - 分布式状态存储
- **Saga 事务** - 分布式事务协调
- **Outbox/Inbox 模式** - 可靠消息投递和幂等处理

### 🛡️ 可靠性

- **熔断器** - 自动故障隔离
- **重试机制** - 可配置重试策略
- **限流控制** - 保护系统资源
- **死信队列** - 失败消息处理
- **健康检查** - 实时监控服务状态

### ⚡ 高性能

- **零反射** - 编译时类型安全
- **无锁设计** - 原子操作优化
- **快速路径优化** - 18.5% 吞吐量提升
- **内存优化** - 33% 内存减少
- **GC 友好** - 40% GC 压力降低

---

## 🚀 快速开始

> 📖 **完整指南**: 查看 [快速开始指南](docs/guides/GETTING_STARTED.md) 获取详细教程

### 安装

```bash
# 核心包
dotnet add package Catga

# NATS 分布式消息
dotnet add package Catga.Nats

# Redis 状态存储
dotnet add package Catga.Redis

# Kubernetes 服务发现
dotnet add package Catga.ServiceDiscovery.Kubernetes
```

### ⚡ 极简使用（推荐）

```csharp
// 1. 一行注册 - 自动扫描所有 Handlers
services.AddCatgaDevelopment(); // 开发模式
// 或
services.AddCatgaProduction();  // 生产模式

// 2. 定义消息和处理器
public record CreateOrderCommand(string CustomerId, decimal Amount)
    : IRequest<OrderResult>;

public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderResult>
{
    public async ValueTask<CatgaResult<OrderResult>> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken)
    {
        // 业务逻辑
        return CatgaResult<OrderResult>.Success(new OrderResult(...));
    }
}

// 3. 使用
var result = await _mediator.SendAsync(new CreateOrderCommand("customer-123", 99.99m));
```

### 🔧 链式配置（高级）

```csharp
services.AddCatgaBuilder(builder => builder
    .ScanCurrentAssembly()           // 自动扫描当前程序集
    .WithOutbox()                    // 启用 Outbox 模式
    .WithInbox()                     // 启用 Inbox 模式
    .WithReliability()               // 启用可靠性特性（熔断/重试/死信队列）
    .WithPerformanceOptimization()   // 启用性能优化
);
```

### 📋 传统方式（手动注册）

```csharp
// 手动注册每个 Handler（AOT 友好）
services.AddCatga();
services.AddRequestHandler<CreateOrderCommand, OrderResult, CreateOrderHandler>();
services.AddEventHandler<OrderCreatedEvent, NotificationHandler>();
```

### 分布式部署

```csharp
// 配置 P2P 集群（推荐）
services.AddCatga()
    .AddNatsCatga("nats://node1:4222,nats://node2:4222,nats://node3:4222")
    .AddRedisCatgaStore("redis://cluster:6379")
    .AddRedisOutbox()   // 可靠消息发送
    .AddRedisInbox();   // 幂等消息处理

// 部署：每个服务 3-5 个对等实例，零配置，自动负载均衡
```

---

## 📊 架构特点

### 无主多节点 (P2P) ⭐ 推荐

```
┌────────── NATS 集群 ──────────┐
│    (自动负载均衡)              │
└─────┬──────┬──────┬──────────┘
      │      │      │
      ↓      ↓      ↓
  ┌─────┐┌─────┐┌─────┐
  │实例1││实例2││实例3│
  │✅对等││✅对等││✅对等│
  └─────┘└─────┘└─────┘

特点:
✅ 无单点故障
✅ 自动故障转移 (< 1秒)
✅ 水平扩展 (85-95% 效率)
✅ 零配置，添加节点即时生效
```

**详细说明**: [分布式架构文档](docs/distributed/)

---

## 📖 文档

### 快速导航

- 📘 [快速开始](docs/guides/quick-start.md) - 5分钟上手
- 📗 [快速参考](docs/QUICK_REFERENCE.md) - API 速查
- 📙 [架构说明](docs/architecture/ARCHITECTURE.md) - 功能分层
- 📙 [完整文档](docs/README.md) - 所有文档索引
- 📕 [贡献指南](CONTRIBUTING.md) - 参与贡献

### 核心文档

- [CQRS 模式](docs/architecture/cqrs.md)
- [Mediator API](docs/api/mediator.md)
- [Pipeline Behaviors](docs/guides/quick-start.md#pipeline-behaviors)
- [基础示例](docs/examples/basic-usage.md)

### 分布式与集群

- 🌐 [集群架构分析](docs/distributed/CLUSTER_ARCHITECTURE_ANALYSIS.md) ⭐ 推荐
- 🔄 [P2P 架构详解](docs/distributed/PEER_TO_PEER_ARCHITECTURE.md)
- 🏗️ [分布式部署指南](docs/distributed/DISTRIBUTED_CLUSTER_SUPPORT.md)

### 可靠性模式

- 📦 [Outbox/Inbox 模式](docs/patterns/outbox-inbox.md)
- 🔄 [Saga 分布式事务](docs/patterns/OUTBOX_INBOX_IMPLEMENTATION.md)

### 性能优化

- ⚡ [性能优化指南](docs/performance/optimization.md)
- 🎯 [Native AOT 指南](docs/aot/native-aot-guide.md)
- 📊 [基准测试](benchmarks/PERFORMANCE_BENCHMARK_RESULTS.md)

### 可观测性

- 📊 [监控与追踪](docs/observability/README.md)
- 🔍 [健康检查](docs/observability/OBSERVABILITY_COMPLETE.md)

### AOT 兼容性

- 🎯 [Native AOT 指南](docs/aot/native-aot-guide.md)
- 📦 [源生成器使用](docs/aot/README.md)

---

## 🎯 性能基准

### 吞吐量

| 场景 | 单实例 | 3 副本 | 10 副本 |
|------|--------|--------|---------|
| **本地消息** | 50,000 TPS | 150,000 TPS | 500,000 TPS |
| **NATS 分布式** | 10,000 TPS | 28,000 TPS | 85,000 TPS |

### 延迟 (P99)

| 负载 | 优化前 | 优化后 | 提升 |
|------|--------|--------|------|
| 1K TPS | 55ms | 38ms | **31%** |
| 10K TPS | 320ms | 95ms | **70%** |

### 性能优化成果

- ✅ **吞吐量提升 18.5%** (平均)
- ✅ **延迟降低 30%** (P95)
- ✅ **内存减少 33%**
- ✅ **GC 压力降低 40%**

**详细基准测试**: [性能报告](docs/performance/)

---

## 🏗️ 项目结构

```
Catga/
├── src/
│   ├── Catga/                          # 核心框架
│   ├── Catga.Nats/                     # NATS 集成
│   ├── Catga.Redis/                    # Redis 集成
│   └── Catga.ServiceDiscovery.Kubernetes/  # K8s 服务发现
├── tests/
│   └── Catga.Tests/                    # 单元测试
├── benchmarks/
│   └── Catga.Benchmarks/               # 基准测试
├── docs/                               # 文档
│   ├── architecture/                   # 架构文档
│   ├── distributed/                    # 分布式文档
│   ├── performance/                    # 性能文档
│   ├── patterns/                       # 设计模式
│   └── guides/                         # 使用指南
└── examples/                           # 示例代码
```

---

## 🌟 核心优势

### vs MediatR

| 特性 | Catga | MediatR |
|------|-------|---------|
| **分布式支持** | ✅ 原生 | ❌ 需自行实现 |
| **AOT 友好** | ✅ 100% | ⚠️ 部分 |
| **性能** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ |
| **集群部署** | ✅ P2P | ❌ 无 |
| **Saga 事务** | ✅ 内置 | ❌ 无 |
| **Outbox/Inbox** | ✅ 内置 | ❌ 无 |

### 为什么选择 Catga？

✅ **分布式优先** - 原生支持微服务架构
✅ **生产就绪** - 内置可靠性和可观测性
✅ **高性能** - 零反射，无锁设计
✅ **云原生** - Kubernetes 原生支持
✅ **简单易用** - 最小化配置，渐进增强

---

## 🔧 技术栈

- **.NET 9+** - 最新 .NET 平台
- **NATS** - 高性能消息总线
- **Redis** - 分布式状态存储
- **Kubernetes** - 容器编排
- **OpenTelemetry** - 可观测性标准

---

## 📈 项目状态

- ✅ **核心功能** - 稳定
- ✅ **分布式能力** - 生产就绪
- ✅ **AOT 兼容** - 100% (参见 [AOT验证报告](docs/aot/AOT_VERIFICATION_REPORT.md))
- ✅ **测试覆盖** - 良好
- ✅ **文档完整** - 详尽

---

## 🤝 贡献

欢迎贡献！请查看 [贡献指南](CONTRIBUTING.md)。

### 如何贡献

1. Fork 本仓库
2. 创建特性分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m 'feat: Add AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 提交 Pull Request

---

## 📄 许可证

本项目采用 [MIT 许可证](LICENSE)。

---

## 🎯 路线图

### v1.1 (规划中)

- [ ] ValueTask 优化
- [ ] 对象池支持
- [ ] 更多服务发现实现
- [ ] 性能监控面板

### v2.0 (未来)

- [ ] 源生成器优化
- [ ] 零分配 Pipeline
- [ ] 多语言客户端

---

## 🙏 致谢

感谢所有贡献者和使用 Catga 的开发者！

---

## 📞 联系方式

- **Issues**: [GitHub Issues](https://github.com/你的用户名/Catga/issues)
- **Discussions**: [GitHub Discussions](https://github.com/你的用户名/Catga/discussions)

---

**⭐ 如果 Catga 对你有帮助，请给个 Star！**

**Catga - 为分布式而生的 CQRS 框架！** 🚀✨
