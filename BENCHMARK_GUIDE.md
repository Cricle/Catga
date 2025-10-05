# Catga 基准测试指南

## 🚀 快速运行

### 运行所有基准测试
```bash
dotnet run -c Release --project benchmarks/Catga.Benchmarks
```

### 运行特定基准测试
```bash
# 只运行分配优化测试
dotnet run -c Release --project benchmarks/Catga.Benchmarks --filter "*Allocation*"

# 只运行 CQRS 测试
dotnet run -c Release --project benchmarks/Catga.Benchmarks --filter "*Cqrs*"

# 只运行并发测试
dotnet run -c Release --project benchmarks/Catga.Benchmarks --filter "*Concurrency*"

# 只运行 CatGa 测试
dotnet run -c Release --project benchmarks/Catga.Benchmarks --filter "*CatGa*"
```

### 使用短任务模式（快速验证）
```bash
dotnet run -c Release --project benchmarks/Catga.Benchmarks --job short
```

---

## 📊 现有基准测试

### 1. AllocationBenchmarks
**测试目标**: 内存分配和 GC 压力

**包含测试**:
- `StringMessageId_Allocation` - 字符串方式创建 MessageId（基准）
- `StructMessageId_Allocation` - 结构体方式创建 MessageId（优化）
- `ClassResult_Allocation` - CatgaResult 类分配
- `TaskFromResult_Allocation` - Task.FromResult 分配
- `ValueTask_Allocation` - ValueTask 分配（优化）
- `ListWithCapacity_Allocation` - 预分配容量的 List
- `ListWithoutCapacity_Allocation` - 动态扩容的 List
- `ArrayPool_Usage` - ArrayPool 缓冲区重用（优化）
- `DirectArray_Allocation` - 直接数组分配
- `Dictionary_WithCapacity` - 预分配容量的 Dictionary
- `Dictionary_WithoutCapacity` - 动态扩容的 Dictionary

**关键指标**:
- Mean (平均时间)
- Gen0/Gen1/Gen2 (GC 触发次数)
- Allocated (分配内存)
- Rank (性能排名)

### 2. CqrsBenchmarks
**测试目标**: CQRS 操作性能

**包含测试**:
- `SendCommand` - 命令处理
- `SendQuery` - 查询处理
- `PublishEvent` - 事件发布
- `SendCommandWithRetry` - 带重试的命令
- `SendCommandWithValidation` - 带验证的命令

### 3. ConcurrencyBenchmarks
**测试目标**: 并发控制性能

**包含测试**:
- `NoLimit` - 无并发限制
- `WithConcurrencyLimit` - 有并发限制
- `WithRateLimiter` - 有速率限制
- `WithCircuitBreaker` - 有熔断器

### 4. CatGaBenchmarks
**测试目标**: 分布式事务性能

**包含测试**:
- `ExecuteSimpleSaga` - 简单 Saga
- `ExecuteSagaWithCompensation` - 带补偿的 Saga
- `ParallelSagaExecution` - 并行 Saga

---

## 📈 如何解读结果

### 关键指标说明

#### Mean (平均时间)
- 越小越好
- 单位: ns (纳秒), μs (微秒), ms (毫秒)
- 1 μs = 1,000 ns
- 1 ms = 1,000 μs

#### Error & StdDev (误差和标准差)
- 表示测试稳定性
- 越小说明结果越可靠

#### Ratio (相对比率)
- 相对于 Baseline 的倍数
- < 1.0 表示更快
- > 1.0 表示更慢

#### Gen0/Gen1/Gen2
- 每 1000 次操作触发的 GC 次数
- Gen0: 年轻代 GC（最频繁）
- Gen1: 中间代 GC
- Gen2: 老年代 GC（最昂贵）
- 0 表示零 GC（最优）

#### Allocated (分配内存)
- 每次操作分配的内存
- 越小越好
- 0 B 表示零分配（最优）

#### Rank (排名)
- 相对性能排名
- 1 = 最快
- 数字越大越慢

---

## 🎯 优化目标

### 零分配操作 🌟
目标: Allocated = 0 B, Gen0 = 0

**已实现**:
- ✅ StructMessageId (vs String: 96 KB → 0 B)
- ✅ ValueTask (vs Task: 72 KB → 0 B)
- ✅ ArrayPool (vs Direct: 1 MB → 0 B)

### 性能提升
目标: Mean 时间减少 > 30%

**已实现**:
- ✅ StructMessageId: -35% (86.9 μs → 56.5 μs)
- ✅ ValueTask: -96% (9.7 μs → 0.36 μs)
- ✅ ArrayPool: -90% (66.6 μs → 6.8 μs)

---

## 📊 结果示例

### 优化前 vs 优化后

```
| Method                     | Mean      | Gen0   | Allocated |
|--------------------------- |----------:|-------:|----------:|
| StringMessageId_Allocation | 86,880 ns | 11.47  |   96000 B | ← 基准
| StructMessageId_Allocation | 56,504 ns |  0.00  |       0 B | ← 优化后
```

**解读**:
- ⚡ 性能提升: 35% 更快
- 💾 内存: 100% 减少（零分配）
- 🔄 GC: 100% 消除

---

## 🔧 高级选项

### BenchmarkDotNet 参数

#### Job 配置
```bash
--job short        # 快速测试（3次迭代）
--job medium       # 中等测试（15次迭代，默认）
--job long         # 长时间测试（100次迭代）
```

#### 过滤器
```bash
--filter "*Name*"      # 名称包含 Name
--filter "Class.Method" # 特定方法
```

#### 输出格式
```bash
--exporters json       # 导出 JSON
--exporters html       # 导出 HTML 报告
--exporters markdown   # 导出 Markdown
```

#### 诊断器
```bash
--memory               # 内存诊断（默认开启）
--threading            # 线程诊断
--disasm               # 反汇编
```

---

## 📁 结果文件位置

基准测试结果保存在：
```
benchmarks/Catga.Benchmarks/BenchmarkDotNet.Artifacts/
├── results/
│   ├── Catga.Benchmarks.AllocationBenchmarks-report.html
│   ├── Catga.Benchmarks.AllocationBenchmarks-report.csv
│   └── ...
└── logs/
    └── ...
```

---

## 🎨 自定义基准测试

### 添加新的基准测试

```csharp
using BenchmarkDotNet.Attributes;

namespace Catga.Benchmarks;

[MemoryDiagnoser]
[RankColumn]
public class MyBenchmarks
{
    [Benchmark(Baseline = true)]
    public void Baseline()
    {
        // 基准实现
    }

    [Benchmark]
    public void Optimized()
    {
        // 优化实现
    }
}
```

### 配置选项

```csharp
[Config(typeof(Config))]
public class MyBenchmarks
{
    private class Config : ManualConfig
    {
        public Config()
        {
            AddJob(Job.ShortRun);
            AddDiagnoser(MemoryDiagnoser.Default);
            AddColumn(RankColumn.Arabic);
        }
    }
}
```

---

## 📊 持续监控

### 建议实践

1. **每次重大变更运行基准测试**
   ```bash
   dotnet run -c Release --project benchmarks/Catga.Benchmarks
   ```

2. **对比结果**
   - 保存每次测试结果
   - 对比性能趋势
   - 识别性能回退

3. **关注关键指标**
   - 高频操作的时间
   - GC 触发次数
   - 内存分配量

4. **设置性能阈值**
   - Mean 时间不应增加 > 10%
   - 零分配操作保持 0 B
   - GC 不应增加

---

## 🎯 性能目标

### 当前性能水平 ⭐⭐⭐⭐⭐

| 操作类型 | 目标 | 当前状态 |
|---------|------|---------|
| MessageId 创建 | < 60 μs | ✅ 56.5 μs |
| 零分配操作 | 0 B | ✅ 3 项达成 |
| GC Gen0 | 0 | ✅ 关键路径 |
| CQRS 操作 | < 100 ns | ⚠️ 待测 |
| Saga 事务 | < 5 ms | ⚠️ 待测 |

---

## 🚀 下一步

### 待测试的优化
1. **ValueTask 迁移** - 预期 96% 提升
2. **ArrayPool 应用** - 预期 90% 提升
3. **Span<T> 优化** - 预期显著减少分配

### 新增基准测试
1. 💡 序列化/反序列化性能
2. 💡 NATS 传输性能
3. 💡 Redis 持久化性能
4. 💡 Pipeline Behavior 开销

---

## 📞 问题排查

### 测试失败
```bash
# 清理并重新构建
dotnet clean
dotnet build -c Release
dotnet run -c Release --project benchmarks/Catga.Benchmarks
```

### 结果不稳定
```bash
# 使用更多迭代
dotnet run -c Release --project benchmarks/Catga.Benchmarks --job long
```

### 内存诊断器错误
```bash
# 确保以 Release 模式运行
dotnet run -c Release --project benchmarks/Catga.Benchmarks
```

---

## 📚 参考资料

- [BenchmarkDotNet 官方文档](https://benchmarkdotnet.org/)
- [.NET 性能优化指南](https://docs.microsoft.com/en-us/dotnet/core/diagnostics/performance-best-practices)
- [PERFORMANCE_BENCHMARK_RESULTS.md](./PERFORMANCE_BENCHMARK_RESULTS.md) - 详细测试结果

---

**更新日期**: 2025-10-05
**基准测试版本**: v1.0
**测试环境**: .NET 9.0, Release mode

