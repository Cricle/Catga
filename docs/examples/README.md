# 示例文档索引

如果你不想先看抽象概念，直接从示例开始最合适。

---

## 先看哪几篇

1. [基础示例](./basic-usage.md)
2. [端到端场景](./e2e-scenarios.md)
3. [OrderSystem 示例](../../examples/README.md)

---

## 每个示例适合什么场景

### [basic-usage.md](./basic-usage.md)

适合第一次上手。

重点是：
- command / query / event 的最小闭环
- 基本注册方式
- 最短路径运行

### [e2e-scenarios.md](./e2e-scenarios.md)

适合想看“完整交互链路”的人。

重点是：
- 业务请求如何穿过 handler、transport、persistence
- 多组件协同时的使用方式

### [OrderSystem 示例](../../examples/README.md)

适合直接跑真实项目结构。

---

## 和其他文档怎么配合

- 看不懂设计原因：回到 [../architecture/README.md](../architecture/README.md)
- 看不懂配置细节：回到 [../guides/README.md](../guides/README.md)
- 想评估生产接入：继续看 [../observability/README.md](../observability/README.md) 和 [../deployment/README.md](../deployment/README.md)
