# ID 优化重构进度报告

## ✅ 已完成

### Phase 1: 修复 IMessage 接口
- ✅ 移除了 `IMessage.MessageId` 的默认 `Guid.NewGuid().ToString()` 实现
- ✅ 添加了 `MessageExtensions.NewMessageId()` 辅助方法
- ✅ 更新了文档，明确要求用户提供 MessageId

###  Phase 2: 修复示例和 Benchmarks
- ✅ 修复 `benchmarks/Catga.Benchmarks` 中的所有消息类型（7 个文件）
- ✅ 修复 `examples/OrderSystem.Api` 中的所有消息类型（Commands.cs, Events.cs）

##  🔄 待完成

### Phase 3: 修复单元测试（剩余 9 个文件）
需要在以下测试文件的消息类型中添加 `MessageId` 属性：

1. `tests/Catga.Tests/CatgaMediatorTests.cs`
   - `TestCommand`
   - `TestEvent`

2. `tests/Catga.Tests/Core/CatgaMediatorExtendedTests.cs`
   - `MetadataCommand`
   - `ExceptionCommand`
   - `ExceptionEvent`
   - `PerformanceCommand`
   - `PerformanceEvent`
   - `ScopedCommand`

3. `tests/Catga.Tests/Pipeline/IdempotencyBehaviorTests.cs`
   - `TestRequest`

4. `tests/Catga.Tests/Transport/InMemoryMessageTransportTests.cs`
   - `TestTransportMessage`
   - `QoS0Message`
   - `QoS1WaitMessage`

5. `tests/Catga.Tests/Integration/BasicIntegrationTests.cs`
   - `SimpleCommand`

6. `tests/Catga.Tests/Integration/QosVerificationTests.cs` (可能有)

7. `tests/Catga.Tests/Handlers/SafeRequestHandlerCustomErrorTests.cs` (可能有)

### Phase 4: 替换 Guid.NewGuid().ToString() 调用
需要在以下文件中替换 `Guid.NewGuid().ToString()` 为 `MessageExtensions.NewMessageId()` 或 `MessageExtensions.NewCorrelationId()`:

1. `src/Catga.AspNetCore/Middleware/CorrelationIdMiddleware.cs`
2. `src/Catga/Rpc/RpcClient.cs`
3. `src/Catga.InMemory/InMemoryMessageTransport.cs`
4. `src/Catga/Core/CatgaTransactionBase.cs`
5. `src/Catga.Transport.Nats/NatsMessageTransport.cs`
6. `src/Catga.Persistence.Redis/RedisDistributedLock.cs`

## 📊 统计

- **已修复**: ~15 个文件
- **待修复**: ~15 个文件
- **预计剩余时间**: 15-20 分钟

## 🎯 下一步行动

可以选择以下任一策略：

### 策略 A: 手动逐个修复（推荐）
继续用 `search_replace` 工具逐个修复测试文件，确保准确性。

### 策略 B: 批量脚本修复（快速）
创建一个 PowerShell 脚本来批量添加 `MessageId` 属性到所有 `record ... : IRequest` / `record ... : IEvent` 类型。

### 策略 C: 临时回退（保守）
如果时间紧迫，可以临时将 `IMessage.MessageId` 改回默认实现，标记为 `TODO` 以后再优化。

## 💡 建议

由于这是一个 Breaking Change，建议：
1. 先完成所有修复（策略 A 或 B）
2. 运行完整测试套件验证
3. 更新 CHANGELOG.md 说明 Breaking Change
4. 增加主版本号（如果遵循 SemVer）

**当前推荐：继续执行策略 A，逐个修复剩余文件。**

