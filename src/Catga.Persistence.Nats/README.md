# Catga.Persistence.Nats (Work In Progress)

## 状态：🚧 开发中

这个项目目前处于开发阶段，暂时从解决方案中移除，因为 NATS KeyValueStore API 的使用方式需要进一步研究。

## 问题

当前实现遇到以下 API 问题：

1. **`NatsJSContext.CreateKeyValue`** 方法不存在
   - 需要找到正确的创建 KV bucket 的方法

2. **`NatsKVContext` 构造函数** 签名不匹配
   - 当前使用 `new NatsKVContext(jsContext, bucketName)` 失败
   - 需要查找正确的初始化方式

3. **`NatsKVContext` 缺少方法**
   - `PutAsync`, `GetEntryAsync`, `GetKeysAsync`, `DeleteAsync` 等方法不存在
   - 需要使用正确的 API 来操作 KV store

## 下一步

1. 研究 `NATS.Client.KeyValueStore` 2.5.2+ 的官方文档和示例
2. 查看 NATS.NET v2 的 KeyValueStore 使用方式
3. 可能需要升级到更新版本的 NATS 客户端包
4. 或者考虑直接使用 JetStream API 而不是 KeyValueStore

## 替代方案

可以参考 `Catga.Transport.Nats` 中的 `NatsEventStore` 实现，它使用 JetStream API 工作良好。

## 架构目标

一旦 API 问题解决，这个项目将提供：

- ✅ `NatsKVEventStore` - 基于 NATS KV 的事件存储
- ✅ `NatsKVOutboxStore` - 基于 NATS KV 的 Outbox 存储
- ✅ `NatsKVInboxStore` - 基于 NATS KV 的 Inbox 存储
- ✅ 完整的 DI 扩展支持

## 贡献

如果您熟悉 NATS KeyValueStore API，欢迎贡献正确的实现！

