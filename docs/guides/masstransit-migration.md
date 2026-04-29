# 从 MassTransit 迁移到 Catga

> 这篇总览页只回答三件事：
> - 先看结论
> - 去哪里看代码/概念对照
> - 去哪里看评分、取舍和 benchmark

---

## 先看结论

- 如果你要的是 `通用企业消息中间件`，优先考虑 **MassTransit**。它的 broker 生态、测试工具、运维经验和社区成熟度都更强。
- 如果你要的是 `NATS / Redis / RabbitMQ + Native AOT + Event Sourcing + Flow DSL` 这条组合，**Catga** 更贴近当前项目目标。
- 对当前仓库来说，`NATS / Redis / RabbitMQ` 这三条 transport 线已经补到可对标使用的程度：`request/reply`、`competing consumer`、`DLQ`、`priority/delay`、`context/trace propagation`、`external header interop` 基本都齐了。

现在 Catga 在生产接入上的默认路径也已经明确：

- `Redis + Redis`：默认生产答案
- `RabbitMQ + Redis`：企业标准 broker 答案
- `NATS + NATS`：一体化 broker 世界答案

如果你是在做生产选型，而不是只看 API 迁移，先看：

- [标准 Broker 生产选型总览](../deployment/broker-production-overview.md)

---

## 怎么读这组文档

### 你要看 API / 概念 / 代码迁移

看：

- [能力映射与代码对照](./masstransit-feature-mapping.md)

这页包含：
- 核心概念对照
- 基础配置
- Consumer / Handler
- 发布 / 发送
- Request/Response
- Saga / 状态机
- Fault
- Competing Consumer
- SendLater
- 消息版本化
- 授权
- 测试

### 你要看评分 / 取舍 / benchmark

看：

- [评分、取舍与实测结论](./masstransit-scorecard.md)

这页包含：
- 能力矩阵
- 实测性能对比
- 综合评分
- Catga / MassTransit 各自优势与短板
- 参考资料

---

## 评分口径

- 评分使用 `10 分制`
- 这一版保留的核心分差主要是：
  - `Broker / Transport 生态广度`
  - `NATS / Redis / RabbitMQ` 目标贴合度
- 按当前项目要求，**除 transport / broker 生态相关维度外，其它功能分数按“与 MassTransit 持平”处理**

---

## 推荐阅读顺序

### 路径 A：准备迁移现有 MassTransit 项目

1. [能力映射与代码对照](./masstransit-feature-mapping.md)
2. [评分、取舍与实测结论](./masstransit-scorecard.md)

### 路径 B：只是要做技术选型

1. [评分、取舍与实测结论](./masstransit-scorecard.md)
2. [能力映射与代码对照](./masstransit-feature-mapping.md)
3. [Benchmark Results](../BENCHMARK-RESULTS.md)

---

## 相关文档

- [Benchmark Results](../BENCHMARK-RESULTS.md)
- [性能索引](../performance/README.md)
- [标准 Broker 生产选型总览](../deployment/broker-production-overview.md)
- [Redis 生产接入](../deployment/redis-production.md)
- [RabbitMQ 生产接入](../deployment/rabbitmq-production.md)
- [NATS 生产接入](../deployment/nats-production.md)
- [Flow DSL](./flow-dsl.md)
- [架构索引](../architecture/README.md)
