# 文章索引

这一组文档更偏“成体系说明”，适合连续阅读。

和 `guides/` 的区别是：
- `articles/` 更偏概念、背景、完整叙述
- `guides/` 更偏接入、配置、落地操作

---

## 推荐阅读顺序

1. [快速开始](./getting-started.md)
2. [配置指南](./configuration.md)
3. [事件溯源](./event-sourcing.md)
4. [OpenTelemetry 集成](./opentelemetry-integration.md)
5. [AOT 部署](./aot-deployment.md)

---

## 每篇文档适合什么场景

### [getting-started.md](./getting-started.md)

适合第一次接触 Catga。

### [configuration.md](./configuration.md)

适合项目接入前统一配置口径。
现在这篇已经明确区分：

- serializer
- transport
- persistence
- hosted services / health checks
- 默认生产 broker 组合

### [event-sourcing.md](./event-sourcing.md)

适合评估 Event Sourcing、Projection、Snapshot 相关能力。

### [opentelemetry-integration.md](./opentelemetry-integration.md)

适合把 tracing / metrics 快速接进现有系统。

### [aot-deployment.md](./aot-deployment.md)

适合做 Native AOT 落地时的背景阅读。

---

## 下一步看什么

- 偏实操：看 [../guides/README.md](../guides/README.md)
- 偏架构：看 [../architecture/README.md](../architecture/README.md)
- 偏上线：先看 [../deployment/broker-production-overview.md](../deployment/broker-production-overview.md)，再看 [../deployment/README.md](../deployment/README.md)
