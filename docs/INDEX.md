# 📚 Catga 文档中心

<div align="center">

**现代化、高性能的 .NET CQRS/Event Sourcing 框架**

**纳秒级延迟 · 百万QPS · 零反射 · 源生成 · 生产就绪**

[GitHub](https://github.com/Cricle/Catga) · [快速开始](./articles/getting-started.md) · [API 文档](./api/README.md) · [示例](../examples/)

</div>

---

## 🚀 快速导航

### 新手入门

| 文档 | 说明 | 预计时间 |
|------|------|---------|
| [快速开始](./articles/getting-started.md) | 5 分钟上手 Catga | ⏱️ 5 min |
| [基础示例](./examples/basic-usage.md) | 命令、查询、事件示例 | ⏱️ 10 min |
| [CQRS 概念](./architecture/cqrs.md) | 理解 CQRS 模式 | ⏱️ 15 min |
| [架构概览](./architecture/overview.md) | 框架架构设计 | ⏱️ 20 min |

### 核心功能

| 文档 | 说明 |
|------|------|
| [Mediator API](./api/mediator.md) | 消息调解器使用 |
| [消息定义](./api/messages.md) | 命令、查询、事件定义 |
| [Handler 开发](./guides/auto-di-registration.md) | Handler 自动注册 |
| [源生成器](./guides/source-generator.md) | 编译时代码生成 |
| [错误处理](./guides/error-handling.md) | 自动错误处理和回滚 |
| [配置选项](./articles/configuration.md) | 框架配置指南 |
| [Resilience (Polly)](./Resilience.md) | 弹性策略（重试/超时/断路/舱壁） |

### 高级特性

| 文档 | 说明 |
|------|------|
| [分布式事务](./patterns/DISTRIBUTED-TRANSACTION-V2.md) | Outbox/Inbox 模式 |
| [事件溯源](./architecture/ARCHITECTURE.md#event-sourcing) | Event Sourcing 实现 |
| [分布式追踪](./observability/DISTRIBUTED-TRACING-GUIDE.md) | OpenTelemetry 集成 |
| [Jaeger 完整指南](./observability/JAEGER-COMPLETE-GUIDE.md) | 分布式追踪可视化 |
| [监控指标](./production/MONITORING-GUIDE.md) | Prometheus + Grafana |
| [分布式 ID](./guides/distributed-id.md) | Snowflake ID 生成 |

### 序列化

| 文档 | 说明 | 性能 |
|------|------|------|
| [JSON 序列化](./guides/serialization.md) | System.Text.Json | 兼容性好 |
| [MemoryPack](./guides/serialization.md#memorypack) | 二进制序列化 | ⚡ 最快 |
| [AOT 序列化指南](./aot/serialization-aot-guide.md) | Native AOT 支持 | 💡 重要 |

### 部署运维

| 文档 | 说明 |
|------|------|
| [Kubernetes 部署](./deployment/kubernetes.md) | K8s 部署配置 |
| [Native AOT 发布](./deployment/native-aot-publishing.md) | AOT 编译和发布 |
| [AOT 部署指南](./articles/aot-deployment.md) | 完整 AOT 部署 |
| [.NET Aspire 集成](./articles/opentelemetry-integration.md) | 云原生开发 |

---

## 📊 性能优化

### 性能文档

| 文档 | 说明 |
|------|------|
| [性能基准测试](./BENCHMARK-RESULTS.md) | ⚡ 纳秒级延迟 (400-600ns), 2M+ QPS 吞吐量 |
| [性能报告](./PERFORMANCE-REPORT.md) | 详细性能分析 |
| [GC 和热路径优化](./development/GC_AND_HOTPATH_REVIEW.md) | TagList 栈分配, Span 优化 |
| [线程池管理](./development/THREAD_POOL_MANAGEMENT_PLAN.md) | 并发限制, 熔断器, 批处理 |
| [内存优化指南](./guides/memory-optimization-guide.md) | 零分配优化实战 |

### 关键指标

```
📊 核心 CQRS 性能
├── 命令处理: 462 ns (432 B)    → 2.2M ops/s
├── 查询处理: 446 ns (368 B)    → 2.2M ops/s
├── 事件发布: 438 ns (432 B)    → 2.3M ops/s
└── 批量处理: 45.1 μs (100 ops) → 2.2M ops/s

🚀 业务场景
├── 创建订单: 544 ns
├── 支付处理: 626 ns
├── 订单查询: 509 ns
└── 完整流程: 1.63 μs

🔥 并发性能
├── 10 并发:  5.3 μs  → 1.9M ops/s
├── 100 并发: 54.2 μs → 1.8M ops/s
└── 1000 并发: 519 μs → 1.9M ops/s
```

---

## 🏗️ 架构设计

### 架构文档

| 文档 | 说明 |
|------|------|
| [架构总览](./architecture/ARCHITECTURE.md) | 完整架构设计 |
| [CQRS 模式](./architecture/cqrs.md) | 命令查询职责分离 |
| [职责边界](./architecture/RESPONSIBILITY-BOUNDARY.md) | 各层职责划分 |
| [系统概览](./architecture/overview.md) | 系统设计概览 |

### 架构图

```
┌─────────────────────────────────────────────┐
│           应用层 (Application)               │
│  Controllers / Handlers / Services          │
└──────────────────┬──────────────────────────┘
                   │
    ┌──────────────▼──────────────┐
    │    Catga Mediator (核心)     │
    │  · 消息路由                  │
    │  · Pipeline 执行             │
    │  · 错误处理                  │
    └──────────────┬──────────────┘
                   │
    ┌──────────────┴──────────────┐
    │                             │
┌───▼────────┐          ┌────────▼────────┐
│ Command    │          │     Event       │
│ Query      │          │   (多Handler)    │
└───┬────────┘          └────────┬────────┘
    │                             │
    ▼                             ▼
业务逻辑                      事件处理
```

---

## 🧪 测试指南

### 测试文档

| 文档 | 说明 |
|------|------|
| [Catga.Testing 使用](../src/Catga.Testing/README.md) | 测试辅助库 |
| [测试库总结](./development/TESTING_LIBRARY_SUMMARY.md) | 完整功能介绍 |

### 测试示例

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

## 🔧 开发者指南

### 开发文档

| 文档 | 说明 |
|------|------|
| [贡献指南](./development/CONTRIBUTING.md) | 如何贡献代码 |
| [开发文档](./development/README.md) | 开发环境搭建 |
| [AI 学习指南](./development/AI-LEARNING-GUIDE.md) | 框架学习路径 |
| [ValueTask 使用指南](./development/VALUETASK_VS_TASK_GUIDELINES.md) | ValueTask vs Task |
| [ValueTask 审计报告](./development/VALUETASK_TASK_AUDIT_REPORT.md) | 代码审计 |
| [WorkerId 增强](./development/WORKERID_ENHANCEMENT.md) | 分布式 ID 配置 |

### 技术决策

| 文档 | 说明 |
|------|------|
| [零反射设计](./guides/source-generator.md) | 为什么使用源生成器 |
| [可插拔架构](./architecture/overview.md) | 模块化设计 |
| [AOT 优化](./aot/serialization-aot-guide.md) | Native AOT 支持 |
| [内存优化](./guides/memory-optimization-guide.md) | 零分配设计 |

---

## 📖 示例项目

### 完整示例

| 示例 | 说明 | 特性 |
|------|------|------|
| [OrderSystem.Api](../examples/OrderSystem.Api/) | 电商订单系统 | 完整业务流程、分布式部署 |
| [OrderSystem.AppHost](../examples/OrderSystem.AppHost/) | .NET Aspire 编排 | 云原生开发 |

### 示例代码

```csharp
// 1️⃣ 定义消息
public record CreateOrderCommand(string ProductId, int Quantity) : IRequest<Order>;
public record OrderCreatedEvent(string OrderId) : IEvent;

// 2️⃣ 定义 Handler
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, Order>
{
    public async Task<CatgaResult<Order>> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        var order = new Order
        {
            Id = Guid.NewGuid().ToString(),
            ProductId = request.ProductId,
            Quantity = request.Quantity
        };

        return CatgaResult<Order>.Success(order);
    }
}

// 3️⃣ 配置和使用
builder.Services.AddCatga();

var result = await mediator.SendAsync(new CreateOrderCommand("PROD-001", 5));
```

---

## 🔍 分析器和工具

### Roslyn 分析器

| 分析器 | 说明 |
|--------|------|
| [分析器介绍](./analyzers/README.md) | Catga 分析器 |
| [分析器使用](./guides/analyzers.md) | 使用指南 |

### 诊断规则

- `CAT1001`: Handler 必须是 public
- `CAT1002`: Handler 必须有无参构造函数
- `CAT2001`: Request 必须只有一个 Handler
- `CAT2002`: Event 可以有多个 Handler
- `CAT2003`: 检测到重复的 Request Handler

---

## 🎨 品牌资源

### Logo 和视觉

| 资源 | 说明 |
|------|------|
| [Logo 使用指南](./branding/logo-guide.md) | Logo 使用规范 |
| [Favicon](./web/favicon.svg) | 网站图标 (SVG) |

---

## 📋 其他资源

### 文档和报告

| 文档 | 说明 |
|------|------|
| [更新日志](./CHANGELOG.md) | 版本更新记录 |
| [Grafana 仪表板](./development/GRAFANA_UPDATE_SUMMARY.md) | 监控仪表板配置 |
| [遥测优化](./development/TELEMETRY_OPTIMIZATION_SUMMARY.md) | 指标优化总结 |
| [单元测试修复](./development/UT_FIX_SUMMARY.md) | 测试修复记录 |
| [包管理优化](./development/DIRECTORY_PROPS_SUMMARY.md) | 中央包管理 |

### 脚本工具

| 工具 | 说明 |
|------|------|
| [Benchmark 脚本](../scripts/README.md) | 性能测试脚本 |
| [反射验证](../scripts/VerifyReflectionOptimization.ps1) | 验证零反射 |

---

## 🔗 外部链接

- 📦 [NuGet 包](https://www.nuget.org/packages/Catga/)
- 💬 [GitHub 讨论](https://github.com/Cricle/Catga/discussions)
- 🐛 [问题追踪](https://github.com/Cricle/Catga/issues)
- 📰 [发布说明](https://github.com/Cricle/Catga/releases)

---

## 📞 获取帮助

### 常见问题

1. ❓ **如何开始？**
   → 查看 [快速开始指南](./articles/getting-started.md)

2. ❓ **性能如何优化？**
   → 查看 [性能优化指南](./guides/memory-optimization-guide.md)

3. ❓ **如何部署到生产？**
   → 查看 [Kubernetes 部署](./deployment/kubernetes.md)

4. ❓ **如何编写测试？**
   → 查看 [测试辅助库](../src/Catga.Testing/README.md)

5. ❓ **如何贡献代码？**
   → 查看 [贡献指南](./development/CONTRIBUTING.md)

### 获取支持

- 💬 [讨论区](https://github.com/Cricle/Catga/discussions) - 提问和交流
- 🐛 [问题追踪](https://github.com/Cricle/Catga/issues) - 报告 Bug
- 📧 Email - support@catga.dev (开发中)

---

<div align="center">

**如果觉得有用，请给个 ⭐ Star！**

Made with ❤️ by the Catga Team

[返回顶部](#-catga-文档中心)

</div>
