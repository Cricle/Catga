# Catga 完整执行计划

**制定日期**: 2025-10-19
**总预计时间**: 33 小时
**目标**: 完成所有剩余 Phase，达到 100% 生产就绪

---

## 📋 执行概览

| Phase | 任务 | 时间 | 状态 |
|-------|------|------|------|
| ✅ Phase 1 | NatsJSOutboxStore 修复 | 0.5h | **完成** |
| ✅ Phase 3 | 配置增强 | 3h | **完成** |
| 🔄 Phase 2 | 测试增强 | 6h | **进行中** |
| ⏳ Phase 4 | 文档完善 | 5h | 待执行 |
| ⏳ Phase 5 | 生态系统集成 | 11h | 待执行 |
| 📦 Final | 提交和发布 | 1h | 待执行 |
| **总计** | | **26.5h** | **13% 完成** |

---

## 🎯 Phase 2: 测试增强 (6 小时)

### Task 2.1: 集成测试项目 (4 小时)

#### 2.1.1 创建测试项目 (0.5h)
```bash
dotnet new xunit -n Catga.IntegrationTests -o tests/Catga.IntegrationTests
dotnet sln add tests/Catga.IntegrationTests/Catga.IntegrationTests.csproj
```

**添加依赖**:
- Testcontainers
- Testcontainers.Redis
- Testcontainers.Nats (自定义)
- FluentAssertions
- xUnit

#### 2.1.2 Redis Transport 集成测试 (1h)
- Redis Pub/Sub (QoS 0) 真实传输
- Redis Streams (QoS 1) 真实传输
- 批量发送测试
- 错误处理测试

#### 2.1.3 NATS Transport 集成测试 (1h)
- NATS Core 消息传输
- NATS JetStream 消息传输
- 订阅和取消订阅
- 错误恢复测试

#### 2.1.4 Persistence 集成测试 (1.5h)
- **NATS Persistence**:
  - EventStore: 追加事件、读取事件流
  - OutboxStore: 添加、获取待处理、标记已发布/失败
  - InboxStore: 锁定、标记已处理、幂等性验证
- **Redis Persistence**:
  - OutboxStore 完整流程
  - InboxStore 完整流程

### Task 2.2: 性能基准测试 (2 小时)

#### 2.2.1 创建 Benchmark 项目 (0.5h)
```bash
dotnet new console -n Catga.Benchmarks -o tests/Catga.Benchmarks
dotnet sln add tests/Catga.Benchmarks/Catga.Benchmarks.csproj
```

**添加依赖**:
- BenchmarkDotNet
- BenchmarkDotNet.Diagnostics.Windows

#### 2.2.2 序列化器性能对比 (0.5h)
- JsonMessageSerializer vs MemoryPackMessageSerializer
- 小消息 (< 1KB)
- 中等消息 (1KB - 10KB)
- 大消息 (> 10KB)

#### 2.2.3 Transport 性能对比 (0.5h)
- InMemory vs Redis vs NATS
- 吞吐量测试 (messages/sec)
- 延迟测试 (P50, P95, P99)
- 批量发送性能

#### 2.2.4 ArrayPool 优化验证 (0.5h)
- 有/无 ArrayPool 对比
- 内存分配对比
- GC 压力对比

---

## 🎯 Phase 4: 文档完善 (5 小时)

### Task 4.1: API 文档生成 (3 小时)

#### 4.1.1 配置 DocFX (1h)
```bash
dotnet tool install -g docfx
docfx init -q
```

**配置文件**: `docfx.json`
- API 文档源: `src/**/*.csproj`
- 输出目录: `docs/_site`
- 主题: Modern

#### 4.1.2 编写文章 (2h)
- `docs/articles/getting-started.md` - 快速开始指南
- `docs/articles/architecture.md` - 架构设计文档
- `docs/articles/transport-layer.md` - Transport 层详解
- `docs/articles/persistence-layer.md` - Persistence 层详解
- `docs/articles/serialization.md` - 序列化器选择指南
- `docs/articles/aot-deployment.md` - Native AOT 部署指南
- `docs/articles/configuration.md` - 配置选项完整指南

### Task 4.2: 完善示例代码 (2 小时)

#### 4.2.1 MinimalApi 示例 (0.5h)
`examples/MinimalApi/` - 最简单的 Web API 示例
- 基础 CQRS
- InMemory Transport
- 健康检查

#### 4.2.2 Microservices 示例 (1h)
`examples/Microservices/` - 完整的微服务通信示例
- 2 个服务: OrderService, InventoryService
- Redis Transport
- NATS Persistence
- OpenTelemetry 集成
- .NET Aspire 配置

#### 4.2.3 EventSourcing 示例 (0.5h)
`examples/EventSourcing/` - 事件溯源完整示例
- 聚合根
- 事件存储
- 事件重放
- 快照

---

## 🎯 Phase 5: 生态系统集成 (11 小时)

### Task 5.1: OpenTelemetry 完整集成 (4 小时)

#### 5.1.1 ActivitySource 集成 (1.5h)
**创建**: `src/Catga/Observability/CatgaActivitySource.cs`
- 定义 Activity 名称常量
- 定义 Tag 名称常量
- 创建 ActivitySource 实例

**集成到组件**:
- `CatgaMediator.cs` - SendAsync, PublishAsync
- `InMemoryMessageTransport.cs`
- `RedisMessageTransport.cs`
- `NatsMessageTransport.cs`
- `OutboxPublisher.cs`
- `InboxProcessor.cs`

#### 5.1.2 自动 Trace 传播 (1h)
**创建**: `src/Catga/Observability/TraceContextPropagator.cs`
- Inject: 发送时注入 Trace Context
- Extract: 接收时提取并创建 Child Activity
- 遵循 W3C Trace Context 标准

#### 5.1.3 Metrics 导出 (1h)
**创建**: `src/Catga/Observability/CatgaMetrics.cs`
- Counter: MessagesPublished, MessagesSent, MessagesReceived, Processed, Failed
- Histogram: ProcessingDuration, OutboxProcessingDuration
- UpDownCounter: ActiveSubscriptions, PendingOutboxMessages

#### 5.1.4 Exemplar 支持 (0.5h)
- 在 Histogram.Record 时附加 TraceId
- 配置 OTLP Exporter
- 更新 Jaeger 示例

### Task 5.2: .NET Aspire Dashboard 集成 (3 小时)

#### 5.2.1 自定义资源类型 (1h)
**创建**: `src/Catga.AspNetCore/Aspire/CatgaResource.cs`
- `CatgaResource` 类
- `CatgaResourceExtensions` - AddCatga, WithRedisTransport, WithNatsTransport
- Manifest 发布支持

#### 5.2.2 实时监控 (1h)
**创建**: `src/Catga.AspNetCore/Aspire/CatgaHealthCheck.cs`
- 健康检查实现
- 实时指标收集
- Dashboard 数据导出

#### 5.2.3 示例项目 (1h)
**创建**: `examples/AspireIntegration/`
- AppHost 项目
- API 项目
- Redis/NATS 容器配置
- Dashboard 截图

### Task 5.3: Source Generator 增强 (4 小时)

#### 5.3.1 AsyncTaskAnalyzer (1h)
**创建**: `src/Catga.SourceGenerator/Analyzers/AsyncTaskAnalyzer.cs`
- 检测未 await 的 Task
- 提供 Code Fix
- 单元测试

#### 5.3.2 MissingDIRegistrationAnalyzer (1.5h)
**创建**: `src/Catga.SourceGenerator/Analyzers/MissingDIRegistrationAnalyzer.cs`
- 检测 IMessageTransport 使用但未注册
- 检测 IEventStore 使用但未注册
- 检测 IMessageSerializer 使用但未注册
- 提供建议的注册代码

#### 5.3.3 AotCompatibilityAnalyzer (1h)
**创建**: `src/Catga.SourceGenerator/Analyzers/AotCompatibilityAnalyzer.cs`
- 检测直接使用 JsonSerializer
- 检测 Type.GetType() 未标记
- 检测反射创建实例

#### 5.3.4 BenchmarkGenerator (0.5h)
**创建**: `src/Catga.SourceGenerator/Generators/BenchmarkGenerator.cs`
- 为 [GenerateBenchmark] 标记的 Handler 生成测试
- 自动设置 Setup/Cleanup
- 集成 MemoryDiagnoser

---

## 📦 Final: 提交和发布 (1 小时)

### 最终检查清单

#### 编译和测试
- [ ] `dotnet build` - 0 错误, 0 警告
- [ ] `dotnet test` - 所有测试通过
- [ ] 集成测试通过
- [ ] Benchmark 运行成功

#### 文档
- [ ] API 文档生成成功
- [ ] 所有示例项目可运行
- [ ] README.md 更新
- [ ] CHANGELOG.md 生成

#### 代码质量
- [ ] 所有 TODO 已完成
- [ ] 无未使用的代码
- [ ] 所有公共 API 有 XML 注释
- [ ] License 头部正确

### Git 提交

```bash
# 提交 Phase 2
git add tests/Catga.IntegrationTests tests/Catga.Benchmarks
git commit -m "feat: add integration tests and benchmarks (Phase 2)"

# 提交 Phase 4
git add docs/ examples/
git commit -m "docs: complete API documentation and examples (Phase 4)"

# 提交 Phase 5
git add src/Catga/Observability src/Catga.AspNetCore/Aspire src/Catga.SourceGenerator/Analyzers
git commit -m "feat: add OpenTelemetry, Aspire, and enhanced analyzers (Phase 5)"

# 打 tag
git tag -a v1.0.0 -m "Release v1.0.0 - Production Ready"
git push origin master --tags
```

### 发布到 NuGet

```bash
# 打包
dotnet pack -c Release -o ./nupkgs

# 发布 (需要 API Key)
dotnet nuget push ./nupkgs/*.nupkg -s https://api.nuget.org/v3/index.json
```

---

## 📊 里程碑和进度追踪

### 已完成 ✅
- [x] Phase 1: NatsJSOutboxStore 修复 (0.5h)
- [x] Phase 3: 配置增强 (3h)

### 当前进度 🔄
- [ ] Phase 2: 测试增强 (0/6h)
  - [ ] 2.1: 集成测试 (0/4h)
  - [ ] 2.2: 性能测试 (0/2h)

### 待执行 ⏳
- [ ] Phase 4: 文档完善 (0/5h)
- [ ] Phase 5: 生态系统集成 (0/11h)
- [ ] Final: 提交和发布 (0/1h)

### 总进度
**已完成**: 3.5 / 26.5 小时 (13.2%)
**预计剩余**: 23 小时

---

## 🎯 执行策略

### 分批执行 (推荐)
1. **第一批**: Phase 2 (6h) - 今日/明日完成
2. **第二批**: Phase 4 (5h) - 本周完成
3. **第三批**: Phase 5 (11h) - 下周完成
4. **最终**: 发布 (1h)

### 一次性执行 (挑战模式)
- 连续 23 小时执行
- 需要 3 个工作日
- 建议分多个 session

---

## 💡 注意事项

### Phase 2 注意事项
- Testcontainers 需要 Docker 运行
- NATS Testcontainer 可能需要自定义镜像
- 集成测试会比较慢 (每个测试 1-5 秒)

### Phase 4 注意事项
- DocFX 需要 .NET SDK
- 示例项目需要独立可运行
- 文档需要定期更新

### Phase 5 注意事项
- OpenTelemetry 需要配置 OTLP Endpoint
- Aspire 需要 .NET 9 SDK
- Source Generator 开发较复杂，需要调试技巧

---

## 🚀 开始执行！

**准备好了吗？我将立即开始执行 Phase 2.1: 集成测试项目创建！**

请确认：
- [ ] Docker 已运行 (用于 Testcontainers)
- [ ] 有足够的磁盘空间 (约 5GB，包括 Docker 镜像)
- [ ] 网络连接正常 (需要下载 NuGet 包和 Docker 镜像)

**让我们开始吧！** 🎯

