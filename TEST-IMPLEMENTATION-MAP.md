# 测试与实现映射文档

**创建时间**: 2025-10-19  
**状态**: 测试已创建，实现已存在，需要适配  

---

## 📊 发现总结

通过验证，我们发现：
- ✅ **81 个测试用例已创建** (3,600+ lines)
- ✅ **实现类已经存在**
- ⚠️  **类名不完全匹配**，需要适配

---

## 🔄 测试与实现映射表

### Transport 层

| 测试中使用的类名 | 实际实现的类名 | 文件路径 | 状态 |
|-----------------|---------------|---------|------|
| `RedisMessageTransport` | `RedisMessageTransport` | `src/Catga.Transport.Redis/RedisMessageTransport.cs` | ✅ 完全匹配 |
| `NatsMessageTransport` | `NatsMessageTransport` | `src/Catga.Transport.Nats/NatsMessageTransport.cs` | ✅ 完全匹配 |

### Persistence 层 - Redis

| 测试中使用的类名 | 实际实现的类名 | 文件路径 | 状态 |
|-----------------|---------------|---------|------|
| `RedisEventStore` | ❌ **不存在** | - | ⚠️ 需要实现或找到替代 |
| `RedisOutboxStore` | `RedisOutboxPersistence` | `src/Catga.Persistence.Redis/Persistence/RedisOutboxPersistence.cs` | 🔄 类名不同 |
| `RedisInboxStore` | `RedisInboxPersistence` | `src/Catga.Persistence.Redis/Persistence/RedisInboxPersistence.cs` | 🔄 类名不同 |

### Persistence 层 - NATS

| 测试中使用的类名 | 实际实现的类名 | 文件路径 | 状态 |
|-----------------|---------------|---------|------|
| `NatsEventStore` | `NatsEventStore` (在Transport.Nats) | `src/Catga.Transport.Nats/NatsEventStore.cs` | ⚠️ 位置错误，应在Persistence |
| - | `NatsKVEventStore` | `src/Catga.Persistence.Nats/NatsKVEventStore.cs` | ℹ️ 可能的替代实现 |
| `NatsOutboxStore` | `NatsJSOutboxStore` | `src/Catga.Persistence.Nats/Stores/NatsJSOutboxStore.cs` | 🔄 类名不同 |
| `NatsInboxStore` | `NatsJSInboxStore` | `src/Catga.Persistence.Nats/Stores/NatsJSInboxStore.cs` | 🔄 类名不同 |

---

## 🛠️ 需要的适配工作

### 1. 更新测试以使用正确的类名 (推荐)

**优点**:
- 测试可以立即运行
- 不破坏现有实现
- 工作量小 (~30分钟)

**需要修改**:
- `RedisOutboxStoreTests.cs`: `RedisOutboxStore` → `RedisOutboxPersistence`
- `RedisInboxStoreTests.cs`: `RedisInboxStore` → `RedisInboxPersistence`
- `NatsEventStoreTests.cs`: 检查并调整为 `NatsKVEventStore`
- 添加 NATS Outbox/Inbox 测试并使用 `NatsJSOutboxStore`/`NatsJSInboxStore`

### 2. Redis EventStore 问题

**发现**: `RedisEventStore` 类不存在

**可能的解决方案**:
- **方案 A**: 创建 `RedisEventStore` 类 (~2h)
- **方案 B**: 删除该测试文件，标记为 "TODO: Future Implementation"
- **方案 C**: 检查是否有其他 Redis 事件存储实现

### 3. NATS EventStore 位置问题

**发现**: `NatsEventStore` 在 `Transport.Nats` 项目中，但应该在 `Persistence.Nats`

**建议**: 
- 测试使用 `NatsKVEventStore` (已在正确的 Persistence 项目中)
- 或将 `NatsEventStore` 移动到 `Persistence.Nats`

---

## 📝 详细适配步骤

### Phase 1: 快速修复 - 测试类名适配 (~30分钟)

#### 1.1 Redis Persistence 测试
```csharp
// tests/Catga.Tests/Persistence/RedisOutboxStoreTests.cs
- RedisOutboxStore _outboxStore
+ RedisOutboxPersistence _outboxStore

// tests/Catga.Tests/Persistence/RedisInboxStoreTests.cs
- RedisInboxStore _inboxStore
+ RedisInboxPersistence _inboxStore
```

#### 1.2 NATS Persistence 测试
```csharp
// tests/Catga.Tests/Persistence/NatsEventStoreTests.cs
- NatsEventStore _eventStore
+ NatsKVEventStore _eventStore  // 或使用正确的类

// 添加 NATS Outbox/Inbox 测试 (如果需要)
```

### Phase 2: 处理 RedisEventStore (~2h)

#### 选项 A: 实现 RedisEventStore
创建 `src/Catga.Persistence.Redis/RedisEventStore.cs`

#### 选项 B: 标记为未来功能
```csharp
// tests/Catga.Tests/Persistence/RedisEventStoreTests.cs
// TODO: Redis EventStore implementation pending
// [Fact(Skip = "Implementation not available yet")]
```

### Phase 3: 运行测试验证 (~30分钟)

1. 编译测试项目
2. 运行所有测试
3. 修复发现的接口不匹配
4. 验证覆盖率

---

## 🎯 推荐执行方案

### 快速路径 (1-2小时)

1. ✅ **更新类名映射** (30分钟)
   - 修改测试以使用正确的类名
   - `RedisOutboxPersistence`, `RedisInboxPersistence`
   - `NatsKVEventStore`, `NatsJSOutboxStore`, `NatsJSInboxStore`

2. ✅ **处理 RedisEventStore** (30分钟)
   - 标记测试为 Skip
   - 添加 TODO 注释
   - 在文档中说明未来计划

3. ✅ **编译并运行测试** (30分钟)
   - 修复编译错误
   - 运行测试套件
   - 生成覆盖率报告

4. ✅ **文档化** (30分钟)
   - 更新 TEST-AND-DOC-PLAN.md
   - 记录测试结果
   - 标记未实现的功能

### 完整路径 (8-10小时)

如果想要完整实现：
1. 实现 RedisEventStore
2. 统一命名约定
3. 补全所有测试
4. 集成测试

---

## 📊 当前测试覆盖状态

| 组件 | 测试文件 | 测试数 | 实现状态 | 可运行 |
|------|---------|--------|---------|--------|
| Redis Transport | RedisMessageTransportTests | 10 | ✅ 已实现 | ✅ 是 |
| NATS Transport | NatsMessageTransportTests | 12 | ✅ 已实现 | ✅ 是 |
| Redis EventStore | RedisEventStoreTests | 15 | ❌ 未实现 | ❌ 否 |
| Redis Outbox | RedisOutboxStoreTests | 17 | ✅ 已实现 | 🔄 需适配 |
| Redis Inbox | RedisInboxStoreTests | 16 | ✅ 已实现 | 🔄 需适配 |
| NATS EventStore | NatsEventStoreTests | 11 | ✅ 已实现 | 🔄 需适配 |
| **总计** | **6 文件** | **81 tests** | **83%** | **33%** |

---

## ✅ 结论

### 好消息
- ✅ 大部分实现已经存在 (83%)
- ✅ 主要是命名不匹配问题
- ✅ 快速适配即可运行大部分测试

### 需要处理
- ⚠️  Redis EventStore 缺失
- 🔄 4-5 个类名需要适配
- 📝 需要更新测试文件

### 建议
**立即执行快速路径** (~1-2小时):
1. 适配类名
2. 标记缺失功能
3. 运行测试
4. 生成报告

这样可以：
- ✅ 验证现有实现的质量
- ✅ 获得测试覆盖率数据
- ✅ 为未来开发提供清晰的TODO列表
- ✅ 完成当前会话的验证目标

