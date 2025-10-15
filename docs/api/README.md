# Catga API 参考

欢迎查看 Catga 的完整 API 文档。

## 核心概念

### 消息类型

- [IMessage](messages.md#imessage) - 所有消息的基接口
- [IRequest](messages.md#irequest) - 请求接口（命令/查询）
- [IEvent](messages.md#ievent) - 事件接口
- [IEvent](messages.md#ievent) - 事件接口

### 处理器

- [IRequestHandler](handlers.md#irequesthandler) - 请求处理器接口
- [IEventHandler](handlers.md#ieventhandler) - 事件处理器接口

### Mediator

- [ICatgaMediator](mediator.md) - 核心调度器接口
- [CatgaMediator](mediator.md#catgamediator) - 默认实现

### 结果类型

- [CatgaResult&lt;T&gt;](results.md) - 泛型结果类型
- [CatgaResult](results.md#non-generic) - 非泛型结果类型

### Pipeline Behaviors

- [IPipelineBehavior](pipeline.md) - Pipeline 行为接口
- [LoggingBehavior](pipeline.md#logging) - 日志记录
- [TracingBehavior](pipeline.md#tracing) - 分布式追踪
- [IdempotencyBehavior](pipeline.md#idempotency) - 幂等性
- [ValidationBehavior](pipeline.md#validation) - 验证
- [RetryBehavior](pipeline.md#retry) - 重试

### CatGa (Saga)

- [ICatGaTransaction](catga.md) - Saga 事务接口
- [ICatGaExecutor](catga.md#executor) - Saga 执行器
- [CatGaContext](catga.md#context) - Saga 上下文

### 配置

- [CatgaOptions](configuration.md) - 核心配置选项
- [依赖注入](dependency-injection.md) - DI 配置方法

## 扩展包

### Catga.Nats

- [NATS 集成](../guides/nats-integration.md)
- [NatsCatgaMediator](nats/mediator.md)
- [NatsTransitServiceCollectionExtensions](nats/configuration.md)

### Catga.Redis

- [Redis 集成](../guides/redis-integration.md)
- [RedisIdempotencyStore](redis/idempotency.md)
- [RedisCatGaStore](redis/saga-store.md)

## 快速链接

- [快速开始](../guides/quick-start.md)
- [使用示例](../examples/)
- [常见问题](../faq.md)

