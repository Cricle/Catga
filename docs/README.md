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

## 按主路径读

### 路径 A: 先跑起来

1. [快速开始](./articles/getting-started.md)
2. [基础示例](./examples/basic-usage.md)
3. [配置指南](./articles/configuration.md)
4. [开发指南索引](./guides/README.md)

### 路径 B: 评估架构和能力边界

1. [架构索引](./architecture/README.md)
2. [系统概览](./architecture/overview.md)
3. [职责边界](./architecture/RESPONSIBILITY-BOUNDARY.md)
4. [MassTransit 迁移 / 对照](./guides/masstransit-migration.md)
5. [性能文档索引](./performance/README.md)

### 路径 C: 做生产接入

1. [部署索引](./deployment/README.md)
2. [标准 Broker 生产选型总览](./deployment/broker-production-overview.md)
3. [可观测性索引](./observability/README.md)
4. [监控指南](./production/MONITORING-GUIDE.md)
5. [Native AOT 发布](./deployment/native-aot-publishing.md)

---

## 补充入口

### 我要做 Flow / Saga / 长事务

- [Flow DSL](./guides/flow-dsl.md)
- [Flow DSL 最佳实践](./guides/flow-dsl-best-practices.md)
- [Flow 存储能力对照](./flow/storage-parity.md)
- [分布式事务模式](./patterns/DISTRIBUTED-TRANSACTION-V2.md)

### 我要看具体示例

- [示例索引](./examples/README.md)

### 我要翻专题或历史文档

- [topics/README.md](./topics/README.md)
- [topics/history/README.md](./topics/history/README.md)
- [cluster/README.md](./cluster/README.md)
- [development/CONTRIBUTING.md](./development/CONTRIBUTING.md)

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
