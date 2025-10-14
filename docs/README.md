# 📚 Catga 完整文档

> **Catga 文档中心** - 从入门到精通，构建高性能分布式 CQRS 系统

[返回主页](../README.md) · [快速参考](../QUICK-REFERENCE.md) · [示例项目](../examples/)

---

## 🎯 新手路径（3 步上手）

### 第 1 步：快速开始（5 分钟）
- **[30 秒快速开始](../README.md#-30-秒快速开始)** - 立即开始使用
- **[5 分钟快速参考](../QUICK-REFERENCE.md)** - 常用 API 速查

### 第 2 步：选择序列化器（2 分钟）
- **[序列化指南](guides/serialization.md)** - MemoryPack vs JSON 决策

### 第 3 步：部署到生产（10 分钟）
- **[Native AOT 发布](deployment/native-aot-publishing.md)** - AOT 部署指南
- **[Kubernetes 部署](deployment/kubernetes.md)** - K8s 最佳实践

**🎉 完成！开始构建您的第一个应用**

---

## 🎓 进阶路径（5 步精通）

### 第 1 步：理解架构
- **[架构概览](architecture/ARCHITECTURE.md)** - 系统设计和核心概念
- **[职责边界](architecture/RESPONSIBILITY-BOUNDARY.md)** - Catga vs NATS/Redis/K8s
- **[CQRS 模式](architecture/cqrs.md)** - 命令查询职责分离详解

### 第 2 步：使用分析器
- **[Roslyn 分析器](guides/analyzers.md)** - 编译时检查和自动修复
  - CATGA001: 缺少 [MemoryPackable]
  - CATGA002: 缺少序列化器注册
  - 15+ 规则，9 个自动修复

### 第 3 步：性能优化
- **[反射优化](../REFLECTION_OPTIMIZATION_SUMMARY.md)** - 90x 性能提升之旅
- **[基准测试](../benchmarks/Catga.Benchmarks/)** - 详细的性能数据

### 第 4 步：分布式部署
- **[分布式架构](distributed/README.md)** - NATS/Redis 集成
- **[Kubernetes 集成](distributed/KUBERNETES.md)** - K8s 服务发现

### 第 5 步：可观测性
- **[OpenTelemetry 集成](guides/observability.md)** - Metrics/Tracing/Logging
- **[监控指标](guides/observability.md#内置指标)** - 关键性能指标

**🏆 恭喜！您已经掌握 Catga**

---

## 📖 核心文档

### 🏗️ 架构与设计

#### [架构概览](architecture/ARCHITECTURE.md)
完整的系统架构设计，包括：
- 层次结构和组件划分
- 核心抽象和接口设计
- 扩展点和集成方式
- 性能优化策略

#### [CQRS 模式](architecture/cqrs.md)
命令查询职责分离模式详解：
- Command vs Query vs Event
- Handler 设计模式
- Pipeline 管道机制
- 最佳实践和反模式

#### [职责边界](architecture/RESPONSIBILITY-BOUNDARY.md)
清晰的职责划分：
- **Catga 负责**：CQRS 分发、Pipeline、幂等性
- **NATS/Redis 负责**：消息传输、持久化
- **K8s/Aspire 负责**：服务发现、负载均衡

---

### 🛠️ 使用指南

#### [序列化指南](guides/serialization.md) 🆕
一站式序列化配置：
- **MemoryPack** - 100% AOT，5x 性能，推荐
- **JSON** - 人类可读，需配置 AOT
- 性能对比和决策树
- 完整配置示例

#### [Roslyn 分析器](guides/analyzers.md) 🆕
编译时代码检查：
- 15+ 静态分析规则
- 9 个自动代码修复
- AOT 兼容性检查
- 性能最佳实践

#### [源生成器](guides/source-generator-usage.md)
自动 Handler 注册：
- 零反射设计
- 编译时发现 Handler
- 自动生成注册代码
- 100% AOT 兼容

#### [分布式 ID](guides/distributed-id.md)
Snowflake ID 生成器：
- 高性能、线程安全
- 零分配、无锁设计
- 分布式唯一 ID

---

### 🌐 分布式

#### [分布式架构](distributed/README.md)
分布式系统设计：
- NATS JetStream 集成
- Redis Streams 集成
- QoS 保证（AtMostOnce/AtLeastOnce/ExactlyOnce）
- Outbox/Inbox 模式

#### [Kubernetes 集成](distributed/KUBERNETES.md) 🆕
K8s 部署最佳实践：
- Service Discovery
- Health Checks
- HorizontalPodAutoscaler
- ConfigMap 配置

---

### 🚀 部署

#### [Native AOT 发布](deployment/native-aot-publishing.md)
AOT 编译和部署：
- 项目配置
- 发布命令
- 性能验证
- 常见问题

#### [Kubernetes 部署](deployment/kubernetes.md) 🆕
K8s 生产部署：
- Deployment/Service 配置
- 健康检查配置
- 自动扩缩容
- 最佳实践

---

### 📊 API 参考

#### [Mediator API](api/mediator.md)
核心 Mediator 接口：
- `SendAsync` - 发送 Command/Query
- `PublishAsync` - 发布 Event
- `CatgaResult<T>` - 结果包装

#### [消息定义](api/messages.md)
消息类型和接口：
- `IRequest<TResponse>` - Command/Query
- `IEvent` - Event
- `IMessage` - 消息元数据

#### [API 总览](api/README.md)
完整 API 文档索引

---

### 💡 示例和模式

#### [基础示例](examples/basic-usage.md)
从零开始教程：
- 创建第一个 Command
- 实现 Handler
- 配置和使用
- 单元测试

#### [OrderSystem](../examples/OrderSystem.AppHost/README.md)
完整的电商订单系统：
- CQRS 模式
- Event Sourcing
- 分布式追踪
- .NET Aspire 编排

#### [MemoryPackAotDemo](../examples/MemoryPackAotDemo/README.md) 🆕
100% AOT 示例：
- MemoryPack 序列化
- Native AOT 发布
- 性能验证

---

## 🔗 快速链接

### 按场景查找

#### 我是新手
1. [30 秒快速开始](../README.md#-30-秒快速开始)
2. [5 分钟快速参考](../QUICK-REFERENCE.md)
3. [基础示例](examples/basic-usage.md)

#### 我要使用 AOT
1. [序列化指南 - MemoryPack](guides/serialization.md#memorypack-推荐---100-aot)
2. [Native AOT 发布](deployment/native-aot-publishing.md)
3. [MemoryPackAotDemo](../examples/MemoryPackAotDemo/)

#### 我要优化性能
1. [反射优化总结](../REFLECTION_OPTIMIZATION_SUMMARY.md)
2. [基准测试报告](../benchmarks/Catga.Benchmarks/)
3. [性能调优技巧](guides/performance.md)

#### 我要构建分布式系统
1. [分布式架构](distributed/README.md)
2. [Kubernetes 部署](deployment/kubernetes.md)
3. [可观测性](guides/observability.md)

#### 我要从其他框架迁移
1. [Catga vs MassTransit](CATGA_VS_MASSTRANSIT.md)
2. [API 参考](api/README.md)
3. [架构对比](architecture/ARCHITECTURE.md)

#### 我要使用分析器
1. [分析器指南](guides/analyzers.md)
2. [源生成器](guides/source-generator-usage.md)
3. [AOT 最佳实践](deployment/native-aot-publishing.md)

---

## 📂 文档结构

```
docs/
├── README.md                       # 📍 你在这里
│
├── 🚀 快速开始
│   ├── examples/
│   │   └── basic-usage.md          # 基础教程
│   └── guides/
│       └── serialization.md        # 序列化指南
│
├── 🏗️ 架构
│   └── architecture/
│       ├── ARCHITECTURE.md         # 架构概览
│       ├── cqrs.md                 # CQRS 模式
│       ├── overview.md             # 系统概述
│       └── RESPONSIBILITY-BOUNDARY.md  # 职责边界
│
├── 🛠️ 工具链
│   └── guides/
│       ├── analyzers.md            # Roslyn 分析器
│       ├── source-generator-usage.md   # 源生成器
│       ├── distributed-id.md       # 分布式 ID
│       └── observability.md        # 可观测性
│
├── 🌐 分布式
│   └── distributed/
│       ├── README.md               # 分布式概览
│       ├── ARCHITECTURE.md         # 分布式架构
│       └── KUBERNETES.md           # K8s 集成
│
├── 🚀 部署
│   └── deployment/
│       ├── native-aot-publishing.md    # AOT 发布
│       └── kubernetes.md           # K8s 部署
│
├── 📊 API 参考
│   └── api/
│       ├── README.md               # API 总览
│       ├── mediator.md             # Mediator API
│       └── messages.md             # 消息接口
│
└── 📝 其他
    ├── ASPNETCORE_INTEGRATION_SUMMARY.md   # ASP.NET Core 集成
    ├── CATGA_VS_MASSTRANSIT.md            # 框架对比
    ├── CODE_SIMPLIFICATION_SUMMARY.md     # 代码简化总结
    ├── PROJECT_STRUCTURE.md               # 项目结构
    ├── QUICK_START_RPC.md                 # RPC 快速开始
    └── RPC_IMPLEMENTATION.md              # RPC 实现细节
```

---

## 🆕 最近更新

### 2025-10-14
- ✅ 重写 README.md - 30 秒快速开始
- ✅ 重写 QUICK-REFERENCE.md - 真正的 5 分钟参考
- ✅ 新增序列化指南 - MemoryPack vs JSON
- ✅ 新增 K8s 部署文档
- ✅ 新增 Roslyn 分析器文档
- ✅ 更新架构文档 - 反映最新设计

### 2025-10 (早期)
- ✅ 移除应用层节点发现（交给 K8s/Aspire）
- ✅ 序列化器架构重构（基础设施无关）
- ✅ 新增 Fluent Builder API
- ✅ 新增编译时分析器（CATGA001/CATGA002）
- ✅ 反射优化 - 90x 性能提升

---

## 📞 获取帮助

### 文档问题
- **GitHub Issues** - [报告文档问题](https://github.com/catga/catga/issues/new?labels=documentation)
- **Pull Request** - 直接提交文档改进

### 技术问题
- **GitHub Issues** - [报告 Bug](https://github.com/catga/catga/issues/new?labels=bug)
- **GitHub Discussions** - [提问和讨论](https://github.com/catga/catga/discussions)

### 贡献指南
- **[CONTRIBUTING.md](../CONTRIBUTING.md)** - 如何贡献代码和文档

---

## 📝 文档版本

- **最新稳定版**: v2.0.0
- **文档更新**: 2025-10-14
- **框架版本**: .NET 9.0

---

<div align="center">

**📚 探索 Catga 的强大功能！**

[返回主页](../README.md) · [快速参考](../QUICK-REFERENCE.md) · [示例项目](../examples/)

Made with ❤️ by the Catga Team

</div>
