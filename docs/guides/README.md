# 开发指南索引

这一组文档解决的是“实际怎么用 Catga”。

如果 `architecture/` 更偏“为什么这样设计”，那这里更偏“项目里该怎么接”。

---

## 先读哪几篇

第一次接入 Catga，建议先读：

1. [自动 DI / 自动注册](./auto-di-registration.md)
2. [序列化](./serialization.md)
3. [错误处理](./error-handling.md)
4. [Hosting 配置](./hosting-configuration.md)
5. [分析器](./analyzers.md)

---

## 按主题分组

### 代码生成与注册

- [source-generator.md](./source-generator.md)
- [auto-di-registration.md](./auto-di-registration.md)
- [analyzers.md](./analyzers.md)
- [analyzers-rules.md](./analyzers-rules.md)
- [analyzers-configuration.md](./analyzers-configuration.md)

适合解决：
- handler / service 怎么自动注册
- 哪些约束会在编译期报错
- `CAT2004` 这类生命周期规则怎么落地

### 运行时与托管

- [hosting-configuration.md](./hosting-configuration.md)
- [hosting-migration.md](./hosting-migration.md)
- [mediator-auto-batching.md](./mediator-auto-batching.md)

适合解决：
- 宿主里怎么接入
- 后台服务、生命周期、批处理怎么配置

### 数据与序列化

- [serialization.md](./serialization.md)
- [distributed-id.md](./distributed-id.md)
- [memory-optimization-guide.md](./memory-optimization-guide.md)

适合解决：
- 消息格式、AOT 兼容、性能敏感路径

### Flow / Saga

- [flow-dsl.md](./flow-dsl.md)
- [flow-dsl-best-practices.md](./flow-dsl-best-practices.md)

适合解决：
- 流程编排
- 补偿逻辑
- 并行 / 重试 / 恢复策略

### 迁移与对比

- [masstransit-migration.md](./masstransit-migration.md)
- [masstransit-feature-mapping.md](./masstransit-feature-mapping.md)
- [masstransit-scorecard.md](./masstransit-scorecard.md)

适合解决：
- Catga 和 MassTransit 的能力对照
- 当前项目目标场景下的取舍

---

## 常见入口

### 我要查 “这个错误是什么意思”

先看：
- [analyzers.md](./analyzers.md)
- [analyzers-rules.md](./analyzers-rules.md)
- [error-handling.md](./error-handling.md)

### 我要查 “这个能力该怎么配”

先看：
- [hosting-configuration.md](./hosting-configuration.md)
- [serialization.md](./serialization.md)
- [source-generator.md](./source-generator.md)
- [analyzers-configuration.md](./analyzers-configuration.md)

### 我要查 “Flow 应该怎么写”

先看：
- [flow-dsl.md](./flow-dsl.md)
- [flow-dsl-best-practices.md](./flow-dsl-best-practices.md)

---

## 不在这一组里的文档

- 架构理解：看 [../architecture/README.md](../architecture/README.md)
- 示例代码：看 [../examples/README.md](../examples/README.md)
- 运维部署：看 [../observability/README.md](../observability/README.md) 和 [../deployment/README.md](../deployment/README.md)
