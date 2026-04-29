# 性能文档索引

性能相关文档现在分成两类：

- `最新基准结果`
- `分析 / 优化 / 历史报告`

不要把这两类混着看。

---

## 先读什么

### 我要最新 benchmark 结论

- [Benchmark Results](../BENCHMARK-RESULTS.md)

这篇回答的是 `runtime benchmark`，不是 broker 生产选型。

### 我要性能优化思路

- [性能优化指南](../performance-optimization-guide.md)
- [内存优化指南](../guides/memory-optimization-guide.md)

### 我要历史背景

- [PERFORMANCE-REPORT.md](../topics/history/PERFORMANCE-REPORT.md)

历史报告可以参考，但不应覆盖最新 benchmark 结论。

---

## 使用建议

- 对外引用数据时，优先引用 [Benchmark Results](../BENCHMARK-RESULTS.md)
- 对外讲生产默认路径时，不要拿 benchmark 结果直接替代 broker 选型，应该引用 [标准 Broker 生产选型总览](../deployment/broker-production-overview.md)
- 做框架对比时，配合 [MassTransit 迁移 / 对照](../guides/masstransit-migration.md) 一起看
- 做 `MassTransit vs Catga` 判断时，最好同时看 [评分、取舍与实测结论](../guides/masstransit-scorecard.md)
- 做性能调优时，不要只看数字，也要看 [性能优化指南](../performance-optimization-guide.md)
