# E2E 场景集合（分布式与集群）

> 目的：提供可操作的端到端验证清单，帮助你在分布式/集群环境验证可靠性、可观测性与零丢失。配套样例：
>
> - OrderSystem 示例: ../../examples/OrderSystem.Api/README.md
> - 测试辅助库: ../../src/Catga.Testing/README.md
>
---

## 1. Outbox/Inbox 最终一致性与去重

- 步骤
  - 启用 Outbox/Inbox（参考: ../patterns/DISTRIBUTED-TRANSACTION-V2.md）。
  - 在 OrderSystem 中模拟“支付成功后发布事件”，同时刻意在处理器里抛出一次异常并重试。
  - 使用 3 副本部署，观察重复投递。
- 验证
  - 业务侧以幂等键（订单号）保证“只生效一次”。
  - 无论重试/重放，聚合状态一致，外部系统无重复副作用。

## 2. NATS JetStream QoS1 重放与去重窗口

- 步骤
  - 使用 JetStream（At-Least-Once），设置适当的去重窗口。
  - 工具/脚本对同一消息 ID 连续投递 3 次。
- 验证
  - 实际 Handler 生效 1 次，另外 2 次被去重。
  - 指标/日志能看到投递次数与处理次数不一致但最终一致。

## 3. Redis Streams 消费者组再均衡与 Pending 领取

- 步骤
  - 使用 Redis Streams（消费者组）。
  - 运行 2 个消费者；处理一半时强制关闭其中一个实例，制造 Pending。
  - 让另一个实例使用 Claim 领取 Pending 并继续处理。
- 验证
  - 无消息丢失；重复交付被业务幂等吸收。
  - 指标显示 Pending→Processed 的转移。

## 4. 滚动升级与优雅停机（零丢失）

- 步骤
  - 按 Kubernetes 部署说明滚动升级（参考: ../deployment/kubernetes.md）。
  - 开启 Readiness/PreStop/Graceful Shutdown。
- 验证
  - 升级过程中无 5xx 峰值、无未确认消息遗留。
  - 退出前处理完在途请求，指标平滑。

## 5. Dead Letter Queue（DLQ）与告警

- 步骤
  - 在 Handler 中对特定输入制造不可恢复错误（例如验证失败）。
  - 配置最大重试次数，进入 DLQ。
- 验证
  - DLQ 中能查询到消息与错误原因；
  - 触发告警（Prometheus/Grafana），并能从 DLQ 选择性重放或人工处理。
  - 参考: ../production/MONITORING-GUIDE.md

## 6. 分布式追踪与调用链路

- 步骤
  - 启用 OpenTelemetry（Trace + Metrics + Logs）。
  - 一次下单全链路：API → Mediator → Pipeline → Handler → 传输/持久化 → 下游服务。
- 验证
  - Jaeger/Tempo 中能看到完整 Span 树和关键属性（消息类型、幂等键、重试次数）。
  - 参考: ../observability/DISTRIBUTED-TRACING-GUIDE.md, ../observability/JAEGER-COMPLETE-GUIDE.md

## 7. 批量处理与回压

- 步骤
  - 以 100/1000 批次进行发布与消费，限制并发度与速率。
  - 观察下游（DB/外部 API）在压力下的行为。
- 验证
  - 触发回压/限流而非崩溃；延迟上升受控，错误率可接受。
  - 参考脚本: ../../scripts/README.md, 性能文档: ../BENCHMARK-RESULTS.md

## 8. 序列化兼容性与跨服务演进

- 步骤
  - 同一消息使用 MemoryPack 与 JSON 两种序列化进行端到端互通验证。
  - 进行一次 schema 演进（新增可选字段），旧版服务仍能消费。
- 验证
  - AOT 环境下零反射、高性能；
  - 向后兼容策略生效，无反序列化异常。
  - 参考: ../guides/serialization.md

---

## 运行提示

- 弹性与限流：参阅 ../Resilience.md。
- 观测与指标：参阅 ../production/MONITORING-GUIDE.md。
- AOT/发布：参阅 ../deployment/native-aot-publishing.md。
