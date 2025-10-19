# Catga 下一步执行计划

**制定日期**: 2025-10-19
**基于**: REVIEW-REPORT.md 分析结果

---

## 🎯 执行策略

基于当前项目状态 (95% 生产就绪)，我们采用 **修复 → 验证 → 增强** 的渐进式策略。

---

## 📅 Phase 1: 关键 Bug 修复 (立即执行)

### Task 1.1: 修复 NatsJSOutboxStore.IncrementRetryCountAsync
**优先级**: 🔴 Critical
**预计时间**: 30 分钟
**文件**: `src/Catga.Persistence.Nats/Stores/NatsJSOutboxStore.cs`

#### 实现步骤
1. 实现消息检索逻辑 (JetStream Consumer)
2. 反序列化现有 OutboxMessage
3. 增加 RetryCount 并更新时间戳
4. 重新序列化并发布
5. 确认 (Ack) 旧消息

#### 验证标准
- ✅ 编译通过
- ✅ 单元测试通过
- ✅ 能够正确递增重试计数

---

## 📅 Phase 2: 测试增强 (本周内)

### Task 2.1: 添加集成测试项目
**优先级**: 🟡 High
**预计时间**: 4 小时

#### 实现内容
```
tests/Catga.IntegrationTests/
├── Fixtures/
│   ├── RedisFixture.cs       (Testcontainers)
│   └── NatsFixture.cs        (Testcontainers)
├── Transport/
│   ├── RedisTransportTests.cs
│   └── NatsTransportTests.cs
├── Persistence/
│   ├── RedisOutboxTests.cs
│   ├── NatsOutboxTests.cs
│   └── EventStoreTests.cs
└── EndToEnd/
    └── MessageFlowTests.cs   (完整的消息流测试)
```

#### 关键测试场景
1. **Redis Transport QoS 0/1 真实传输**
2. **NATS JetStream 消息持久化**
3. **Outbox 重试机制 (含 IncrementRetryCount)**
4. **Inbox 幂等性验证**
5. **跨传输层消息传递**

### Task 2.2: 添加性能基准测试
**优先级**: 🟢 Medium
**预计时间**: 2 小时

#### 实现内容
```
tests/Catga.Benchmarks/
├── SerializerBenchmarks.cs
│   ├── Json vs MemoryPack
│   └── 有/无 ArrayPool 对比
├── TransportBenchmarks.cs
│   ├── InMemory vs Redis vs NATS
│   └── 批量发送性能
└── PersistenceBenchmarks.cs
    ├── EventStore 写入吞吐
    └── Outbox 处理延迟
```

---

## 📅 Phase 3: 配置增强 (下周)

### Task 3.1: NATS JetStream 配置增强
**优先级**: 🟢 Medium
**预计时间**: 2 小时

#### 新增配置类
```csharp
public class NatsJSStoreOptions
{
    public string StreamName { get; set; } = "CATGA";
    public StreamConfigRetention Retention { get; set; } = StreamConfigRetention.Limits;
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromDays(7);
    public long MaxMsgs { get; set; } = 1_000_000;
    public int Replicas { get; set; } = 1;        // 高可用
    public bool Compress { get; set; } = true;    // 压缩
}
```

#### DI 扩展更新
```csharp
services.AddNatsPersistence(options =>
{
    options.StreamName = "MY_EVENTS";
    options.Retention = StreamConfigRetention.Interest;
    options.Replicas = 3; // HA
});
```

### Task 3.2: Redis Transport 配置增强
**优先级**: 🟢 Medium
**预计时间**: 1.5 小时

#### 新增配置
```csharp
public class RedisTransportOptions
{
    // 现有
    public string ConnectionString { get; set; }
    public QualityOfService DefaultQoS { get; set; }

    // 新增
    public int ConnectTimeout { get; set; } = 5000;
    public int SyncTimeout { get; set; } = 5000;
    public int AsyncTimeout { get; set; } = 5000;
    public bool AbortOnConnectFail { get; set; } = false;
    public string ClientName { get; set; } = "Catga";

    // Cluster 支持
    public bool AllowAdmin { get; set; } = false;
    public RedisMode Mode { get; set; } = RedisMode.Standalone;
}

public enum RedisMode
{
    Standalone,
    Sentinel,
    Cluster
}
```

---

## 📅 Phase 4: 文档完善 (持续)

### Task 4.1: API 文档生成
**优先级**: 🟢 Medium
**预计时间**: 3 小时

#### 工具
- DocFX
- 自动从 XML 注释生成

#### 内容结构
```
docs/
├── api/                  (自动生成)
├── articles/
│   ├── getting-started.md
│   ├── architecture.md
│   ├── transport-layer.md
│   ├── persistence-layer.md
│   ├── serialization.md
│   └── aot-deployment.md
└── examples/
    ├── basic-usage.md
    ├── outbox-pattern.md
    ├── event-sourcing.md
    └── migration-guide.md
```

### Task 4.2: 完善示例代码
**优先级**: 🟢 Medium
**预计时间**: 2 小时

#### 新增示例
1. **examples/MinimalApi/** - 最简单的使用场景
2. **examples/Microservices/** - 微服务通信示例
3. **examples/EventSourcing/** - 事件溯源完整示例
4. **examples/Performance/** - 性能优化技巧

---

## 📅 Phase 5: 生态系统集成 (未来)

### Task 5.1: OpenTelemetry 完整集成
**优先级**: 🔵 Low
**预计时间**: 4 小时

#### 实现内容
- ActivitySource 集成
- 自动 Trace 传播 (Transport 层)
- Metrics 导出 (消息吞吐、延迟、错误率)
- Exemplar 支持 (关联 Traces 和 Metrics)

### Task 5.2: .NET Aspire Dashboard 集成
**优先级**: 🔵 Low
**预计时间**: 3 小时

#### 实现内容
- 自定义资源类型注册
- 实时消息流可视化
- 健康检查集成
- 分布式追踪可视化

### Task 5.3: Source Generator 增强
**优先级**: 🔵 Low
**预计时间**: 4 小时

#### 新增分析器
- 检测未 await 的 Task
- 检测缺失的 DI 注册
- 检测不支持 AOT 的代码模式
- 自动生成 Benchmark 代码

---

## 🚀 立即执行建议

### 今日 (2025-10-19)
1. ✅ **修复 NatsJSOutboxStore.IncrementRetryCountAsync** (30min)
2. ✅ **添加单元测试验证修复** (15min)
3. ✅ **运行完整测试套件** (5min)
4. ✅ **提交并打 tag: v1.0.0-rc1** (10min)

### 本周
- 📝 实现集成测试项目 (Task 2.1)
- 📊 添加性能基准测试 (Task 2.2)

### 下周
- ⚙️ NATS/Redis 配置增强 (Task 3.1, 3.2)
- 📖 完善文档和示例 (Task 4.1, 4.2)

---

## 📊 成功指标

### 短期 (本周)
- ✅ 所有已知 Bug 修复
- ✅ 集成测试覆盖率 > 80%
- ✅ 性能基准报告生成

### 中期 (本月)
- ✅ 配置项完整暴露
- ✅ 文档覆盖率 > 90%
- ✅ 发布 v1.0.0 正式版

### 长期 (下季度)
- ✅ OpenTelemetry 完整集成
- ✅ .NET Aspire 深度集成
- ✅ 社区生态初步建立 (5+ 外部贡献)

---

## 🎯 决策点

**现在需要你决定**:

1. **是否立即修复 NatsJSOutboxStore？**
   - [ ] 是 → 我立即开始实现
   - [ ] 否 → 跳过，直接进入其他 Phase

2. **下一步优先级？**
   - [ ] Phase 1: Bug 修复
   - [ ] Phase 2: 测试增强
   - [ ] Phase 3: 配置增强
   - [ ] Phase 4: 文档完善
   - [ ] Phase 5: 生态系统集成

3. **是否需要我自动执行所有 Phase 1 任务？**
   - [ ] 是 → 全自动执行
   - [ ] 否 → 逐个确认

---

**请告诉我你的选择，我会立即开始执行！** 🚀

