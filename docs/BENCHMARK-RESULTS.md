# Benchmark Results

> Framework comparison section below reflects the latest run on 2026-04-29 with BenchmarkDotNet v0.14.0, Debian 12, Intel Xeon Platinum 8457C, .NET SDK 10.0.201, .NET Runtime 10.0.5.
> This file is the current benchmark source of truth for Catga. Other docs should link here instead of repeating old numbers.

## 先看边界

这份 benchmark 文档回答的是：

- Catga 运行时调用成本大概是多少
- Catga 和 MediatR / MassTransit 的同进程路径差异在哪里
- 分配、延迟、批量吞吐的相对关系是什么

这份 benchmark 文档不直接回答：

- 生产环境到底该选 Redis、RabbitMQ 还是 NATS
- 哪个 broker 的网络往返吞吐更高
- 哪条生产接入路径最适合作为默认答案

如果你要看 broker 生产选型，先跳到：

- [标准 Broker 生产选型总览](./deployment/broker-production-overview.md)
- [Redis 生产接入](./deployment/redis-production.md)
- [RabbitMQ 生产接入](./deployment/rabbitmq-production.md)
- [NATS 生产接入](./deployment/nats-production.md)

## Framework Comparison (Catga vs MediatR vs MassTransit)

### Command Performance

| Framework | Mean | Allocated | Ratio |
|-----------|------|-----------|-------|
| **Catga** | 149.72 ns | 88 B | 1.00x |
| MediatR | 96.93 ns | 288 B | 0.65x |
| MassTransit | 33,382.25 ns | 12,470 B | 223.01x |

### Event/Notification Performance

| Framework | Mean | Allocated | Ratio |
|-----------|------|-----------|-------|
| **Catga** | 87.23 ns | 64 B | 1.00x |
| MediatR | 111.54 ns | 288 B | 1.28x |

### Batch 100 Commands

| Framework | Mean | Allocated | Ratio |
|-----------|------|-----------|-------|
| **Catga** | 13.24 μs | 8,800 B | 1.00x |
| MediatR | 9.67 μs | 28,800 B | 0.73x |
| MassTransit | 1,250.85 μs | 1,224,240 B | 94.53x |

### Key Insights

- **Catga** remains the lowest-allocation option in all measured framework-comparison scenarios
- **MediatR** is faster for the single in-process command path and for the batch-100 in-process path, but with materially higher allocation than Catga
- **Catga** is faster than MediatR for event publish and keeps allocation much lower
- **MassTransit** is still substantially heavier in this mediator/request-reply benchmark and should not be read as a broker round-trip benchmark

### How To Read These Results

- 把这页当成 `runtime cost` 文档，而不是 `broker choice` 文档
- 这里的 `MassTransit` 对照主要反映 mediator / request client 路径，不代表 RabbitMQ / ASB / SQS 真实端到端 broker 性能
- 这里的 `Catga transport` 命令只是 benchmark 入口，不等同于“Redis / RabbitMQ / NATS 生产选型已经由这页决定”
- 真正做生产决策时，要把这页和 broker 文档一起看：运行时数字回答“框架本身重不重”，broker 总览回答“生产默认怎么落”

## Broker 选择与 Benchmark 的关系

可以把两类文档分开理解：

- `Benchmark Results`：回答 Catga 核心运行时是否轻、分配是否低、同进程调用成本如何
- `Broker Production Overview`：回答 Redis / RabbitMQ / NATS 在生产环境怎么选、默认答案是什么

当前建议口径：

- 想看 `性能`：先看这页
- 想看 `生产默认路径`：先看 [broker-production-overview.md](./deployment/broker-production-overview.md)
- 想做 `MassTransit vs Catga` 技术选型：把这页和 [masstransit-scorecard.md](./guides/masstransit-scorecard.md) 一起看

## Core CQRS Performance

| Operation | Mean | Allocated |
|-----------|------|-----------|
| Command | 256 ns | 88 B |
| Query | 230 ns | 32 B |
| Event (1 handler) | 146 ns | 32 B |
| Command x100 | 22.08 μs | 8,800 B |
| Event x100 | 20.16 μs | 3,200 B |

## Throughput Analysis

| Scenario | Latency | Throughput |
|----------|---------|------------|
| Single Command | 149.72 ns | **6.7M ops/sec** |
| Single Event | 87.23 ns | **11.5M ops/sec** |
| Batch 100 Commands | 13.24 μs | **7.6M ops/sec** |

## Memory Efficiency

| Framework | Command | Event | Batch 100 |
|-----------|---------|-------|-----------|
| **Catga** | 88 B | 64 B | 8,800 B |
| MediatR | 288 B | 288 B | 28,800 B |
| MassTransit | 12,470 B | - | 1,224,240 B |

## Run Benchmarks

```bash
# Framework comparison
dotnet run -c Release --framework net10.0 --project benchmarks/Catga.Benchmarks -- --filter *FrameworkComparison*

# Core CQRS
dotnet run -c Release --project benchmarks/Catga.Benchmarks -- --filter *Core*

# Transport (requires Docker)
dotnet run -c Release --project benchmarks/Catga.Benchmarks -- --filter *Transport*

# All benchmarks
dotnet run -c Release --project benchmarks/Catga.Benchmarks -- --filter *
```

## Related Docs

- [性能文档索引](./performance/README.md)
- [Catga vs MassTransit：评分、取舍与实测结论](./guides/masstransit-scorecard.md)
- [标准 Broker 生产选型总览](./deployment/broker-production-overview.md)
