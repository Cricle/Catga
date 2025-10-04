# Catga 性能基准测试

## 📊 概述

使用 BenchmarkDotNet 对 Catga 进行全面的性能基准测试，包括：

- **CQRS 性能测试** - 命令、查询、事件的吞吐量和延迟
- **CatGa 性能测试** - 分布式事务的性能特征
- **并发控制测试** - ConcurrencyLimiter、IdempotencyStore、RateLimiter、CircuitBreaker

## 🚀 运行测试

### 运行所有测试

```powershell
dotnet run -c Release --project benchmarks/Catga.Benchmarks
```

### 运行特定测试

```powershell
# CQRS 测试
dotnet run -c Release --project benchmarks/Catga.Benchmarks --filter "*CqrsBenchmarks*"

# CatGa 测试
dotnet run -c Release --project benchmarks/Catga.Benchmarks --filter "*CatGaBenchmarks*"

# 并发控制测试
dotnet run -c Release --project benchmarks/Catga.Benchmarks --filter "*ConcurrencyBenchmarks*"
```

### 生成报告

```powershell
# 生成详细报告
dotnet run -c Release --project benchmarks/Catga.Benchmarks --exporters json html

# 生成内存诊断报告
dotnet run -c Release --project benchmarks/Catga.Benchmarks --memory
```

## 📈 测试场景

### 1. CQRS 测试

| 测试项 | 说明 | 操作数 |
|--------|------|--------|
| **SendCommand_Single** | 单次命令处理 | 1 |
| **SendQuery_Single** | 单次查询处理 | 1 |
| **PublishEvent_Single** | 单次事件发布 | 1 |
| **SendCommand_Batch100** | 批量命令处理 | 100 |
| **SendQuery_Batch100** | 批量查询处理 | 100 |
| **PublishEvent_Batch100** | 批量事件发布 | 100 |
| **SendCommand_HighConcurrency1000** | 高并发命令 | 1000 |

### 2. CatGa 测试

| 测试项 | 说明 | 操作数 |
|--------|------|--------|
| **ExecuteTransaction_Simple** | 单次简单事务 | 1 |
| **ExecuteTransaction_Complex** | 单次复杂事务（带补偿） | 1 |
| **ExecuteTransaction_Batch100** | 批量事务 | 100 |
| **ExecuteTransaction_HighConcurrency1000** | 高并发事务 | 1000 |
| **ExecuteTransaction_Idempotency100** | 幂等性测试 | 100 (重复) |

### 3. 并发控制测试

| 测试项 | 说明 | 操作数 |
|--------|------|--------|
| **ConcurrencyLimiter_Single** | 单次并发限制 | 1 |
| **ConcurrencyLimiter_Batch100** | 批量并发限制 | 100 |
| **IdempotencyStore_Write** | 幂等性存储写入 | 1 |
| **IdempotencyStore_Read** | 幂等性存储读取 | 1 |
| **IdempotencyStore_BatchWrite100** | 批量写入 | 100 |
| **IdempotencyStore_BatchRead100** | 批量读取 | 100 |
| **RateLimiter_TryAcquire** | 令牌桶获取 | 1 |
| **RateLimiter_BatchAcquire100** | 批量令牌获取 | 100 |
| **CircuitBreaker_Success** | 熔断器成功操作 | 1 |
| **CircuitBreaker_Batch100** | 熔断器批量操作 | 100 |

## 🎯 性能目标

### CQRS 目标

- **单次操作延迟**: < 0.1ms (P99)
- **批量吞吐量**: > 50,000 ops/s
- **高并发吞吐量**: > 30,000 ops/s

### CatGa 目标

- **简单事务延迟**: < 0.2ms (P99)
- **复杂事务延迟**: < 1ms (P99)
- **批量吞吐量**: > 20,000 txn/s
- **幂等性命中率**: 100%

### 并发控制目标

- **ConcurrencyLimiter**: > 100,000 ops/s
- **IdempotencyStore 写入**: > 80,000 ops/s
- **IdempotencyStore 读取**: > 200,000 ops/s
- **RateLimiter**: > 500,000 ops/s
- **CircuitBreaker**: > 150,000 ops/s

## 📊 报告解读

### 关键指标

- **Mean** - 平均执行时间
- **Error** - 标准误差
- **StdDev** - 标准差
- **Median** - 中位数
- **P95/P99** - 95th/99th 百分位延迟
- **Gen0/Gen1/Gen2** - GC 收集次数
- **Allocated** - 分配的内存

### 优化建议

1. **Mean < 1ms** ✅ 优秀
2. **1ms < Mean < 10ms** ⚠️ 可接受
3. **Mean > 10ms** ❌ 需要优化

4. **Allocated < 1KB** ✅ 低内存占用
5. **1KB < Allocated < 10KB** ⚠️ 中等
6. **Allocated > 10KB** ❌ 高内存占用

## 🔧 配置

### BenchmarkDotNet 配置

```csharp
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, RuntimeMoniker.Net90, warmupCount: 3, iterationCount: 10)]
```

- **MemoryDiagnoser**: 启用内存诊断
- **RunStrategy.Throughput**: 吞吐量优化模式
- **warmupCount: 3**: 预热 3 次
- **iterationCount: 10**: 迭代 10 次

### 环境要求

- **.NET 9.0** 或更高
- **Release 模式** 编译
- **关闭调试器** 运行

## 📝 注意事项

1. **必须在 Release 模式下运行**
   ```powershell
   dotnet run -c Release
   ```

2. **关闭其他应用程序**，以减少系统噪声

3. **多次运行**，确保结果稳定

4. **查看生成的报告**
   - 报告位于 `BenchmarkDotNet.Artifacts/results/`
   - HTML 报告可在浏览器中查看

## 🎉 示例输出

```
| Method                             | Mean        | Error     | StdDev    | Gen0   | Allocated |
|----------------------------------- |------------:|----------:|----------:|-------:|----------:|
| SendCommand_Single                 |    45.32 us |  0.891 us |  0.833 us | 0.0610 |     528 B |
| SendQuery_Single                   |    43.21 us |  0.847 us |  0.792 us | 0.0610 |     528 B |
| PublishEvent_Single                |    41.15 us |  0.812 us |  0.760 us | 0.0610 |     528 B |
| SendCommand_Batch100               | 4,523.45 us | 89.234 us | 83.467 us | 6.2500 |  52,800 B |
| ExecuteTransaction_Simple          |    52.34 us |  1.023 us |  0.957 us | 0.0732 |     624 B |
| ConcurrencyLimiter_Single          |     8.45 us |  0.165 us |  0.154 us | 0.0153 |     128 B |
| IdempotencyStore_Write             |     6.23 us |  0.122 us |  0.114 us | 0.0229 |     192 B |
| RateLimiter_TryAcquire             |     0.85 ns |  0.017 ns |  0.016 ns | -      |       - B |
```

## 🚀 性能优化建议

基于基准测试结果，可以进行以下优化：

1. **减少内存分配** - 使用对象池、ValueTask
2. **优化热路径** - 减少虚方法调用
3. **批量处理** - 合并多个操作
4. **异步优化** - 使用 ValueTask、ConfigureAwait(false)
5. **缓存优化** - 缓存频繁访问的数据

---

**Catga** - 高性能、高并发、AOT 友好的 CQRS 和分布式事务框架 🚀

