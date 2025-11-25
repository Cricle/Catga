<div align="center">

<img src="./web/favicon.svg" width="100" height="100" alt="Catga Logo"/>

# Catga 文档

**现代化、高性能的 .NET CQRS/Event Sourcing 框架**

[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Native AOT](https://img.shields.io/badge/Native-AOT-success?logo=dotnet)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](../LICENSE)

**纳秒级延迟 · 百万QPS · 零反射 · 源生成 · 生产就绪**

[📚 完整文档索引](./INDEX.md) · [🚀 快速开始](./articles/getting-started.md) · [📊 性能基准](./BENCHMARK-RESULTS.md) · [💻 示例](../examples/)

</div>

---

## 📖 文档导航

### 🎯 快速开始 (5 分钟)

从这里开始学习 Catga！

1. [📝 快速开始指南](./articles/getting-started.md)
   5 分钟上手，从零开始构建第一个 CQRS 应用

2. [💡 基础示例](./examples/basic-usage.md)
   命令、查询、事件的基础用法

3. [🧠 CQRS 概念](./architecture/cqrs.md)
   理解命令查询职责分离模式

---

### 🏗️ 核心概念

深入理解 Catga 架构

| 文档 | 说明 |
|------|------|
| [架构总览](./architecture/ARCHITECTURE.md) | 完整的架构设计和职责划分 |
| [CQRS 模式](./architecture/cqrs.md) | 命令查询职责分离详解 |
| [职责边界](./architecture/RESPONSIBILITY-BOUNDARY.md) | 各层职责明确划分 |
| [系统概览](./architecture/overview.md) | 整体系统设计 |

**架构图**:

```
┌──────────────────────────────────────┐
│        应用层 (Application)           │
│  Controllers / Handlers / Services   │
└──────────────┬───────────────────────┘
               │
    ┌──────────▼──────────┐
    │   Catga Mediator    │
    │  · 消息路由         │
    │  · Pipeline 执行    │
    │  · 错误处理         │
    └──────────┬──────────┘
               │
    ┌──────────┴──────────┐
    │                     │
┌───▼─────┐        ┌─────▼────┐
│ Command │        │  Event   │
│ Query   │        │(多Handler)│
└───┬─────┘        └─────┬────┘
    │                     │
    ▼                     ▼
 业务逻辑              事件处理
```

---

### 📚 开发指南

从配置到部署的完整指南

#### 基础配置

| 文档 | 说明 |
|------|------|
| [配置指南](./articles/configuration.md) | 框架配置详解 |
| [依赖注入](./guides/auto-di-registration.md) | Handler 自动注册 |
| [错误处理](./guides/error-handling.md) | 异常处理和回滚 |
| [自定义错误处理](./guides/custom-error-handling.md) | 自定义错误策略 |
| [Resilience (Polly)](./Resilience.md) | 弹性策略（重试/超时/断路/舱壁） |

#### 高级功能

| 文档 | 说明 |
|------|------|
| [源生成器](./guides/source-generator.md) | 编译时代码生成 |
| [源生成器使用](./guides/source-generator-usage.md) | 详细使用指南 |
| [序列化](./guides/serialization.md) | JSON / MemoryPack |
| [分布式 ID](./guides/distributed-id.md) | Snowflake ID 生成 |
| [内存优化](./guides/memory-optimization-guide.md) | 零分配优化 |

---

### 📊 性能优化

极致性能的秘密

| 文档 | 说明 | 关键指标 |
|------|------|---------|
| [**性能基准测试**](./BENCHMARK-RESULTS.md) | BenchmarkDotNet 测试报告 | **462 ns, 2.2M QPS** |
| [性能报告](./PERFORMANCE-REPORT.md) | 详细性能分析 | P99 < 1μs |
| [GC 和热路径优化](./development/GC_AND_HOTPATH_REVIEW.md) | TagList 栈分配, Span 优化 | 零分配设计 |
| [线程池管理](./development/THREAD_POOL_MANAGEMENT_PLAN.md) | 并发限制, 熔断器 | 防止耗尽 |
| [内存优化指南](./guides/memory-optimization-guide.md) | 实战技巧 | < 600B/op |

#### 性能亮点

```
📊 核心 CQRS 性能
├── 命令处理: 462 ns (432 B)    → 2.2M ops/s ⚡
├── 查询处理: 446 ns (368 B)    → 2.2M ops/s ⚡
├── 事件发布: 438 ns (432 B)    → 2.3M ops/s ⚡
└── 批量处理: 45.1 μs (100 ops) → 2.2M ops/s ⚡

🚀 业务场景性能
├── 创建订单: 544 ns (440 B)
├── 支付处理: 626 ns (568 B)
├── 订单查询: 509 ns (416 B)
└── 完整流程: 1.63 μs (1.4 KB)

🔥 并发性能
├── 10 并发:  5.3 μs (3.5 KB)  → 1.9M ops/s
├── 100 并发: 54.2 μs (34.4 KB) → 1.8M ops/s
└── 1000 并发: 519 μs (343.8 KB) → 1.9M ops/s
```

---

### 🌐 分布式和微服务

构建可靠的分布式系统

| 文档 | 说明 |
|------|------|
| [分布式事务](./patterns/DISTRIBUTED-TRANSACTION-V2.md) | Outbox/Inbox 模式 |
| [事件溯源](./architecture/ARCHITECTURE.md#event-sourcing) | Event Sourcing 实现 |
| [分布式追踪](./observability/DISTRIBUTED-TRACING-GUIDE.md) | OpenTelemetry 集成 |
| [Jaeger 完整指南](./observability/JAEGER-COMPLETE-GUIDE.md) | 分布式追踪可视化 |
| [监控指标](./production/MONITORING-GUIDE.md) | Prometheus + Grafana |

#### 可靠性保障

```
✅ Outbox/Inbox 模式
├── 保证消息至少一次送达
├── 自动重试和补偿
└── 幂等性处理

✅ 熔断器和限流
├── 防止级联故障
├── 并发控制
└── 降级策略

✅ 监控和追踪
├── OpenTelemetry 集成
├── Grafana Dashboard
└── Jaeger Tracing
```

---

### 🚀 部署和运维

从开发到生产的完整流程

| 文档 | 说明 |
|------|------|
| [Kubernetes 部署](./deployment/kubernetes.md) | K8s 部署配置 |
| [Native AOT 发布](./deployment/native-aot-publishing.md) | AOT 编译和发布 |
| [AOT 部署指南](./articles/aot-deployment.md) | 完整 AOT 部署 |
| [序列化 AOT 指南](./aot/serialization-aot-guide.md) | 序列化 AOT 支持 |
| [OpenTelemetry 集成](./articles/opentelemetry-integration.md) | 可观测性集成 |

#### 部署选项

```
🐳 容器化部署
├── Docker 镜像
├── Kubernetes Deployment
└── Helm Charts

🚀 Native AOT
├── 极快启动时间 (< 50ms)
├── 极低内存占用 (< 20MB)
└── 无需 JIT 编译

☁️ 云原生
├── .NET Aspire 支持
├── Azure Container Apps
└── AWS ECS / EKS
```

---

### 🧪 测试

编写高质量测试

| 文档 | 说明 |
|------|------|
| [Catga.Testing 使用](../src/Catga.Testing/README.md) | 测试辅助库完整指南 |
| [测试库总结](./development/TESTING_LIBRARY_SUMMARY.md) | 功能和最佳实践 |

#### 测试示例

```csharp
using Catga.Testing;

public class OrderTests : IDisposable
{
    private readonly CatgaTestFixture _fixture;

    public OrderTests()
    {
        _fixture = new CatgaTestFixture();
        _fixture.RegisterRequestHandler<CreateOrderCommand, Order, CreateOrderHandler>();
    }

    [Fact]
    public async Task CreateOrder_ShouldSucceed()
    {
        var result = await _fixture.Mediator.SendAsync(
            new CreateOrderCommand("PROD-001", 5)
        );

        result.Should().BeSuccessful();
        result.Should().HaveValueSatisfying(order =>
        {
            order.ProductId.Should().Be("PROD-001");
            order.Quantity.Should().Be(5);
        });
    }

    public void Dispose() => _fixture.Dispose();
}
```

---

### 💻 示例项目

学习最佳实践

| 示例 | 说明 | 特性 |
|------|------|------|
| [OrderSystem.Api](../examples/OrderSystem.Api/) | 电商订单系统 | 完整业务流程、分布式部署 |
| [OrderSystem.AppHost](../examples/OrderSystem.AppHost/) | .NET Aspire 编排 | 云原生开发 |

**运行示例**:

```bash
# 单节点运行
cd examples/OrderSystem.Api
dotnet run

# 集群运行 (3 节点)
cd examples/OrderSystem.Api
.\start-cluster.ps1

# .NET Aspire 运行
cd examples/OrderSystem.AppHost
dotnet run
```

---

### 🔧 开发者资源

贡献和深入学习

| 文档 | 说明 |
|------|------|
| [贡献指南](./development/CONTRIBUTING.md) | 如何贡献代码 |
| [开发文档](./development/README.md) | 开发环境搭建 |
| [AI 学习指南](./development/AI-LEARNING-GUIDE.md) | 框架学习路径 |
| [ValueTask 使用指南](./development/VALUETASK_VS_TASK_GUIDELINES.md) | ValueTask vs Task |
| [WorkerId 增强](./development/WORKERID_ENHANCEMENT.md) | 分布式 ID 配置 |

---

### 📋 API 参考

| 文档 | 说明 |
|------|------|
| [Mediator API](./api/mediator.md) | ICatgaMediator 接口 |
| [消息 API](./api/messages.md) | IRequest, IEvent, IMessage |
| [API 总览](./api/README.md) | 完整 API 文档 |

---

### 🔍 工具和诊断

| 文档 | 说明 |
|------|------|
| [分析器介绍](./analyzers/README.md) | Roslyn 分析器 |
| [分析器使用](./guides/analyzers.md) | 诊断规则 |
| [Grafana Dashboard](./development/GRAFANA_UPDATE_SUMMARY.md) | 监控仪表板 |

#### 诊断规则

- `CAT1001`: Handler 必须是 public
- `CAT1002`: Handler 必须有无参构造函数
- `CAT2001`: Request 必须只有一个 Handler
- `CAT2002`: Event 可以有多个 Handler
- `CAT2003`: 检测到重复的 Request Handler

---

## 📞 获取帮助

### 常见问题

<details>
<summary>❓ 如何开始学习 Catga？</summary>

1. 阅读 [快速开始指南](./articles/getting-started.md)
2. 运行 [OrderSystem 示例](../examples/OrderSystem.Api/)
3. 查看 [CQRS 概念](./architecture/cqrs.md)

</details>

<details>
<summary>❓ 性能如何优化？</summary>

1. 查看 [性能基准测试](./BENCHMARK-RESULTS.md)
2. 阅读 [GC 和热路径优化](./development/GC_AND_HOTPATH_REVIEW.md)
3. 应用 [内存优化指南](./guides/memory-optimization-guide.md)

</details>

<details>
<summary>❓ 如何部署到生产？</summary>

1. 选择部署方式: [Kubernetes](./deployment/kubernetes.md) 或 [Native AOT](./deployment/native-aot-publishing.md)
2. 配置 [监控和追踪](./production/MONITORING-GUIDE.md)
3. 参考 [OrderSystem 集群部署](../examples/OrderSystem.Api/README.md)

</details>

<details>
<summary>❓ 如何编写测试？</summary>

1. 安装 `Catga.Testing` 包
2. 阅读 [测试辅助库文档](../src/Catga.Testing/README.md)
3. 参考示例项目的测试代码

</details>

### 获取支持

- 💬 [GitHub 讨论区](https://github.com/Cricle/Catga/discussions) - 提问和交流
- 🐛 [问题追踪](https://github.com/Cricle/Catga/issues) - 报告 Bug
- 📧 Email - support@catga.dev (开发中)
- ⭐ [GitHub](https://github.com/Cricle/Catga) - 给个 Star 支持我们！

---

## 🎓 学习路径

### 新手 (1-2 天)

```
Day 1: 基础概念
├── 📝 快速开始 (30 min)
├── 🧠 CQRS 概念 (1 hour)
├── 💻 运行示例 (30 min)
└── 🔧 配置指南 (1 hour)

Day 2: 实战开发
├── 📚 Handler 开发 (2 hours)
├── 🚨 错误处理 (1 hour)
└── 🧪 编写测试 (1 hour)
```

### 进阶 (3-5 天)

```
Day 3: 高级特性
├── 🌐 分布式事务 (2 hours)
├── 📊 性能优化 (2 hours)
└── 🔍 分布式追踪 (2 hours)

Day 4-5: 生产部署
├── 🐳 容器化 (2 hours)
├── ☸️ Kubernetes (3 hours)
├── 🚀 Native AOT (2 hours)
└── 📈 监控运维 (2 hours)
```

### 专家 (持续)

```
深入源码
├── 🏗️ 架构设计
├── ⚡ 性能优化
├── 🧪 单元测试
└── 🤝 贡献代码
```

---

## 🗺️ 文档地图

```
docs/
├── 📖 INDEX.md                    # 完整文档索引
├── 📝 README.md                   # 文档主页 (本页)
├── 📊 BENCHMARK-RESULTS.md        # 性能基准
├── 📈 PERFORMANCE-REPORT.md       # 性能报告
├── 📰 CHANGELOG.md                # 更新日志
│
├── 📚 articles/                   # 文章
│   ├── getting-started.md        # ⭐ 快速开始
│   ├── configuration.md          # 配置指南
│   ├── architecture.md           # 架构介绍
│   ├── aot-deployment.md         # AOT 部署
│   └── opentelemetry-integration.md
│
├── 🏗️ architecture/              # 架构设计
│   ├── ARCHITECTURE.md           # ⭐ 完整架构
│   ├── cqrs.md                   # CQRS 模式
│   ├── overview.md               # 系统概览
│   └── RESPONSIBILITY-BOUNDARY.md
│
├── 📖 guides/                    # 开发指南
│   ├── auto-di-registration.md  # 自动注册
│   ├── error-handling.md        # 错误处理
│   ├── source-generator.md      # 源生成器
│   ├── serialization.md         # 序列化
│   ├── distributed-id.md        # 分布式 ID
│   └── memory-optimization-guide.md
│
├── 🌐 patterns/                  # 设计模式
│   └── DISTRIBUTED-TRANSACTION-V2.md
│
├── 🔍 observability/             # 可观测性
│   ├── DISTRIBUTED-TRACING-GUIDE.md
│   └── JAEGER-COMPLETE-GUIDE.md
│
├── 🚀 deployment/                # 部署
│   ├── kubernetes.md
│   └── native-aot-publishing.md
│
├── 📊 production/                # 生产运维
│   └── MONITORING-GUIDE.md
│
├── 🧪 examples/                  # 示例
│   └── basic-usage.md
│
├── 🔧 development/               # 开发者
│   ├── CONTRIBUTING.md
│   ├── README.md
│   ├── AI-LEARNING-GUIDE.md
│   ├── GC_AND_HOTPATH_REVIEW.md
│   ├── THREAD_POOL_MANAGEMENT_PLAN.md
│   └── TESTING_LIBRARY_SUMMARY.md
│
└── 📋 api/                       # API 参考
    ├── README.md
    ├── mediator.md
    └── messages.md
```

---

<div align="center">

## 🌟 开始你的 Catga 之旅

[📝 快速开始](./articles/getting-started.md) · [📚 完整文档](./INDEX.md) · [💻 查看示例](../examples/) · [⭐ GitHub](https://github.com/Cricle/Catga)

**如果觉得有用，请给个 ⭐ Star！**

Made with ❤️ by the Catga Team

</div>
