# 架构文档索引

这一组文档回答的是同一个问题：Catga 到底怎么组织核心职责。

如果你不想一上来就看大而全的架构图，建议按下面顺序读。

---

## 推荐顺序

1. [系统概览](./overview.md)
2. [整体架构总览](./ARCHITECTURE.md)
3. [模块与边界](./modules-and-boundaries.md)
4. [运行时、性能与扩展点](./runtime-and-extensibility.md)
5. [CQRS 模式](./cqrs.md)
6. [职责边界](./RESPONSIBILITY-BOUNDARY.md)
7. [依赖原则](./dependency-principles.md)

---

## 每篇文档解决什么问题

### [overview.md](./overview.md)

适合第一次看 Catga。

重点是：
- 这个框架想解决什么问题
- 核心组件有哪些
- 从应用代码到 transport / persistence 的大致路径

### [cqrs.md](./cqrs.md)

聚焦模式本身，而不是 Catga 实现细节。

适合：
- 想确认命令、查询、事件在 Catga 里分别怎么落位
- 想统一团队的 CQRS 术语

### [ARCHITECTURE.md](./ARCHITECTURE.md)

这是新的“总览入口页”。

适合：
- 快速确定应该读哪篇子文档
- 先看大图，再决定深入哪一层

### [modules-and-boundaries.md](./modules-and-boundaries.md)

适合：
- 需要理解分层、模块职责、边界归属
- 需要讨论 Catga 负责什么、不负责什么

### [runtime-and-extensibility.md](./runtime-and-extensibility.md)

适合：
- 需要理解配置架构、请求/事件流、性能和扩展点
- 需要做框架级评审或扩展实现

### [RESPONSIBILITY-BOUNDARY.md](./RESPONSIBILITY-BOUNDARY.md)

适合做边界划分和 code review。

重点是：
- 哪些职责属于业务层
- 哪些职责属于 Catga 基础设施层
- 什么事情不应该交给框架做

### [dependency-principles.md](./dependency-principles.md)

适合处理依赖方向、模块拆分和可测试性问题。

---

## 读完这一组之后看什么

- 想开始写业务代码：看 [开发指南索引](../guides/README.md)
- 想直接跑示例：看 [示例索引](../examples/README.md)
- 想评估生产接入：看 [可观测性索引](../observability/README.md) 和 [部署索引](../deployment/README.md)
