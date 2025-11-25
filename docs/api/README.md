# Catga API 参考

欢迎查看 Catga 的完整 API 文档。

## 核心概念

### 消息类型

- [IMessage](messages.md#imessage) - 所有消息的基接口
- [IRequest](messages.md#irequesttresponse) - 请求接口（命令/查询）
- [IEvent](messages.md#ievent) - 事件接口

### Mediator

- [ICatgaMediator](mediator.md) - 核心调度器接口
- [CatgaMediator](mediator.md#catgamediator) - 默认实现

### 配置与扩展

- [配置指南](../articles/configuration.md)
- [依赖注入（自动注册）](../guides/auto-di-registration.md)
- [架构总览与 Pipeline](../architecture/ARCHITECTURE.md)

### 结果类型

- [错误处理与 CatgaResult](../guides/error-handling.md)

### Pipeline Behaviors

- [架构总览与 Pipeline](../architecture/ARCHITECTURE.md#pipeline-behaviors)

### 分布式事务

- [Outbox/Inbox 模式](../patterns/DISTRIBUTED-TRANSACTION-V2.md)

## 扩展主题

- [传输与持久化（架构总览）](../architecture/overview.md)

## 快速链接

- [快速开始](../articles/getting-started.md)
- [使用示例](../examples/)

