# Catga 文档中心

<div align="center">

**完整的 CQRS 框架文档，助你快速上手并精通 Catga**

[快速开始](#快速入门) · [核心概念](#核心概念) · [功能指南](#功能指南) · [部署](#部署)

</div>

---

## 📚 文档导航

### 快速入门

| 文档 | 描述 | 预计时间 |
|------|------|---------|
| [快速开始](./QUICK-START.md) | 5 分钟构建第一个应用 | ⏱️ 5 min |
| [Quick Reference](./QUICK-REFERENCE.md) | API 速查表 | ⏱️ 2 min |
| [OrderSystem 示例](../examples/OrderSystem.Api/) | 完整的订单系统示例 | ⏱️ 15 min |

### 核心概念

| 文档 | 描述 |
|------|------|
| [消息定义](./api/messages.md) | IRequest, IEvent, INotification |
| [Handler 实现](./api/handlers.md) | SafeRequestHandler, IEventHandler |
| [错误处理](./guides/error-handling.md) | CatgaException, CatgaResult |
| [依赖注入](./guides/dependency-injection.md) | 自动注册, Source Generator |

### 功能指南

| 文档 | 描述 | 特性 |
|------|------|------|
| [自定义错误处理](./guides/custom-error-handling.md) | 虚函数重写，自动回滚 | 🆕 |
| [时间旅行调试](./DEBUGGER.md) | 完整流程回放 | ⭐ |
| [Source Generator](./SOURCE-GENERATOR.md) | 零反射，自动注册 | 🔥 |
| [分布式事务](./patterns/DISTRIBUTED-TRANSACTION-V2.md) | Catga Pattern | 💡 |
| [事件驱动](./patterns/event-driven.md) | 发布/订阅模式 | 📢 |
| [.NET Aspire 集成](./guides/debugger-aspire-integration.md) | 云原生开发 | ☁️ |

### 序列化与传输

| 文档 | 描述 | AOT |
|------|------|-----|
| [MemoryPack 序列化](./serialization/memorypack.md) | AOT 兼容，高性能 | ✅ |
| [JSON 序列化](./serialization/json.md) | 开发友好 | ⚠️ |
| [NATS 传输](./transport/nats.md) | 分布式消息传输 | ✅ |
| [Redis 持久化](./persistence/redis.md) | 事件存储 | ✅ |

### 高级主题

| 文档 | 描述 |
|------|------|
| [性能优化](./PERFORMANCE-REPORT.md) | 性能基准和优化技巧 |
| [AOT 兼容性](../src/Catga.Debugger/AOT-COMPATIBILITY.md) | Native AOT 完整指南 |
| [Benchmark 结果](./BENCHMARK-RESULTS.md) | 详细的性能测试数据 |
| [测试覆盖率](../TEST-COVERAGE-SUMMARY.md) | 测试策略和覆盖率分析 |

### 部署

| 文档 | 描述 |
|------|------|
| [生产配置](./deployment/production.md) | 生产环境最佳实践 |
| [Docker 部署](./deployment/docker.md) | 容器化部署 |
| [Kubernetes](./deployment/kubernetes.md) | K8s 部署指南 |
| [监控和告警](./deployment/monitoring.md) | OpenTelemetry 集成 |

---

## 🚀 快速开始路径

### 路径 1: 新手入门（推荐）

1. **5 分钟** - 阅读 [快速开始](./QUICK-START.md)
2. **10 分钟** - 运行 [OrderSystem 示例](../examples/OrderSystem.Api/)
3. **15 分钟** - 学习 [消息定义](./api/messages.md) 和 [Handler 实现](./api/handlers.md)
4. **开始编码** - 构建你的第一个应用！

### 路径 2: 有 MediatR 经验

1. **2 分钟** - 查看 [Quick Reference](./QUICK-REFERENCE.md)
2. **5 分钟** - 了解 [SafeRequestHandler](./api/handlers.md#saferequesthandler)
3. **10 分钟** - 学习 [Source Generator](./SOURCE-GENERATOR.md)
4. **开始迁移** - 从 MediatR 迁移到 Catga

### 路径 3: 关注性能

1. **5 分钟** - 阅读 [性能报告](./PERFORMANCE-REPORT.md)
2. **10 分钟** - 查看 [Benchmark 结果](./BENCHMARK-RESULTS.md)
3. **15 分钟** - 学习 [MemoryPack 序列化](./serialization/memorypack.md)
4. **开始优化** - 应用零分配设计模式

### 路径 4: 分布式系统

1. **10 分钟** - 学习 [NATS 传输](./transport/nats.md)
2. **10 分钟** - 学习 [Redis 持久化](./persistence/redis.md)
3. **20 分钟** - 了解 [分布式事务](./patterns/DISTRIBUTED-TRANSACTION-V2.md)
4. **开始构建** - 分布式 CQRS 应用

---

## 📖 核心概念速览

### 1. SafeRequestHandler

**零异常处理的 Handler 基类**：

```csharp
public class CreateOrderHandler : SafeRequestHandler<CreateOrder, OrderResult>
{
    // 只需编写业务逻辑，无需 try-catch！
    protected override async Task<OrderResult> HandleCoreAsync(
        CreateOrder request, 
        CancellationToken ct)
    {
        if (request.Amount <= 0)
            throw new CatgaException("Amount must be positive");  // 自动转换为失败结果
            
        // 业务逻辑
        return new OrderResult(orderId, DateTime.UtcNow);
    }
    
    // 可选：自定义错误处理和回滚
    protected override async Task<CatgaResult<OrderResult>> OnBusinessErrorAsync(...)
    {
        // 自动回滚逻辑
        await RollbackChangesAsync();
        return CatgaResult.Failure("Operation rolled back");
    }
}
```

### 2. Source Generator

**零配置，自动注册**：

```csharp
// 自动发现并注册所有 Handler
builder.Services.AddGeneratedHandlers();

// 自动发现并注册所有服务
builder.Services.AddGeneratedServices();

// 服务定义
[CatgaService(ServiceLifetime.Scoped, ServiceType = typeof(IRepository))]
public class Repository : IRepository { }
```

### 3. 事件驱动

**一个事件，多个 Handler**：

```csharp
// 定义事件
[MemoryPackable]
public partial record OrderCreated(string OrderId) : IEvent;

// 多个 Handler 自动并行执行
public class SendEmailHandler : IEventHandler<OrderCreated> { }
public class UpdateStatsHandler : IEventHandler<OrderCreated> { }
public class NotifyWarehouseHandler : IEventHandler<OrderCreated> { }

// 发布事件
await mediator.PublishAsync(new OrderCreated(orderId));
```

### 4. 消息定义

**简洁的消息契约**：

```csharp
// 命令（有返回值）
[MemoryPackable]
public partial record CreateOrder(string Id, decimal Amount) : IRequest<OrderResult>;

// 命令结果
[MemoryPackable]
public partial record OrderResult(string OrderId, DateTime CreatedAt);

// 事件（通知）
[MemoryPackable]
public partial record OrderCreated(string OrderId) : IEvent;
```

---

## 🎯 特性矩阵

| 特性 | Catga | MediatR | MassTransit |
|------|-------|---------|-------------|
| 零反射 | ✅ Source Generator | ❌ | ❌ |
| AOT 兼容 | ✅ 100% | ⚠️ 部分 | ❌ |
| 零分配 | ✅ | ⚠️ 部分 | ❌ |
| 自动注册 | ✅ Source Generator | ❌ 手动 | ✅ |
| 错误处理 | ✅ SafeRequestHandler | ❌ 手动 | ⚠️ 部分 |
| 自动回滚 | ✅ 虚函数 | ❌ | ⚠️ 部分 |
| 分布式 | ✅ NATS/Redis | ❌ | ✅ |
| 时间旅行调试 | ✅ 独创 | ❌ | ❌ |
| .NET Aspire | ✅ 原生支持 | ❌ | ⚠️ 部分 |

---

## 💡 常见问题

### Catga vs MediatR？

**Catga** 是为 .NET 9 和 Native AOT 设计的，提供：
- ✅ **更好的性能** - 零反射，零分配
- ✅ **更少的代码** - SafeRequestHandler，自动注册
- ✅ **更强的功能** - 自动回滚，时间旅行调试
- ✅ **AOT 优先** - 100% AOT 兼容

**MediatR** 是经典的中介者模式实现，适合不需要 AOT 的场景。

### 什么时候选择 Catga？

选择 Catga 如果你：
- ✅ 使用 .NET 9
- ✅ 关注性能（微服务、高并发）
- ✅ 需要 Native AOT
- ✅ 构建分布式系统
- ✅ 需要时间旅行调试

### Catga 生产就绪了吗？

**是的！** Catga 包含：
- ✅ 194 个单元测试（100% 通过）
- ✅ 完整的性能基准
- ✅ 生产级错误处理
- ✅ 优雅关闭和恢复
- ✅ OpenTelemetry 集成
- ✅ 完整的文档

---

## 🔗 快速链接

### 开始使用
- [快速开始](./QUICK-START.md)
- [OrderSystem 示例](../examples/OrderSystem.Api/)
- [API 速查](./QUICK-REFERENCE.md)

### 核心文档
- [SafeRequestHandler](./api/handlers.md#saferequesthandler)
- [Source Generator](./SOURCE-GENERATOR.md)
- [错误处理](./guides/error-handling.md)

### 高级特性
- [时间旅行调试](./DEBUGGER.md)
- [自定义错误处理](./guides/custom-error-handling.md)
- [分布式事务](./patterns/DISTRIBUTED-TRANSACTION-V2.md)

### 性能
- [性能报告](./PERFORMANCE-REPORT.md)
- [Benchmark 结果](./BENCHMARK-RESULTS.md)

---

## 📞 获取帮助

- 🐛 **Bug 报告**: [GitHub Issues](https://github.com/catga/catga/issues)
- 💬 **问题讨论**: [GitHub Discussions](https://github.com/catga/catga/discussions)
- 📖 **文档问题**: 直接提交 PR
- ⭐ **给我们 Star**: [GitHub](https://github.com/catga/catga)

---

<div align="center">

**开始你的 Catga 之旅！**

[快速开始](./QUICK-START.md) · [查看示例](../examples/OrderSystem.Api/) · [阅读文档](./api/messages.md)

</div>
