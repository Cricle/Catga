# Catga 测试与文档完善计划

**创建时间**: 2025-10-19  
**状态**: 待执行  
**目标**: 完善测试覆盖率、优化示例项目、整理文档结构

---

## 📋 总览

### 当前状态分析

#### ✅ 已完成
- **核心功能**: CQRS、Event Sourcing、消息传输完整实现
- **基础测试**: 14个测试文件，覆盖核心功能
  - `CatgaMediatorTests.cs`
  - `CatgaResultTests.cs`
  - `Core/ArrayPoolHelperTests.cs`
  - `Core/BaseMemoryStoreTests.cs`
  - `Core/CatgaMediatorExtendedTests.cs`
  - `Core/CatgaResultExtendedTests.cs`
  - `Core/ShardedIdempotencyStoreTests.cs`
  - `Core/SnowflakeIdGeneratorTests.cs`
  - `Core/TypeNameCacheTests.cs`
  - `DistributedIdBatchTests.cs`
  - `Handlers/SafeRequestHandlerCustomErrorTests.cs`
  - `Pipeline/IdempotencyBehaviorTests.cs`
  - `Pipeline/LoggingBehaviorTests.cs`
  - `Pipeline/RetryBehaviorTests.cs`
  - `Serialization/JsonMessageSerializerTests.cs`
  - `Serialization/MemoryPackMessageSerializerTests.cs`
  - `Transport/InMemoryMessageTransportTests.cs`
  - `Transport/QosVerificationTests.cs`
- **示例项目**: 2个示例 (OrderSystem, MinimalApi)
- **文档**: 多层级文档结构

#### ❌ 缺失项
- **Redis Transport/Persistence 测试**: 0个
- **NATS Transport/Persistence 测试**: 0个
- **集成测试**: 0个 (Testcontainers)
- **E2E 测试**: 0个
- **性能基准测试**: 不完整
- **文档**: 有重复和过时内容

---

## 🎯 Phase 1: 代码质量 Review

### 1.1 编译警告修复
**优先级**: 🔴 HIGH

#### 发现的问题
```csharp
// NatsKVEventStore.cs:205
warning IL2057: Unrecognized value passed to Type.GetType(String)
```

#### 修复方案
```csharp
// 替换反射为泛型或添加 AOT 属性
[UnconditionalSuppressMessage("Trimming", "IL2057", 
    Justification = "Type names are validated at design time")]
```

**预计时间**: 15分钟  
**文件**: `src/Catga.Persistence.Nats/NatsKVEventStore.cs`

---

## 🧪 Phase 2: 完善单元测试

### 2.1 Transport 层测试
**优先级**: 🔴 HIGH  
**预计时间**: 3小时

#### 需要创建的测试文件

1. **`tests/Catga.Tests/Transport/RedisMessageTransportTests.cs`**
   ```csharp
   - PublishAsync_QoS0_UsesPubSub
   - PublishAsync_QoS1_UsesStreams
   - SendAsync_QoS0_UsesPubSub
   - SendAsync_QoS1_UsesStreams
   - SubscribeAsync_QoS0_ReceivesMessages
   - SubscribeAsync_QoS1_GuaranteesDelivery
   - PublishBatchAsync_Success
   - SendBatchAsync_Success
   - ErrorHandling_ReconnectionLogic
   - Dispose_CleansUpResources
   ```

2. **`tests/Catga.Tests/Transport/NatsMessageTransportTests.cs`**
   ```csharp
   - PublishAsync_CoreNats_Success
   - SendAsync_RequestReply_Success
   - SubscribeAsync_ReceivesMessages
   - PublishBatchAsync_Success
   - SendBatchAsync_Success
   - JetStreamIntegration_Success
   - ErrorHandling_Reconnection
   - Dispose_CleansUpResources
   ```

**覆盖率目标**: 90%+

### 2.2 Persistence 层测试
**优先级**: 🔴 HIGH  
**预计时间**: 4小时

#### 需要创建的测试文件

1. **`tests/Catga.Tests/Persistence/RedisEventStoreTests.cs`**
   ```csharp
   - SaveAsync_SingleEvent_Success
   - GetEventsAsync_RetrievesInOrder
   - GetEventsAsync_WithSnapshot_OptimizedRetrieval
   - SaveSnapshotAsync_Success
   - GetLatestSnapshotAsync_ReturnsNewest
   - ConcurrentWrites_HandledCorrectly
   - LargePayload_Chunking_Success
   - ErrorHandling_Retry
   ```

2. **`tests/Catga.Tests/Persistence/RedisOutboxStoreTests.cs`**
   ```csharp
   - AddAsync_Success
   - GetPendingAsync_RetrievesUnprocessed
   - MarkAsProcessedAsync_UpdatesStatus
   - MarkAsFailedAsync_UpdatesStatus
   - CleanupAsync_RemovesOldMessages
   - ConcurrentAccess_ThreadSafe
   ```

3. **`tests/Catga.Tests/Persistence/RedisInboxStoreTests.cs`**
   ```csharp
   - ExistsAsync_NewMessage_ReturnsFalse
   - ExistsAsync_ProcessedMessage_ReturnsTrue
   - MarkAsProcessedAsync_Success
   - CleanupAsync_RemovesExpired
   ```

4. **`tests/Catga.Tests/Persistence/NatsEventStoreTests.cs`**
   ```csharp
   - SaveAsync_JetStream_Success
   - GetEventsAsync_StreamRetrieval
   - GetEventsAsync_WithFilter
   - SaveSnapshotAsync_KVStore
   - GetLatestSnapshotAsync_KVStore
   - StreamConsumer_Subscription
   ```

5. **`tests/Catga.Tests/Persistence/NatsOutboxStoreTests.cs`**
   ```csharp
   - AddAsync_JetStream_Success
   - GetPendingAsync_WithConsumerAck
   - MarkAsProcessedAsync_AckMessage
   - RetryLogic_WithDeadLetterQueue
   ```

6. **`tests/Catga.Tests/Persistence/NatsInboxStoreTests.cs`**
   ```csharp
   - ExistsAsync_KVStore_Check
   - MarkAsProcessedAsync_KVStore
   - CleanupAsync_Expiration
   ```

7. **`tests/Catga.Tests/Persistence/InMemoryEventStoreTests.cs`**
   ```csharp
   - SaveAsync_InMemory_Success
   - GetEventsAsync_FusionCache
   - Snapshot_MemoryOptimization
   - ConcurrentAccess_ThreadSafe
   ```

8. **`tests/Catga.Tests/Persistence/InMemoryOutboxStoreTests.cs`**
   ```csharp
   - AddAsync_FusionCache_Success
   - GetPendingAsync_Concurrency
   - MarkAsProcessedAsync_AtomicOperation
   ```

9. **`tests/Catga.Tests/Persistence/InMemoryInboxStoreTests.cs`**
   ```csharp
   - ExistsAsync_ConcurrentHashMap
   - MarkAsProcessedAsync_ThreadSafe
   - Cleanup_BackgroundService
   ```

**覆盖率目标**: 85%+

### 2.3 AspNetCore 集成测试
**优先级**: 🟡 MEDIUM  
**预计时间**: 2小时

#### 需要创建的测试文件

1. **`tests/Catga.Tests/AspNetCore/DiagnosticsDashboardTests.cs`**
   ```csharp
   - HealthEndpoint_ReturnsStatus
   - MetricsEndpoint_ReturnsMetrics
   - ActivityEndpoint_ReturnsActivities
   - Authorization_EnforcesPolicy
   ```

2. **`tests/Catga.Tests/AspNetCore/CatgaHealthCheckTests.cs`**
   ```csharp
   - CheckHealthAsync_AllHealthy_ReturnsHealthy
   - CheckHealthAsync_TransportDown_ReturnsUnhealthy
   - CheckHealthAsync_PersistenceDown_ReturnsUnhealthy
   ```

---

## 🔗 Phase 3: 集成测试

### 3.1 Testcontainers 设置
**优先级**: 🔴 HIGH  
**预计时间**: 2小时

#### NuGet 包
```xml
<PackageReference Include="Testcontainers.Redis" Version="3.7.0" />
<PackageReference Include="Testcontainers.Nats" Version="1.0.0" />
```

#### 创建基础设施

1. **`tests/Catga.IntegrationTests/Catga.IntegrationTests.csproj`**
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <TargetFramework>net9.0</TargetFramework>
       <IsPackable>false</IsPackable>
     </PropertyGroup>
     <ItemGroup>
       <PackageReference Include="Testcontainers.Redis" />
       <PackageReference Include="Testcontainers.Nats" />
       <PackageReference Include="xunit" />
     </ItemGroup>
   </Project>
   ```

2. **`tests/Catga.IntegrationTests/Fixtures/RedisFixture.cs`**
   ```csharp
   public class RedisFixture : IAsyncLifetime
   {
       private RedisContainer _container;
       public string ConnectionString { get; private set; }
       
       public async Task InitializeAsync()
       {
           _container = new RedisBuilder().Build();
           await _container.StartAsync();
           ConnectionString = _container.GetConnectionString();
       }
       
       public Task DisposeAsync() => _container.DisposeAsync().AsTask();
   }
   ```

3. **`tests/Catga.IntegrationTests/Fixtures/NatsFixture.cs`**
   ```csharp
   public class NatsFixture : IAsyncLifetime
   {
       private NatsContainer _container;
       public string ConnectionString { get; private set; }
       
       public async Task InitializeAsync()
       {
           _container = new NatsBuilder()
               .WithJetStream()
               .Build();
           await _container.StartAsync();
           ConnectionString = _container.GetConnectionString();
       }
       
       public Task DisposeAsync() => _container.DisposeAsync().AsTask();
   }
   ```

### 3.2 端到端集成测试
**优先级**: 🟡 MEDIUM  
**预计时间**: 3小时

#### 测试场景

1. **`tests/Catga.IntegrationTests/RedisIntegrationTests.cs`**
   ```csharp
   - FullWorkflow_PublishSubscribe_Success
   - FullWorkflow_OutboxPattern_Success
   - FullWorkflow_InboxPattern_Deduplication
   - FullWorkflow_EventSourcing_Replay
   - PerformanceTest_1000Messages_Success
   ```

2. **`tests/Catga.IntegrationTests/NatsIntegrationTests.cs`**
   ```csharp
   - FullWorkflow_JetStream_Success
   - FullWorkflow_KVStore_Success
   - FullWorkflow_RequestReply_Success
   - PerformanceTest_Throughput
   ```

3. **`tests/Catga.IntegrationTests/HybridIntegrationTests.cs`**
   ```csharp
   - Redis_Transport_Nats_Persistence
   - Nats_Transport_Redis_Persistence
   - FailoverScenario_RedisToNats
   ```

---

## ⚡ Phase 4: 性能测试

### 4.1 补充 Benchmarks
**优先级**: 🟡 MEDIUM  
**预计时间**: 2小时

#### 需要创建的 Benchmark

1. **`benchmarks/Catga.Benchmarks/Transport/RedisTransportBenchmarks.cs`**
   ```csharp
   [Benchmark] PublishAsync_QoS0_Throughput
   [Benchmark] PublishAsync_QoS1_Throughput
   [Benchmark] PublishBatchAsync_1000Messages
   ```

2. **`benchmarks/Catga.Benchmarks/Transport/NatsTransportBenchmarks.cs`**
   ```csharp
   [Benchmark] PublishAsync_CoreNats
   [Benchmark] PublishAsync_JetStream
   [Benchmark] RequestReply_Latency
   ```

3. **`benchmarks/Catga.Benchmarks/Persistence/EventStoreBenchmarks.cs`**
   ```csharp
   [Benchmark] SaveEvent_Redis
   [Benchmark] SaveEvent_Nats
   [Benchmark] SaveEvent_InMemory
   [Benchmark] GetEvents_1000_Redis
   [Benchmark] GetEvents_1000_Nats
   ```

4. **`benchmarks/Catga.Benchmarks/Serialization/SerializerComparisonBenchmarks.cs`**
   ```csharp
   [Benchmark] Serialize_Json_1KB
   [Benchmark] Serialize_Json_100KB
   [Benchmark] Serialize_MemoryPack_1KB
   [Benchmark] Serialize_MemoryPack_100KB
   [Benchmark] Deserialize_Json_1KB
   [Benchmark] Deserialize_MemoryPack_1KB
   ```

---

## 📚 Phase 5: 示例项目整理

### 5.1 删除 MinimalApi 示例
**优先级**: 🟢 LOW  
**预计时间**: 15分钟

#### 操作步骤
1. 从解决方案中移除 `examples/MinimalApi`
2. 删除目录 `examples/MinimalApi/`
3. 更新 `Catga.sln`
4. 更新 `examples/README.md`

### 5.2 增强 OrderSystem 示例
**优先级**: 🟡 MEDIUM  
**预计时间**: 1小时

#### 增强内容
1. 添加完整的错误处理示例
2. 添加分布式追踪示例
3. 添加健康检查示例
4. 添加 Redis/NATS 切换配置
5. 更新 README，包含：
   - 架构图
   - 运行步骤
   - 关键代码解释
   - 常见问题

---

## 📄 Phase 6: 文档整理

### 6.1 删除过时文档
**优先级**: 🔴 HIGH  
**预计时间**: 30分钟

#### 需要删除的文档（待确认）

**临时/重复文档**:
- `docs/distributed/ARCHITECTURE.md` (与 `docs/architecture/ARCHITECTURE.md` 重复)
- `docs/distributed/KUBERNETES.md` (与 `docs/deployment/kubernetes.md` 重复)
- `docs/INDEX.md` (与 `docs/README.md` 重复)
- `docs/index.html` (应该只在 `docs/web/` 下)
- `examples/OrderSystem.AppHost/README-GRACEFUL.md` (临时文档)
- `examples/README-ORDERSYSTEM.md` (应合并到 `examples/OrderSystem.Api/README.md`)

**过时的架构文档**:
- 检查 `ARCHITECTURE-REFACTORING-EXECUTION.md` 是否过时
- 检查 `MEMORY-OPTIMIZATION-PLAN.md` 是否需要归档

### 6.2 整理文档结构
**优先级**: 🟡 MEDIUM  
**预计时间**: 1小时

#### 统一目录结构

```
docs/
├── README.md                       # 文档导航首页
├── getting-started/                # 快速开始
│   ├── installation.md
│   ├── quick-start.md
│   └── first-app.md
├── guides/                         # 使用指南
│   ├── cqrs.md
│   ├── event-sourcing.md
│   ├── message-transport.md
│   ├── persistence.md
│   ├── serialization.md
│   ├── distributed-id.md
│   ├── source-generator.md
│   ├── analyzers.md
│   ├── custom-error-handling.md
│   └── memory-optimization-guide.md
├── architecture/                   # 架构设计
│   ├── overview.md
│   ├── cqrs.md
│   └── RESPONSIBILITY-BOUNDARY.md
├── deployment/                     # 部署指南
│   ├── kubernetes.md
│   ├── native-aot-publishing.md
│   └── docker.md
├── observability/                  # 可观测性
│   ├── distributed-tracing.md
│   ├── monitoring.md
│   └── jaeger-integration.md
├── patterns/                       # 设计模式
│   └── distributed-transaction.md
├── api/                            # API 参考
│   ├── README.md
│   ├── mediator.md
│   └── messages.md
├── benchmarks/                     # 性能报告
│   └── PERFORMANCE-REPORT.md
├── changelog/                      # 变更历史
│   └── CHANGELOG.md
└── web/                            # GitHub Pages
    ├── index.html
    ├── api.html
    ├── style.css
    └── app.js
```

#### 更新索引文件

1. **`docs/README.md`** - 完整导航
2. **`README.md`** - 项目首页，链接到文档
3. **`AI-LEARNING-GUIDE.md`** - 保持最新

---

## 📊 执行优先级与时间估算

| Phase | 任务 | 优先级 | 预计时间 | 累计时间 |
|-------|------|--------|---------|---------|
| 1 | 代码质量 Review | 🔴 HIGH | 15分钟 | 0.25h |
| 2.1 | Transport 层测试 | 🔴 HIGH | 3小时 | 3.25h |
| 2.2 | Persistence 层测试 | 🔴 HIGH | 4小时 | 7.25h |
| 2.3 | AspNetCore 测试 | 🟡 MEDIUM | 2小时 | 9.25h |
| 3.1 | Testcontainers 设置 | 🔴 HIGH | 2小时 | 11.25h |
| 3.2 | 集成测试 | 🟡 MEDIUM | 3小时 | 14.25h |
| 4 | 性能测试 | 🟡 MEDIUM | 2小时 | 16.25h |
| 5.1 | 删除 MinimalApi | 🟢 LOW | 15分钟 | 16.5h |
| 5.2 | 增强 OrderSystem | 🟡 MEDIUM | 1小时 | 17.5h |
| 6.1 | 删除过时文档 | 🔴 HIGH | 30分钟 | 18h |
| 6.2 | 整理文档结构 | 🟡 MEDIUM | 1小时 | 19h |

**总预计时间**: 19小时

---

## 🎯 推荐执行顺序

### 快速模式 (高优先级任务)
1. Phase 1: 代码质量 Review (15分钟)
2. Phase 6.1: 删除过时文档 (30分钟)
3. Phase 5.1: 删除 MinimalApi (15分钟)
4. Phase 2.1: Transport 层测试 (3小时)
5. Phase 2.2: Persistence 层测试 (4小时)
6. Phase 3.1: Testcontainers 设置 (2小时)

**快速模式总时间**: 10小时

### 完整模式 (所有任务)
按上表顺序执行所有 Phase

---

## ✅ 完成标准

1. **测试覆盖率**:
   - 核心库 (Catga): >90%
   - Transport 库: >85%
   - Persistence 库: >85%
   - 整体: >85%

2. **文档质量**:
   - 无重复文档
   - 清晰的导航结构
   - 所有链接有效
   - 示例代码可运行

3. **代码质量**:
   - 0 编译警告
   - 0 TODO/FIXME
   - 通过所有单元测试
   - 通过所有集成测试

4. **性能基准**:
   - 完整的 Benchmark 覆盖
   - 生成性能报告
   - 与竞品对比数据

---

## 📝 注意事项

1. **AOT 兼容性**: 所有新增测试代码必须考虑 AOT 兼容性
2. **并发安全**: 重点测试多线程场景
3. **资源清理**: 确保所有测试正确清理资源 (Dispose)
4. **测试隔离**: 集成测试使用独立的容器实例
5. **文档链接**: 更新文档时检查所有相关链接

---

## 🚀 开始执行

选择执行模式：
- **快速模式**: 执行高优先级任务，用时 ~10小时
- **完整模式**: 执行所有任务，用时 ~19小时

**建议**: 先执行快速模式，验证效果后再执行剩余任务。

