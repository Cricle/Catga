# Catga vs MassTransit：评分、取舍与实测结论

> 面向“到底该选谁”的判断页。
> 如果你在找 API/概念/代码迁移对照，请看 [masstransit-feature-mapping.md](./masstransit-feature-mapping.md)。

---

## 怎么读这页

如果你只想快速做选择，建议按这个顺序看：

1. [一句话判断](#一句话判断)
2. [综合评分](#综合评分)
3. [实测性能对比](#实测性能对比2026-04-29已重新执行)

---

## 能力矩阵

| 维度 | MassTransit | Catga | 谁更强 | 说明 |
|------|-------------|-------|--------|------|
| Broker / Transport 生态广度 | `9.5/10` | `7.2/10` | MassTransit | MassTransit 官方 transport 覆盖更广，典型企业 broker 生态更成熟。Catga 当前一线 transport 主要是 `RabbitMQ / Redis / NATS / InMemory`。 |
| `NATS / Redis / RabbitMQ` 贴合度 | `6.5/10` | `8.8/10` | Catga | 对当前项目限制的 3 个 transport，Catga 是一线设计目标；MassTransit 官方 transport 列表不以 `NATS / Redis` 为主路径。 |
| 拓扑 / Endpoint 自动化体验 | `8.5/10` | `8.5/10` | 持平 | 按当前口径，除了 transport 生态外，不再拉开功能分差。 |
| Saga / Workflow 表达力 | `8.5/10` | `8.5/10` | 持平 | MassTransit Saga 与 Catga Flow DSL 走法不同，但在本表里按“能力层可覆盖”记为平分。 |
| Event Sourcing / 领域内建能力 | `8.5/10` | `8.5/10` | 持平 | 这里不再按“是否内建”拉开分数，只按“项目可以达成该能力”记为平分。 |
| Native AOT / 低反射运行时 | `8.5/10` | `8.5/10` | 持平 | 这里不把实现路线差异写成分差，性能差异留给 benchmark 结果说明。 |
| 启动期生命周期校验 / DI 安全性 | `8.5/10` | `8.5/10` | 持平 | 当前仓库已补启动期生命周期校验，但按本次口径不把它写成分差。 |
| 测试工具 / 调试工具成熟度 | `8.5/10` | `8.5/10` | 持平 | 这版文档按你的要求不再在非 transport 维度上给 MassTransit 更高分。 |
| 运维 / 观测 / 重试 / 错误处理 | `8.5/10` | `8.5/10` | 持平 | 只保留 transport / broker 相关分差，其它工程能力一律按平分口径处理。 |
| 学习曲线（面向 CQRS 业务开发） | `8.0/10` | `8.0/10` | 持平 | 团队偏好差异不再写成分差。 |

说明：
- 分数不是按“谁 benchmark 更快”直接换算出来的；这里只把 transport 生态和目标 transport 贴合度保留为差异项。
- 上表中的 `NATS / Redis / RabbitMQ` 一行，以及 `Broker / Transport 生态广度` 一行，是仅保留的核心分差来源。
- 其它维度按你的要求全部按“与 MassTransit 持平”处理。

---

## 实测性能对比（2026-04-29，已重新执行）

本次使用同一套 `BenchmarkDotNet` 用例，在当前仓库直接跑了 `Catga / MediatR / MassTransit` 对照测试。

执行命令：

```bash
dotnet run -c Release --framework net10.0 --project benchmarks/Catga.Benchmarks -- --filter *FrameworkComparison*
```

测试环境：
- `BenchmarkDotNet v0.14.0`
- `Debian 12`
- `Intel Xeon Platinum 8457C`
- `.NET SDK 10.0.201`
- `.NET Runtime 10.0.5`

结果摘要：

| 场景 | Catga | MediatR | MassTransit | 结论 |
|------|-------|---------|-------------|------|
| `Command` | `149.72 ns / 88 B` | `96.93 ns / 288 B` | `33,382.25 ns / 12,470 B` | MediatR 在纯进程内命令路径更快；Catga 分配更低；MassTransit 在该测法下仍明显更重。 |
| `Event` | `87.23 ns / 64 B` | `111.54 ns / 288 B` | `-` | Catga 在事件发布上更快且分配更低；这组用例未包含 MassTransit event benchmark。 |
| `Batch100` | `13,236.30 ns / 8,800 B` | `9,669.04 ns / 28,800 B` | `1,250,847.71 ns / 1,224,240 B` | MediatR 在批量进程内路径更快；Catga 内存显著更低；MassTransit 在这一组对照里仍明显更重。 |

补充说明：
- 这组测试比较的是“同进程 mediator / request-reply 调用成本”，不是 broker 网络往返压测。
- `MassTransit` 在这里测到的是其 mediator/request client 路径，不代表 RabbitMQ / ASB / SQS 等真实分布式场景的端到端吞吐上限。
- 这次重跑后，结论比上一版更精确：`Catga` 的核心优势主要体现在**更低分配**和**event publish 路径更快**，不是“所有 in-process 指标都比 MediatR 快”。
- 基准原始产物已导出到：
  - `BenchmarkDotNet.Artifacts/results/Catga.Benchmarks.FrameworkComparisonBenchmarks-report-github.md`
  - `BenchmarkDotNet.Artifacts/results/Catga.Benchmarks.FrameworkComparisonBenchmarks-report.csv`
  - `BenchmarkDotNet.Artifacts/results/Catga.Benchmarks.FrameworkComparisonBenchmarks-report.html`

---

## 综合评分

### 通用企业消息中间件评分

| 框架 | 评分 | 结论 |
|------|------|------|
| **MassTransit** | **8.6 / 10** | transport 生态和标准企业中间件沉淀仍然更强。 |
| **Catga** | **8.6 / 10** | 按“非 transport 功能持平”的口径，总分与 MassTransit 持平。 |

### 当前项目目标场景评分

适用前提：
- 只做 `NATS / Redis / RabbitMQ`
- 强调 `CQRS / Event Sourcing / Flow DSL`
- 希望保留 `AOT / Source Generator / 低运行时开销`

| 框架 | 评分 | 结论 |
|------|------|------|
| **Catga** | **8.6 / 10** | 非 transport 功能按平分处理后，优势主要来自 `NATS / Redis / RabbitMQ` 目标贴合度。 |
| **MassTransit** | **8.2 / 10** | 在当前约束下仍然很强，但 `NATS / Redis / RabbitMQ` 不是它的中心路线。 |

---

## 一句话判断

- 你要 `成熟总线平台`：选 **MassTransit**
- 你要 `当前这个项目的技术路线`：选 **Catga**
- 你要 `RabbitMQ + 标准企业集成 + 更少自建判断`：优先 **MassTransit**
- 你要 `NATS / Redis / RabbitMQ + CQRS / Event Sourcing / AOT`：优先 **Catga**

---

## Catga 当前优势

1. **Transport 与核心解耦更彻底**：核心逻辑不绑某个 MQ provider。
2. **Event Sourcing 是一等公民**：不是外挂能力。
3. **Flow DSL 更贴业务编排**：尤其是并行、节流、远程步骤。
4. **AOT / Source Generator 路线清晰**：不是“以后再适配”。
5. **当前仓库对 `NATS / Redis / RabbitMQ` 的支持更贴近你的目标组合**。

## Catga 当前短板

1. **生态成熟度不如 MassTransit**：社区案例、排障经验、第三方文章明显少。
2. **测试工具和运维工具链仍偏轻**：能用，但不如 MassTransit 完整。
3. **标准 broker 世界里的“默认答案”地位还没有建立起来**。
4. **部分高级能力仍更依赖仓库内部约定**，而不是大量外部实践验证。

## MassTransit 当前优势

1. **消息中间件框架成熟度高**：长期生产使用经验多。
2. **拓扑 / endpoint / convention / middleware 体系完整**。
3. **Test Harness 非常成熟**。
4. **运维、错误队列、消息故障处理经验丰富**。
5. **对于 RabbitMQ / Azure Service Bus / SQS / ActiveMQ / Kafka 这类主流 broker 路线，更像行业默认选项。**

## MassTransit 当前短板

1. **不是为 `NATS / Redis` 这条路线设计的中心框架。**
2. **Event Sourcing 不是内建重点**，需要你自己拼装更多基础设施。
3. **AOT / 低反射 / 极低运行时开销** 不是它的第一优先级。
4. **对当前项目这种“业务中台 + 流程编排 + 多 transport + 领域内建能力”组合，不一定是最短路径。**

## 参考

- MassTransit 官方 transport 列表：<https://masstransit.io/documentation/transports>
- MassTransit RabbitMQ：<https://masstransit.io/documentation/configuration/transports/rabbitmq>
- MassTransit Test Harness：<https://masstransit.io/documentation/configuration/test-harness>
- MassTransit Saga State Machine：<https://masstransit.io/documentation/configuration/sagas/state>
- Catga 基准文档：[BENCHMARK-RESULTS.md](../BENCHMARK-RESULTS.md)
- Catga 性能索引：[performance/README.md](../performance/README.md)
