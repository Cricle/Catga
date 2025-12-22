# E2E 测试修复状态

## 日期
2025-12-22

## 任务目标
修复 E2E 测试中 Redis 和 NATS 后端的测试隔离问题

## 已完成的工作

### 1. 测试隔离改进
- ✅ 移除了 `IAsyncLifetime` 接口，避免共享容器导致的数据污染
- ✅ 为每个测试创建独立的 Docker 容器
- ✅ 重写了所有使用共享容器的测试（Redis 和 NATS）
- ✅ 移除了所有 Skip 标记

### 2. Redis EventStore 优化
- ✅ 修改 Lua 脚本，从逐个事件追加改为批量原子追加
- ✅ 减少 Redis 往返次数，提升性能
- ✅ 提交：`6e5de44` - "fix: Redis EventStore - use single Lua script for atomic batch append"

## 当前问题

### Redis 测试失败
**问题**：`CompleteOrderFlow_Redis_ShouldWorkEndToEnd` 只读取到 2 个事件（OrderPaid 和 OrderShipped），缺少第一个 OrderCreated 事件

**可能原因**：
1. `ReadAsync` 方法的版本过滤逻辑可能有问题
2. Lua 脚本中的版本号计算可能不正确
3. Redis Streams 的 XADD 操作可能有时序问题

**建议修复方向**：
1. 检查 `ReadAsync` 中的 `fromVersion` 过滤逻辑
2. 验证 Lua 脚本中的版本号是否正确递增
3. 添加调试日志，查看实际写入和读取的事件

### NATS 测试超时
**问题**：`CompleteOrderFlow_NATS_ShouldWorkEndToEnd` 在第一次 `AppendAsync` 时调用 `GetVersionAsync` 超时（3秒）

**可能原因**：
1. NATS JetStream 的 stream 还没有创建
2. Consumer 的 `FetchAsync` 操作在等待不存在的消息
3. NATS 容器初始化时间不够（当前等待 5秒）

**建议修复方向**：
1. 在 `GetVersionAsync` 中处理 stream 不存在的情况，直接返回 -1
2. 增加 NATS 容器的初始化等待时间
3. 修改 `GetVersionAsync` 的实现，避免在 stream 不存在时调用 `FetchAsync`

## 测试结果

### 通过的测试 (7/9)
- ✅ `CompleteOrderFlow_InMemory_ShouldWorkEndToEnd`
- ✅ `SnapshotStore_InMemory_ShouldPersistAndRestore`
- ✅ `SnapshotStore_Redis_ShouldPersistAndRestore`
- ✅ `SnapshotStore_NATS_ShouldPersistAndRestore`
- ✅ `EventPublishing_InMemory_ShouldDeliverToHandlers`
- ✅ `EventPublishing_Redis_ShouldDeliverToHandlers`
- ✅ `EventPublishing_NATS_ShouldDeliverToHandlers`

### 失败的测试 (2/9)
- ❌ `CompleteOrderFlow_Redis_ShouldWorkEndToEnd` - 只读取到 2/3 个事件
- ❌ `CompleteOrderFlow_NATS_ShouldWorkEndToEnd` - GetVersionAsync 超时

## 下一步行动

1. **优先级 1**：修复 Redis `ReadAsync` 的版本过滤问题
2. **优先级 2**：修复 NATS `GetVersionAsync` 的超时问题
3. **优先级 3**：运行完整的 E2E 测试套件验证修复

## 相关文件
- `tests/Catga.Tests/E2E/CompleteBackendE2ETests.cs` - E2E 测试文件
- `src/Catga.Persistence.Redis/Stores/RedisEventStore.cs` - Redis EventStore 实现
- `src/Catga.Persistence.Nats/NatsJSEventStore.cs` - NATS EventStore 实现
