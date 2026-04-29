# Catga 文档中心

Catga 现在的文档量已经不少，但并不是每篇都适合第一次阅读。

这份首页只做一件事：
- 告诉你先看什么
- 告诉你按什么目标去看
- 把“主线文档”和“专题/历史文档”分开

---

## 先看这里

如果你第一次接触 Catga，建议按这个顺序读：

1. [快速开始](./articles/getting-started.md)
2. [基础示例](./examples/basic-usage.md)
3. [架构总览](./architecture/README.md)
4. [开发指南索引](./guides/README.md)
5. [性能文档索引](./performance/README.md)

这 5 篇读完，再按你的实际场景继续往下钻。

---

## 按目标找文档

### 我要先跑起来

- [快速开始](./articles/getting-started.md)
- [基础示例](./examples/basic-usage.md)
- [配置指南](./articles/configuration.md)
- [示例索引](./examples/README.md)

### 我要理解 Catga 的核心设计

- [架构索引](./architecture/README.md)
- [整体架构](./architecture/ARCHITECTURE.md)
- [系统概览](./architecture/overview.md)
- [CQRS 模式](./architecture/cqrs.md)
- [职责边界](./architecture/RESPONSIBILITY-BOUNDARY.md)

### 我要做日常业务开发

- [开发指南索引](./guides/README.md)
- [自动 DI / Source Generator](./guides/auto-di-registration.md)
- [序列化](./guides/serialization.md)
- [错误处理](./guides/error-handling.md)
- [Hosting 配置](./guides/hosting-configuration.md)
- [分析器与编译期约束](./guides/analyzers.md)

### 我要做 Flow / Saga / 长事务

- [Flow DSL](./guides/flow-dsl.md)
- [Flow DSL 最佳实践](./guides/flow-dsl-best-practices.md)
- [Flow 存储能力对照](./flow/storage-parity.md)
- [分布式事务模式](./patterns/DISTRIBUTED-TRANSACTION-V2.md)

### 我要上生产

- [可观测性索引](./observability/README.md)
- [部署索引](./deployment/README.md)
- [标准 Broker 生产选型总览](./deployment/broker-production-overview.md)
- [Redis 生产接入](./deployment/redis-production.md)
- [RabbitMQ 生产接入](./deployment/rabbitmq-production.md)
- [NATS 生产接入](./deployment/nats-production.md)
- [监控指南](./production/MONITORING-GUIDE.md)
- [Native AOT 发布](./deployment/native-aot-publishing.md)
- [Kubernetes 部署](./deployment/kubernetes.md)

### 我要看性能和框架对比

- [Benchmark 结果](./BENCHMARK-RESULTS.md)
- [性能优化指南](./performance-optimization-guide.md)
- [MassTransit 迁移 / 对照](./guides/masstransit-migration.md)
- [性能文档索引](./performance/README.md)

---

## 文档分层

为了减少“同类主题分散到很多位置”的问题，后续阅读可以按这几层理解：

### 1. 入门层

- [articles/getting-started.md](./articles/getting-started.md)
- [examples/basic-usage.md](./examples/basic-usage.md)
- [articles/configuration.md](./articles/configuration.md)

### 2. 主线能力层

- [architecture/README.md](./architecture/README.md)
- [guides/README.md](./guides/README.md)
- [examples/README.md](./examples/README.md)

### 3. 运维与生产层

- [observability/README.md](./observability/README.md)
- [deployment/README.md](./deployment/README.md)
- [deployment/broker-production-overview.md](./deployment/broker-production-overview.md)
- [deployment/redis-production.md](./deployment/redis-production.md)
- [deployment/rabbitmq-production.md](./deployment/rabbitmq-production.md)
- [deployment/nats-production.md](./deployment/nats-production.md)
- [production/MONITORING-GUIDE.md](./production/MONITORING-GUIDE.md)

### 4. 专题 / 深挖 / 历史文档

这些文档不是“主线入门”，但对特定问题仍然有价值：

- [topics/README.md](./topics/README.md)
- [topics/history/README.md](./topics/history/README.md)
- [topics/flow/README.md](./topics/flow/README.md)
- [topics/read-model/README.md](./topics/read-model/README.md)
- [cluster/README.md](./cluster/README.md)
- [development/CONTRIBUTING.md](./development/CONTRIBUTING.md)

---

## 推荐阅读路径

### 路径 A: 业务应用开发者

1. [快速开始](./articles/getting-started.md)
2. [基础示例](./examples/basic-usage.md)
3. [配置指南](./articles/configuration.md)
4. [开发指南索引](./guides/README.md)

### 路径 B: 框架能力评估者

1. [架构索引](./architecture/README.md)
2. [MassTransit 迁移 / 对照](./guides/masstransit-migration.md)
3. [Benchmark 结果](./BENCHMARK-RESULTS.md)
4. [性能文档索引](./performance/README.md)
5. [可观测性索引](./observability/README.md)

### 路径 C: 生产环境接入者

1. [配置指南](./articles/configuration.md)
2. [可观测性索引](./observability/README.md)
3. [部署索引](./deployment/README.md)
4. [标准 Broker 生产选型总览](./deployment/broker-production-overview.md)
5. [Redis 生产接入](./deployment/redis-production.md)
6. [RabbitMQ 生产接入](./deployment/rabbitmq-production.md)
7. [NATS 生产接入](./deployment/nats-production.md)
8. [监控指南](./production/MONITORING-GUIDE.md)

---

## 当前整理原则

- `README.md` 只做入口，不再堆太多细节
- `README.md / 索引页` 负责导航
- 具体机制、代码示例、约束规则放回各自主题文档
- 暂时不大规模改文件名和路径，先把入口和阅读顺序理顺，避免打断已有链接
- `最新结果` 和 `历史报告` 分开，避免旧数据污染判断

如果后续继续整理，建议下一步再做：
- 合并明显重复的旧文档
- 统一命名风格
- 给“历史专题文档”单独划区
