# Catga 完整测试覆盖计划

## 📊 当前测试现状

### ✅ 已有单元测试 (19 个文件, 111 个测试用例)

**核心组件测试**:
- ✅ `CatgaMediatorTests.cs` - 10 个测试
- ✅ `CatgaResultTests.cs` - 测试结果类型
- ✅ `SnowflakeIdGeneratorTests.cs` - 14 个测试
- ✅ `ArrayPoolHelperTests.cs` - 内存池测试
- ✅ `ShardedIdempotencyStoreTests.cs` - 幂等性存储
- ✅ `TypeNameCacheTests.cs` - 类型名称缓存
- ✅ `BaseMemoryStoreTests.cs` - 基础存储测试

**Pipeline 测试**:
- ✅ `IdempotencyBehaviorTests.cs`
- ✅ `LoggingBehaviorTests.cs`
- ✅ `RetryBehaviorTests.cs`
- ✅ `ValidationBehaviorTests.cs`

**其他测试**:
- ✅ `QosVerificationTests.cs` - QoS 验证
- ✅ `DistributedIdBatchTests.cs` - 批量 ID 生成

### ✅ 已有基准测试 (7 个文件)

- ✅ `AdvancedIdGeneratorBenchmark.cs` - 高级 ID 生成器 (SIMD, Warmup, Adaptive)
- ✅ `DistributedIdBenchmark.cs` - 基础 ID 生成性能
- ✅ `DistributedIdOptimizationBenchmark.cs` - ID 生成优化对比
- ✅ `AllocationBenchmarks.cs` - 内存分配测试
- ✅ `ReflectionOptimizationBenchmark.cs` - 反射优化测试
- ✅ `SerializationBenchmarks.cs` - 序列化性能测试

---

## 🎯 需要补充的测试

### 📦 阶段 1: 核心组件单元测试 (优先级: P0)

#### 1.1 序列化器测试 ⭐ **关键**
**文件**: `tests/Catga.Tests/Serialization/`

- [ ] **MemoryPackSerializerTests.cs** (15 个测试)
  - 基本序列化/反序列化
  - 复杂对象序列化
  - 空值处理
  - 大对象序列化
  - 并发序列化
  - 错误处理
  - 性能验证 (< 100ns)

- [ ] **JsonSerializerTests.cs** (15 个测试)
  - 基本序列化/反序列化
  - 自定义 JsonSerializerOptions
  - JsonSerializerContext 支持
  - 错误处理
  - UTF-8 编码验证

**预估时间**: 2 小时
**覆盖率目标**: 90%+

---

#### 1.2 传输层测试 ⭐ **关键**
**文件**: `tests/Catga.Tests/Transport/`

- [ ] **InMemoryTransportTests.cs** (12 个测试)
  - 消息发布
  - 消息订阅
  - 多订阅者
  - 取消订阅
  - 并发发布
  - QoS 验证

- [ ] **NatsTransportTests.cs** (15 个测试)
  - NATS 连接
  - JetStream 发布
  - JetStream 订阅
  - QoS 0/1/2 验证
  - 重连机制
  - 错误处理
  - 批量操作

**预估时间**: 3 小时
**覆盖率目标**: 85%+

---

#### 1.3 持久化层测试 ⭐ **关键**
**文件**: `tests/Catga.Tests/Persistence/`

- [ ] **RedisOutboxTests.cs** (12 个测试)
  - 消息存储
  - 消息发布
  - 批量操作
  - 过期清理
  - 并发写入
  - 错误恢复

- [ ] **RedisInboxTests.cs** (12 个测试)
  - 消息接收
  - 幂等性验证
  - 批量处理
  - 过期清理

- [ ] **RedisCacheTests.cs** (10 个测试)
  - Get/Set/Remove
  - 过期时间
  - 批量操作
  - 并发访问

- [ ] **RedisLockTests.cs** (10 个测试)
  - 获取锁
  - 释放锁
  - 锁超时
  - 并发竞争

**预估时间**: 4 小时
**覆盖率目标**: 85%+

---

#### 1.4 ASP.NET Core 集成测试
**文件**: `tests/Catga.AspNetCore.Tests/`

- [ ] **RpcEndpointTests.cs** (10 个测试)
  - RPC 调用
  - 错误处理
  - 超时处理
  - 并发调用

- [ ] **CatgaEndpointTests.cs** (8 个测试)
  - HTTP 端点映射
  - 请求处理
  - 响应格式化

**预估时间**: 2 小时
**覆盖率目标**: 80%+

---

#### 1.5 Source Generator 测试
**文件**: `tests/Catga.SourceGenerator.Tests/`

- [ ] **AnalyzerTests.cs** (15 个测试)
  - CATGA001 检测
  - CATGA002 检测
  - 其他分析器

- [ ] **CodeFixTests.cs** (10 个测试)
  - 自动修复验证

**预估时间**: 3 小时
**覆盖率目标**: 85%+

---

### 🚀 阶段 2: 性能基准测试 (优先级: P1)

#### 2.1 CQRS 核心性能测试 ⭐ **关键**
**文件**: `benchmarks/Catga.Benchmarks/CqrsPerformanceBenchmarks.cs`

```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class CqrsPerformanceBenchmarks
{
    // Command 处理吞吐量
    [Benchmark]
    public async Task SendCommand_Single() { }

    [Benchmark]
    public async Task SendCommand_Batch_100() { }

    [Benchmark]
    public async Task SendCommand_Batch_1000() { }

    // Query 处理吞吐量
    [Benchmark]
    public async Task SendQuery_Single() { }

    // Event 发布吞吐量
    [Benchmark]
    public async Task PublishEvent_Single() { }

    [Benchmark]
    public async Task PublishEvent_Batch_100() { }
}
```

**性能目标**:
- Command: < 1μs (> 1M ops/s)
- Query: < 1μs
- Event: < 1.5μs
- GC: Gen0 = 0

**预估时间**: 2 小时

---

#### 2.2 并发性能测试
**文件**: `benchmarks/Catga.Benchmarks/ConcurrencyBenchmarks.cs`

```csharp
[Benchmark]
public async Task Concurrent_Commands_10() { }

[Benchmark]
public async Task Concurrent_Commands_100() { }

[Benchmark]
public async Task Concurrent_Commands_1000() { }

[Benchmark]
public async Task Concurrent_Events_100() { }
```

**性能目标**:
- 10 并发: < 10μs
- 100 并发: < 100μs
- 1000 并发: < 1ms

**预估时间**: 1.5 小时

---

#### 2.3 序列化性能对比测试
**文件**: `benchmarks/Catga.Benchmarks/SerializationComparisonBenchmarks.cs`

```csharp
[Benchmark(Baseline = true)]
public void MemoryPack_Serialize() { }

[Benchmark]
public void Json_Serialize() { }

[Benchmark]
public void MemoryPack_Deserialize() { }

[Benchmark]
public void Json_Deserialize() { }

[Benchmark]
public void MemoryPack_RoundTrip() { }

[Benchmark]
public void Json_RoundTrip() { }
```

**性能目标**:
- MemoryPack: ~100ns (序列化), ~150ns (反序列化)
- JSON: ~500ns (序列化), ~800ns (反序列化)
- MemoryPack 应快 **5x**

**预估时间**: 1.5 小时

---

#### 2.4 幂等性存储性能测试
**文件**: `benchmarks/Catga.Benchmarks/IdempotencyStoreBenchmarks.cs`

```csharp
[Benchmark]
public async Task IdempotencyStore_CacheMiss() { }

[Benchmark]
public async Task IdempotencyStore_CacheHit() { }

[Benchmark]
public async Task IdempotencyStore_Store_New() { }

[Benchmark]
public async Task IdempotencyStore_Store_Update() { }

[Benchmark]
public async Task IdempotencyStore_Cleanup() { }

[Benchmark(Baseline = true)]
[Arguments(16)]
public async Task IdempotencyStore_Shards_16(int shards) { }

[Benchmark]
[Arguments(32)]
public async Task IdempotencyStore_Shards_32(int shards) { }

[Benchmark]
[Arguments(64)]
public async Task IdempotencyStore_Shards_64(int shards) { }
```

**性能目标**:
- Cache Miss: < 100ns
- Cache Hit: < 200ns
- Store: < 500ns
- 16 分片为最佳平衡

**预估时间**: 2 小时

---

#### 2.5 Pipeline 行为性能测试
**文件**: `benchmarks/Catga.Benchmarks/PipelineBehaviorBenchmarks.cs`

```csharp
[Benchmark(Baseline = true)]
public async Task Pipeline_NoBehavior() { }

[Benchmark]
public async Task Pipeline_WithRetry() { }

[Benchmark]
public async Task Pipeline_WithValidation() { }

[Benchmark]
public async Task Pipeline_WithIdempotency() { }

[Benchmark]
public async Task Pipeline_AllBehaviors() { }
```

**性能目标**:
- No Behavior: < 50μs (Baseline)
- + Retry: < 80μs (+60%)
- + Validation: < 70μs (+40%)
- + All: < 100μs (+100%)

**预估时间**: 1.5 小时

---

## 📈 测试覆盖率目标

### 单元测试覆盖率

| 组件 | 当前覆盖率 | 目标覆盖率 | 状态 |
|------|-----------|-----------|------|
| **Catga (核心)** | ~60% | **85%** | 🟡 需补充 |
| **Catga.InMemory** | ~70% | **90%** | 🟡 需补充 |
| **Catga.Serialization.MemoryPack** | 0% | **90%** | 🔴 缺失 |
| **Catga.Serialization.Json** | 0% | **85%** | 🔴 缺失 |
| **Catga.Transport.Nats** | 0% | **80%** | 🔴 缺失 |
| **Catga.Persistence.Redis** | 0% | **80%** | 🔴 缺失 |
| **Catga.AspNetCore** | 0% | **75%** | 🔴 缺失 |
| **Catga.SourceGenerator** | 0% | **80%** | 🔴 缺失 |
| **整体** | ~50% | **80%+** | 🟡 需补充 |

### 基准测试覆盖

| 测试类型 | 当前 | 目标 | 状态 |
|---------|------|------|------|
| **ID 生成** | ✅ 3 个 | ✅ 3 个 | 🟢 完成 |
| **CQRS 核心** | 0 | 1 个 | 🔴 缺失 |
| **并发性能** | 0 | 1 个 | 🔴 缺失 |
| **序列化对比** | ✅ 1 个 | ✅ 1 个 | 🟢 完成 |
| **幂等性** | 0 | 1 个 | 🔴 缺失 |
| **Pipeline** | 0 | 1 个 | 🔴 缺失 |
| **内存分配** | ✅ 1 个 | ✅ 1 个 | 🟢 完成 |
| **反射优化** | ✅ 1 个 | ✅ 1 个 | 🟢 完成 |
| **整体** | 7 个 | **12 个** | 🟡 需补充 |

---

## ⏱️ 时间估算

### 单元测试开发

| 任务 | 预估时间 | 优先级 |
|------|---------|--------|
| 序列化器测试 | 2 小时 | P0 ⭐ |
| 传输层测试 | 3 小时 | P0 ⭐ |
| 持久化层测试 | 4 小时 | P0 ⭐ |
| ASP.NET Core 测试 | 2 小时 | P1 |
| Source Generator 测试 | 3 小时 | P1 |
| **总计** | **14 小时** | |

### 基准测试开发

| 任务 | 预估时间 | 优先级 |
|------|---------|--------|
| CQRS 核心性能 | 2 小时 | P0 ⭐ |
| 并发性能 | 1.5 小时 | P0 ⭐ |
| 序列化对比 | 1.5 小时 | P1 |
| 幂等性性能 | 2 小时 | P1 |
| Pipeline 性能 | 1.5 小时 | P1 |
| **总计** | **8.5 小时** | |

### 总时间估算

- **P0 任务**: 12.5 小时 (序列化 + 传输 + 持久化 + CQRS + 并发)
- **P1 任务**: 10 小时 (ASP.NET Core + SourceGen + 其他基准测试)
- **总计**: **22.5 小时** (~3 个工作日)

---

## 🎯 执行策略

### 第 1 天: 核心组件单元测试 (P0)

**上午** (4 小时):
1. ✅ 序列化器测试 (2h)
   - MemoryPackSerializerTests.cs
   - JsonSerializerTests.cs
2. ✅ 传输层测试 - Part 1 (2h)
   - InMemoryTransportTests.cs

**下午** (4 小时):
3. ✅ 传输层测试 - Part 2 (1h)
   - NatsTransportTests.cs
4. ✅ 持久化层测试 - Part 1 (3h)
   - RedisOutboxTests.cs
   - RedisInboxTests.cs

---

### 第 2 天: 持久化 + 性能测试 (P0)

**上午** (4 小时):
1. ✅ 持久化层测试 - Part 2 (1h)
   - RedisCacheTests.cs
   - RedisLockTests.cs
2. ✅ CQRS 核心性能测试 (2h)
   - CqrsPerformanceBenchmarks.cs
3. ✅ 并发性能测试 (1.5h)
   - ConcurrencyBenchmarks.cs

**下午** (4 小时):
4. ✅ 幂等性性能测试 (2h)
   - IdempotencyStoreBenchmarks.cs
5. ✅ Pipeline 性能测试 (1.5h)
   - PipelineBehaviorBenchmarks.cs
6. ✅ 运行所有测试 + 生成报告 (0.5h)

---

### 第 3 天: P1 任务 + 完善 (可选)

**上午** (4 小时):
1. ✅ ASP.NET Core 集成测试 (2h)
2. ✅ Source Generator 测试 (2h)

**下午** (4 小时):
3. ✅ 序列化对比基准测试 (1.5h)
4. ✅ 测试覆盖率分析 (1h)
5. ✅ 文档更新 (1h)
6. ✅ 最终验证 + 提交 (0.5h)

---

## 📊 成功标准

### 单元测试

- ✅ **总覆盖率**: ≥ 80%
- ✅ **核心组件覆盖率**: ≥ 85%
- ✅ **所有测试通过**: 100%
- ✅ **测试用例数**: ≥ 250 个 (当前 111 → 目标 250+)

### 基准测试

- ✅ **基准测试套件**: ≥ 12 个 (当前 7 → 目标 12+)
- ✅ **性能指标达标**: 所有关键路径 < 1μs
- ✅ **零分配验证**: Gen0 = 0 for hot paths
- ✅ **性能报告**: HTML + Markdown 格式

### 质量指标

- ✅ **编译错误**: 0
- ✅ **警告**: < 5 (仅 IL2026/IL3050 预期警告)
- ✅ **测试稳定性**: 100% (无 flaky tests)
- ✅ **CI 就绪**: 所有测试可在 CI 中运行

---

## 🚀 立即开始

**优先执行 P0 任务** (第 1-2 天):

1. **序列化器测试** ⭐ 最关键
2. **传输层测试** ⭐ 最关键
3. **持久化层测试** ⭐ 最关键
4. **CQRS 性能测试** ⭐ 最关键
5. **并发性能测试** ⭐ 最关键

**预计完成时间**: 2 个工作日 (12.5 小时)

---

**Catga** - 迈向 100% 测试覆盖的高质量 CQRS 框架 🚀

