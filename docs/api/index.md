# API 索引

这一组文档只覆盖最核心的公共接口。

如果你想先看“项目怎么接入”，优先去 [../guides/README.md](../guides/README.md)；
如果你想查接口语义，再回到这里。

## 先看哪篇

1. [Mediator API](./mediator.md)
2. [Messages API](./messages.md)

## 每篇解决什么问题

### [mediator.md](./mediator.md)

适合查：

- `ICatgaMediator` 的职责
- `SendAsync` / `PublishAsync` 的调用方式
- mediator 在 DI 里的推荐接法

### [messages.md](./messages.md)

适合查：

- `IRequest<TResponse>` / `IEvent` / `IMessage` 的语义
- 消息类型该怎么设计
- AOT / serializer 相关的消息约束

## 下一步去哪

- 想看配置和接入：看 [../guides/README.md](../guides/README.md)
- 想看架构：看 [../architecture/README.md](../architecture/README.md)
