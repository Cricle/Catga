# 性能文档索引

性能相关文档现在分成两类：

- `最新基准结果`
- `分析 / 优化 / 历史报告`

不要把这两类混着看。

---

## 先看哪篇

### 你要最新结论

先看：
- [Benchmark Results](../BENCHMARK-RESULTS.md)

这篇是当前主入口，优先反映最近一次有效 benchmark 结果。
但要注意：它回答的是 `runtime benchmark`，不是 `broker 生产选型`。

如果你要的是“Redis / RabbitMQ / NATS 到底怎么选”，先看：
- [标准 Broker 生产选型总览](../deployment/broker-production-overview.md)

### 你要优化思路

再看：
- [性能优化指南](../performance-optimization-guide.md)
- [内存优化指南](../guides/memory-optimization-guide.md)

### 你要历史背景 / 老报告

最后看：
- [PERFORMANCE-REPORT.md](../topics/history/PERFORMANCE-REPORT.md)

这篇属于历史报告，保留参考价值，但不应覆盖最新 benchmark 结论。

---

## 使用建议

- 对外引用数据时，优先引用 [Benchmark Results](../BENCHMARK-RESULTS.md)
- 对外讲生产默认路径时，不要拿 benchmark 结果直接替代 broker 选型，应该引用 [标准 Broker 生产选型总览](../deployment/broker-production-overview.md)
- 做框架对比时，配合 [MassTransit 迁移 / 对照](../guides/masstransit-migration.md) 一起看
- 做 `MassTransit vs Catga` 判断时，最好同时看 [评分、取舍与实测结论](../guides/masstransit-scorecard.md)
- 做性能调优时，不要只看数字，也要看 [性能优化指南](../performance-optimization-guide.md)
