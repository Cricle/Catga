# 标准 Broker 生产选型总览

这篇文档只回答一个问题：

在 Catga 里进入生产时，`Redis`、`RabbitMQ`、`NATS` 到底该怎么选，默认答案是什么。

如果你不想先读很多细节文档，先看这一篇。

## 先给结论

Catga 当前推荐顺序是：

1. `Redis 传输 + Redis 持久化`：默认生产答案
2. `RabbitMQ 传输 + Redis 持久化`：企业标准 broker 答案
3. `NATS 传输 + NATS 持久化`：一体化 broker 世界答案

如果你只需要一句话版本：

- 想要最务实、最容易落地：选 Redis
- 组织里 RabbitMQ 已经是标准：选 RabbitMQ + Redis
- 想把传输和状态尽量统一在一套 broker 体系：选 NATS

## 为什么是这个顺序

### 1. Redis 传输 + Redis 持久化

这是 Catga 当前最适合成为“默认答案”的方案。

原因很直接：

- 基础设施普及率高
- 团队接受成本最低
- 传输和持久化能力都已经齐
- 不需要为了 Catga 再引入一套更重的 broker 世界
- 文档、接入方式、组合路径都最短

适合：

- 标准业务系统
- 希望快速生产落地的团队
- 希望减少组件数量和运维复杂度的场景

不适合：

- 强依赖 RabbitMQ 交换机/路由/优先级/延迟插件语义
- 已经明确要用 JetStream 统一传输和事件存储

对应文档：

- [Redis 生产接入](./redis-production.md)

### 2. RabbitMQ 传输 + Redis 持久化

这是 Catga 面向“标准企业 broker 世界”的主推荐方案。

原因：

- RabbitMQ 在很多组织里本来就是默认 broker
- Catga 的 RabbitMQ transport 已经覆盖了主流生产诉求
- Redis persistence 可以把 Outbox / Inbox / EventStore / Flow 状态补齐
- 这套组合既保留 RabbitMQ 的组织惯性，也避免 RabbitMQ 持久化能力缺口

适合：

- RabbitMQ 已经是平台标准
- 运维、告警、容量、权限模型都围绕 RabbitMQ 建好了
- 你需要共享队列竞争消费、优先级队列、延迟交换机等 RabbitMQ broker 语义

不适合：

- 你想把传输和状态统一到一套 broker 存储语义里
- 你明确不想维护 RabbitMQ 和 Redis 两套基础设施

对应文档：

- [RabbitMQ 生产接入](./rabbitmq-production.md)
- [Redis 生产接入](./redis-production.md)

### 3. NATS 传输 + NATS 持久化

这是 Catga 当前最完整的“一体化 broker 世界”方案。

原因：

- NATS transport 和 NATS persistence 都已落地
- JetStream / KV 能承接 EventStore、Outbox、Inbox、Flow、Snapshot 等状态能力
- 对想减少“broker 一套、状态一套”分裂的团队非常自然

适合：

- 团队已经有 NATS / JetStream 基础设施
- 更偏向云原生、高吞吐、较轻量的 broker 体系
- 明确希望用一套 broker 世界完成传输和核心状态持久化

不适合：

- 团队没有 NATS 运维经验
- 你真正需要的是 RabbitMQ 那套更传统的消息模型
- 你只是想找一个接受度最高的默认生产后端

对应文档：

- [NATS 生产接入](./nats-production.md)

## 推荐矩阵

| 方案 | 默认推荐度 | 传输能力 | 持久化完整度 | 基础设施接受度 | 组织标准适配 | 结论 |
|------|------|------|------|------|------|------|
| Redis + Redis | 最高 | 高 | 高 | 最高 | 高 | 默认生产答案 |
| RabbitMQ + Redis | 很高 | 很高 | 高 | 很高 | 最高 | 企业标准 broker 答案 |
| NATS + NATS | 很高 | 高 | 很高 | 中 | 中高 | 一体化 broker 世界答案 |
| RabbitMQ only | 中 | 很高 | 低 | 很高 | 高 | 适合只做传输，不适合作为默认完整答案 |
| NATS only | 中 | 高 | 低 | 中 | 中 | 适合只做传输，不适合作为默认完整答案 |

## 按问题选

### 问题 1：我要不要把 Redis 作为默认答案

要。

在 Catga 当前阶段，Redis 最适合承担这个角色，因为它同时满足：

- 成本低
- 接法短
- 说服成本低
- 生产能力完整

### 问题 2：如果客户或组织已经标准化 RabbitMQ 怎么办

不要硬推“全改 Redis”。

直接给出 `RabbitMQ 传输 + Redis 持久化`，这才是更现实的答案：

- broker 不动
- 业务接入成本低
- Catga 可靠性能力还能补齐

### 问题 3：什么时候主推 NATS

只在这两种情况下主推：

- 团队已经有 NATS / JetStream 平台
- 团队明确追求“一体化 broker 世界”，愿意按 NATS 方式组织传输和状态

否则不要把 NATS 当第一默认答案。

## 反选规则

有些情况不该继续让用户自己选，而应该直接排除：

### 优先排除 Redis 默认答案的情况

- 组织已经有强绑定的 RabbitMQ 平台规范
- 业务强依赖优先级队列、延迟交换机、复杂 routing key 设计

### 优先排除 RabbitMQ 路线的情况

- 团队不想维护两套基础设施
- 团队更想让 broker 同时承接状态存储

### 优先排除 NATS 路线的情况

- 没有 JetStream 运维能力
- 组织对 NATS 接受度还不高
- 当前目标是建立“默认答案”，而不是建立“新平台范式”

## 对外表达建议

如果你要在文档、对比材料、评估讨论里对外统一口径，建议直接这么说：

- `Redis` 是 Catga 当前最务实的默认生产后端
- `RabbitMQ + Redis` 是 Catga 面向企业标准 broker 场景的主推荐
- `NATS` 是 Catga 面向一体化 broker 世界的高级推荐

这样说的好处是：

- 不回避当前能力边界
- 不把三个答案讲成同一个层级
- 能让读者快速形成稳定决策

## 继续往下看

如果你已经有明确方向，直接跳到对应文档：

1. [Redis 生产接入](./redis-production.md)
2. [RabbitMQ 生产接入](./rabbitmq-production.md)
3. [NATS 生产接入](./nats-production.md)
