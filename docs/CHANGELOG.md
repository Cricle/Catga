# Changelog

本文档记录了 Catga 的所有重要更改。

格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/)，
版本号遵循 [Semantic Versioning](https://semver.org/lang/zh-CN/)。

---

## [0.1.0] - 2025-12-07

### 🎉 事件溯源完整实现！

**核心功能**:
- ✅ 97 个新测试全部通过
- ✅ 完整 Event Sourcing 支持
- ✅ 多后端支持 (InMemory/Redis/NATS)

#### Event Sourcing 核心
- ✅ **Event Store** - 事件存储 (InMemory/Redis/NATS)
- ✅ **Projections** - 事件投影 (CatchUp/Live/Rebuild)
- ✅ **Subscriptions** - 持久订阅 (Pattern matching)
- ✅ **Enhanced Snapshots** - 增强快照 (版本历史/时间旅行)
- ✅ **Time Travel** - 时间旅行查询 (任意版本状态重建)
- ✅ **Event Versioning** - 事件版本升级 (Upcasters)

#### 工具
- ✅ **Catga.Cli** - CLI 工具 (事件查询/投影重建/Flow管理)
- ✅ **Catga.Dashboard** - Web 监控面板

#### OrderSystem.Api 示例
- ✅ 完整 UI 展示所有功能 (Time Travel/Projections/Subscriptions/Snapshots)
- ✅ 新增 API 端点组

#### 文档
- ✅ Event Sourcing 完整指南 (`docs/articles/event-sourcing.md`)
- ✅ README 更新

---

## [Unreleased]

### Added

#### Flow DSL - 分布式事务 DSL
- ✅ **Fluent DSL API** - `Send`, `Query`, `Publish`, `WhenAll`, `WhenAny`
- ✅ **自动补偿** - `IfFail`, `IfAnyFail` 自动回滚
- ✅ **条件执行** - `OnlyWhen` 条件步骤
- ✅ **并行执行** - `WhenAll`/`WhenAny` 子流程协调
- ✅ **超时配置** - 默认超时 + 标签超时
- ✅ **状态追踪** - `IFlowState` 变更追踪
- ✅ **持久化** - InMemory + Redis 存储
- ✅ **Telemetry** - ActivitySource + Metrics 集成
- ✅ **源代码生成器** - `[FlowState]` 自动生成 IFlowState

#### 新增文件
- `src/Catga/Flow/Abstractions.cs` - 核心接口
- `src/Catga/Flow/FlowConfig.cs` - DSL 配置
- `src/Catga/Flow/DslFlowExecutor.cs` - 执行器
- `src/Catga/Flow/InMemoryDslFlowStore.cs` - 内存存储
- `src/Catga/Flow/FlowResumeHandler.cs` - 恢复处理器
- `src/Catga/Flow/DslFlowServiceExtensions.cs` - DI 扩展
- `src/Catga.Persistence.Redis/Flow/RedisDslFlowStore.cs` - Redis 存储
- `src/Catga.SourceGenerator/FlowStateGenerator.cs` - 源生成器
- `docs/guides/flow-dsl.md` - 文档

---

## [1.0.0] - 2025-10-14

### 🎉 首个正式版发布！

这是 Catga 的首个稳定版本，提供生产级别的高性能、100% AOT 兼容的分布式 CQRS 框架。

**核心成就**:
- ✅ 191 个单元测试全部通过 (100% 通过率)
- ✅ 70 个性能基准测试全部达标
- ✅ 65% 代码覆盖率 (超目标 5%)
- ✅ 100% AOT 兼容 (零 AOT 警告)
- ✅ 完整文档和示例

### Added

#### 核心功能
- ✅ **CQRS Mediator 实现** - 高性能的命令/查询/事件处理
- ✅ **Request/Response 模式** - 类型安全的请求响应
- ✅ **Event Publishing** - 异步事件发布和订阅
- ✅ **Pipeline Behaviors** - 可组合的中间件管道
  - Logging Behavior - 结构化日志记录
  - Tracing Behavior - 分布式追踪 (OpenTelemetry)
  - Validation Behavior - 请求验证
  - Retry Behavior - 自动重试
  - Idempotency Behavior - 幂等性保证

#### AOT 支持
- ✅ **100% Native AOT 兼容** - 完全支持 .NET AOT 编译
  - 3MB 可执行文件大小
  - < 20ms 启动时间
  - < 10MB 内存占用
- ✅ **零反射设计** - 使用源生成器替代反射
- ✅ **Trim 友好** - 正确的 DynamicallyAccessedMembers 标注

#### 序列化
- ✅ **MemoryPack 序列化器** (推荐) - 100% AOT 兼容
  - 5x 性能提升 vs JSON
  - 40% 更小的 payload
  - 零分配序列化
- ✅ **JSON 序列化器** (可选) - System.Text.Json
  - 支持源生成 JsonSerializerContext
  - 人类可读格式

#### 传输层
- ✅ **InMemory 传输** - 进程内通信 (开发/测试)
- ✅ **NATS 传输** - 生产级消息队列
  - JetStream 支持
  - QoS 保证 (AtMostOnce, AtLeastOnce, ExactlyOnce)
  - Consumer Groups
- ✅ **Redis 传输** - Redis Streams
  - QoS 1 支持 (AtLeastOnce)
  - Consumer Groups
  - Dead Letter Queue

#### 持久化
- ✅ **Outbox Pattern** - 可靠的事件发布
- ✅ **Inbox Pattern** - 消息去重和幂等性
- ✅ **幂等性存储** - ShardedIdempotencyStore
  - Lock-free 并发设计
  - 分片减少锁竞争
  - 自动过期清理
- ✅ **Redis 持久化** - 生产级存储后端
  - Outbox Store
  - Inbox Store
  - Idempotency Store
  - Distributed Cache
  - Distributed Lock

#### 分布式功能
- ✅ **Snowflake ID 生成器** - 分布式 ID 生成
  - 高性能 (百万级/秒)
  - 线程安全
  - 零分配
  - 时间排序
- ✅ **分布式锁** - RedisDistributedLock
  - 自动续期
  - 超时保护
  - 公平锁 (FIFO)
- ✅ **分布式缓存** - RedisDistributedCache
  - 自动序列化/反序列化
  - 批量操作
  - 过期策略

#### 质量保证
- ✅ **QoS 支持** - 三种服务质量级别
  - AtMostOnce (QoS 0) - 最多一次
  - AtLeastOnce (QoS 1) - 至少一次
  - ExactlyOnce (QoS 2) - 恰好一次
- ✅ **消息重试** - 可配置的重试策略
- ✅ **Dead Letter Queue** - 失败消息处理
- ✅ **健康检查** - IHealthCheck 实现

#### ASP.NET Core 集成
- ✅ **Minimal API 集成** - CatgaEndpointExtensions
- ✅ **Controller 集成** - 自动模型绑定
- ✅ **RPC 支持** - HTTP-based RPC 调用
- ✅ **Swagger 集成** - API 文档自动生成
- ✅ **CatgaResult 映射** - 自动 HTTP 状态码映射

#### 开发体验
- ✅ **Fluent API** - 简洁的配置 API
  ```csharp
  services.AddCatga()
      .UseMemoryPack()
      .ForProduction();
  ```
- ✅ **Roslyn 分析器** - 编译时检查
  - CATGA001: 检测缺少 [MemoryPackable] 属性
  - CATGA002: 检测缺少序列化器注册
- ✅ **源生成器** - 自动生成注册代码
- ✅ **IntelliSense 支持** - 完整的 XML 文档注释
- ✅ **Code Fixes** - 自动修复建议

#### .NET Aspire 支持
- ✅ **Aspire 集成** - 开箱即用的 Aspire 支持
- ✅ **服务发现** - 自动服务发现
- ✅ **可观测性** - 集成 OpenTelemetry

#### 可观测性
- ✅ **ActivitySource** - 分布式追踪
  - 自动传播 TraceContext
  - 完整的调用链
- ✅ **Metrics** - 性能指标
  - Counter - 请求计数
  - Histogram - 延迟分布
  - ObservableGauge - 当前状态
- ✅ **Structured Logging** - 结构化日志
  - LoggerMessage 源生成
  - 高性能日志记录

### Performance

- ⚡ **5x 吞吐量提升** - vs 传统 JSON 序列化
- ⚡ **96% 启动时间减少** - Native AOT (20ms vs 500ms)
- ⚡ **95% 包大小减少** - Native AOT (3MB vs 60MB)
- ⚡ **80% 内存占用减少** - Native AOT (10MB vs 50MB)
- ⚡ **零分配热路径** - 使用 Span<T> 和 ArrayPool
- ⚡ **Lock-free 并发** - ConcurrentDictionary, ImmutableList

### Documentation

- 📖 **完整的中文文档**
  - README.md - 30 秒快速开始
  - INDEX.md - 5 分钟速查手册
  - 架构设计文档
  - API 参考文档
  - 部署指南 (K8s, Docker)
- 📖 **示例项目**
  - OrderSystem.AppHost - .NET Aspire 示例
  - MemoryPackAotDemo - Native AOT 示例
- 📖 **性能基准测试** - BenchmarkDotNet 报告

### Infrastructure

- 🔧 **CI/CD Pipeline** - GitHub Actions
  - 自动构建和测试
  - 代码覆盖率报告
  - NuGet 自动发布
- 🔧 **中央包管理** - Directory.Packages.props
- 🔧 **SourceLink 支持** - 调试体验优化

### NuGet Packages

发布以下 NuGet 包:

- **Catga** - 核心框架
- **Catga.InMemory** - 内存实现
- **Catga.Serialization.MemoryPack** - MemoryPack 序列化器
- **Catga.Transport.Nats** - NATS 传输
- **Catga.Persistence.Redis** - Redis 持久化
- **Catga.AspNetCore** - ASP.NET Core 集成
- **Catga.SourceGenerator** - Roslyn 分析器和源生成器

---

## [0.9.0-rc.1] - 2025-10-18

### Added

- 🔧 Release Candidate 1 for testing

---

## 版本说明

- **[1.0.0]** - 首个稳定版本
- **[0.9.x]** - Release Candidate 版本
- **[0.x.x]** - Beta 版本 (不稳定)

---

## 贡献指南

请查看 [CONTRIBUTING.md](./development/CONTRIBUTING.md) 了解如何贡献代码。

## License

[MIT](https://github.com/Cricle/Catga/blob/master/LICENSE) © 2025 Catga Contributors

---

[Unreleased]: https://github.com/Cricle/Catga/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/Cricle/Catga/releases/tag/v1.0.0
[0.9.0-rc.1]: https://github.com/Cricle/Catga/releases/tag/v0.9.0-rc.1



