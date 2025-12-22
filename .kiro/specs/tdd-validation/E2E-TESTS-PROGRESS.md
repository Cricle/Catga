# E2E 测试进度报告

## 任务概述
创建完整的 E2E 测试，覆盖 InMemory、Redis 和 NATS 三种后端，测试完整的 CQRS + Event Sourcing 工作流。

## 完成的工作

### 1. 创建 E2E 测试框架 ✅
- **文件**: `tests/Catga.Tests/E2E/CompleteBackendE2ETests.cs`
- **测试数量**: 12 个测试方法
- **覆盖范围**:
  - 3 个完整订单流程测试（InMemory、Redis、NATS）
  - 3 个 Snapshot Store 测试（InMemory、Redis、NATS）
  - 3 个事件发布测试（InMemory、Redis、NATS）
  - 3 个测试模型和处理器

### 2. 修复编译错误 ✅
- **MemoryPack 嵌套类问题**: 将所有 MemoryPackable 类型从嵌套类移到命名空间级别
- **Snapshot Store API**: 修复 `LoadAsync` 返回 `Snapshot<T>?` 的属性访问（需要 `.State`）
- **EventStore API**: 修复 `AppendAsync` 参数顺序（添加 `expectedVersion: -1`）
- **EventStore 返回类型**: 修复 `ReadAsync` 返回 `StoredEvent` 的访问（需要 `.Event` 属性）

### 3. 修复运行时错误 ✅
- **NATS Snapshot Store 序列化**: 为 `NatsSnapshotStore.StoredSnapshot` 添加 `[MemoryPackable]` 属性
- **事件访问**: 修复所有测试中的事件类型断言（从 `events.Events[i]` 改为 `events.Events[i].Event`）

## 测试结果

### 当前状态
- **总测试数**: 9
- **通过**: 7 ✅
- **失败**: 2 ❌
- **跳过**: 0

### 通过的测试 ✅
1. `CompleteOrderFlow_InMemory_ShouldWorkEndToEnd` - InMemory 完整订单流程
2. `SnapshotStore_InMemory_ShouldPersistAndRestore` - InMemory Snapshot 持久化
3. `SnapshotStore_Redis_ShouldPersistAndRestore` - Redis Snapshot 持久化
4. `SnapshotStore_NATS_ShouldPersistAndRestore` - NATS Snapshot 持久化
5. `EventPublishing_InMemory_ShouldDeliverToHandlers` - InMemory 事件发布
6. `EventPublishing_Redis_ShouldDeliverToHandlers` - Redis 事件发布
7. `EventPublishing_NATS_ShouldDeliverToHandlers` - NATS 事件发布

### 失败的测试 ❌

#### 1. `CompleteOrderFlow_Redis_ShouldWorkEndToEnd`
**问题**: 只读取到 2 个事件（OrderPaidEvent 和 OrderShippedEvent），缺少第一个 OrderCreatedEvent

**错误信息**:
```
Expected events.Events to contain 3 item(s), but found 2:
{
    StoredEvent { Event = E2EOrderPaidEvent, Version = 0L },
    StoredEvent { Event = E2EOrderShippedEvent, Version = 1L }
}
```

**可能原因**:
- Redis EventStore 的 `AppendAsync` 可能有并发问题
- 第一个事件可能被覆盖而不是追加
- Version 从 0 开始，说明第一个事件确实丢失了

**建议修复**:
- 检查 `RedisEventStore.AppendAsync` 的实现
- 确认 Redis Stream 的 XADD 命令是否正确
- 添加日志查看事件追加过程

#### 2. `CompleteOrderFlow_NATS_ShouldWorkEndToEnd`
**问题**: NATS JetStream `PublishAsync` 操作超时（3秒）

**错误信息**:
```
Polly.Timeout.TimeoutRejectedException : The operation didn't complete within the allowed timeout of '00:00:03'.
---- System.Threading.Tasks.TaskCanceledException : A task was canceled.
```

**失败位置**: `E2EPayOrderCommandHandler.HandleAsync` 调用 `AppendAsync` 时

**可能原因**:
- NATS JetStream 等待 ACK 超时
- Stream 配置问题（可能需要更大的超时）
- 网络或 Docker 容器问题
- 第二次 Append 可能触发了某些限制

**建议修复**:
- 增加 Resilience Pipeline 的超时时间
- 检查 NATS JetStream 的配置
- 添加重试策略
- 检查是否需要等待 Stream 完全初始化

## 技术细节

### 测试架构
```
CompleteBackendE2ETests
├── InitializeAsync() - 启动 Redis 和 NATS 容器
├── DisposeAsync() - 清理容器
├── 完整订单流程测试 (3个)
│   ├── 创建订单 (CreateOrderCommand)
│   ├── 支付订单 (PayOrderCommand)
│   ├── 发货订单 (ShipOrderCommand)
│   └── 查询订单 (GetOrderQuery)
├── Snapshot Store 测试 (3个)
│   ├── SaveAsync
│   └── LoadAsync
└── 事件发布测试 (3个)
    ├── PublishAsync
    └── EventHandler 接收验证
```

### 测试模型
- `E2ECreateOrderCommand` / `E2EOrderCreatedResult`
- `E2EPayOrderCommand`
- `E2EShipOrderCommand`
- `E2EGetOrderQuery` / `E2EOrderDto`
- `E2EOrderCreatedEvent` / `E2EOrderPaidEvent` / `E2EOrderShippedEvent`
- `E2EOrderSnapshot`

### 处理器
- `E2ECreateOrderCommandHandler`
- `E2EPayOrderCommandHandler`
- `E2EShipOrderCommandHandler`
- `E2EGetOrderQueryHandler`
- `E2EOrderCreatedEventHandler`

## 下一步行动

### 优先级 1: 修复 Redis 事件丢失
1. 检查 `RedisEventStore.AppendAsync` 实现
2. 添加调试日志
3. 验证 Redis Stream XADD 命令
4. 确认 Version 管理逻辑

### 优先级 2: 修复 NATS 超时
1. 增加超时配置
2. 检查 JetStream 配置
3. 添加重试策略
4. 验证 Stream 初始化

### 优先级 3: 完善测试
1. 添加更多边界情况测试
2. 添加并发测试
3. 添加性能基准测试
4. 添加错误处理测试

## 提交记录
1. `9279296` - feat: 添加完整的 E2E 测试覆盖 InMemory/Redis/NATS 三种后端
2. `0ee09e7` - fix: 修复 NATS Snapshot Store 序列化问题和事件访问问题

## 总结
E2E 测试框架已经建立完成，77.8% 的测试通过（7/9）。主要问题集中在 Redis 和 NATS 后端的特定场景，需要进一步调查和修复。测试框架本身是健壮的，可以用于 TDD 方式继续开发和修复。
