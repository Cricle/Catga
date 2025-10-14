# Catga 完整测试覆盖计划

## 📊 当前测试覆盖现状

### ✅ 已完成 (55% 整体覆盖率)

| 模块 | 测试文件数 | 测试用例数 | 覆盖率 | 状态 |
|------|-----------|-----------|--------|------|
| **Catga (核心)** | 8 | ~60 | ~70% | 🟢 良好 |
| **Catga.InMemory** | 9 | ~50 | ~75% | 🟢 优秀 |
| **Pipeline Behaviors** | 4 | ~30 | ~80% | 🟢 优秀 |
| **分布式 ID** | 3 | ~20 | ~90% | 🟢 优秀 |
| **基准测试** | 9 套件 | 70 个 | N/A | 🟢 完整 |

### ❌ 缺失覆盖 (0% 覆盖率)

| 模块 | 关键类数 | 优先级 | 预估时间 |
|------|---------|--------|---------|
| **Catga.Serialization.MemoryPack** | 1 | P0 ⭐ | 2 小时 |
| **Catga.Serialization.Json** | 1 | P0 ⭐ | 2 小时 |
| **Catga.Transport.Nats** | 1 | P0 ⭐ | 3 小时 |
| **Catga.Persistence.Redis** | 9 | P0 ⭐ | 6 小时 |
| **Catga.AspNetCore** | 4 | P1 | 3 小时 |
| **Catga.SourceGenerator** | 7 | P1 | 4 小时 |

---

## 🎯 完整覆盖目标

### 目标覆盖率

| 层级 | 当前 | 目标 | 增量 |
|------|------|------|------|
| **核心层** | 70% | **90%** | +20% |
| **序列化层** | 0% | **85%** | +85% |
| **传输层** | 75% | **85%** | +10% |
| **持久化层** | 0% | **80%** | +80% |
| **集成层** | 0% | **75%** | +75% |
| **整体** | 55% | **85%** | +30% |

### 测试用例目标

| 类型 | 当前 | 目标 | 增量 |
|------|------|------|------|
| **单元测试** | 136 | **300+** | +164 |
| **集成测试** | 0 | **20+** | +20 |
| **基准测试** | 70 | **90+** | +20 |
| **总计** | 206 | **410+** | +204 |

---

## 📋 详细测试计划

## 阶段 1: 序列化器测试 (P0) ⭐

### 1.1 MemoryPackMessageSerializer 测试

**文件**: `tests/Catga.Tests/Serialization/MemoryPackMessageSerializerTests.cs`

**测试用例** (15 个):

```csharp
public class MemoryPackMessageSerializerTests
{
    // 基础功能测试 (5 个)
    [Fact] void Serialize_SimpleObject_ShouldReturnBytes()
    [Fact] void Deserialize_ValidBytes_ShouldReturnObject()
    [Fact] void RoundTrip_ComplexObject_ShouldPreserveData()
    [Fact] void Serialize_NullValue_ShouldHandleGracefully()
    [Fact] void Deserialize_EmptyBytes_ShouldThrowException()

    // 复杂对象测试 (3 个)
    [Fact] void Serialize_NestedObject_ShouldWork()
    [Fact] void Serialize_CollectionObject_ShouldWork()
    [Fact] void Serialize_GenericObject_ShouldWork()

    // 性能测试 (3 个)
    [Fact] void Serialize_LargeObject_ShouldBeEfficient()
    [Fact] void Deserialize_LargeObject_ShouldBeEfficient()
    [Fact] void Serialize_10K_Objects_ShouldBeUnder100ms()

    // 并发测试 (2 个)
    [Fact] void Serialize_Concurrent_ShouldBeThreadSafe()
    [Fact] void Deserialize_Concurrent_ShouldBeThreadSafe()

    // 错误处理测试 (2 个)
    [Fact] void Deserialize_CorruptedData_ShouldThrowException()
    [Fact] void Serialize_UnsupportedType_ShouldThrowException()
}
```

**测试数据**:
```csharp
[MemoryPackable]
public partial record TestMessage(int Id, string Name, DateTime Timestamp);

[MemoryPackable]
public partial record ComplexMessage(
    int Id,
    string Name,
    List<string> Tags,
    Dictionary<string, object> Metadata,
    NestedData Nested
);

[MemoryPackable]
public partial record NestedData(int Value, string Description);
```

**预估时间**: 2 小时
**覆盖率目标**: 90%+

---

### 1.2 JsonMessageSerializer 测试

**文件**: `tests/Catga.Tests/Serialization/JsonMessageSerializerTests.cs`

**测试用例** (15 个):

```csharp
public class JsonMessageSerializerTests
{
    // 基础功能测试 (5 个)
    [Fact] void Serialize_SimpleObject_ShouldReturnBytes()
    [Fact] void Deserialize_ValidJson_ShouldReturnObject()
    [Fact] void RoundTrip_ComplexObject_ShouldPreserveData()
    [Fact] void Serialize_WithCustomOptions_ShouldRespectOptions()
    [Fact] void Deserialize_WithJsonSerializerContext_ShouldWork()

    // UTF-8 编码测试 (2 个)
    [Fact] void Serialize_UnicodeString_ShouldHandleCorrectly()
    [Fact] void Deserialize_Utf8Bytes_ShouldDecodeCorrectly()

    // 性能测试 (3 个)
    [Fact] void Serialize_WithArrayPool_ShouldReduceAllocations()
    [Fact] void Deserialize_LargeJson_ShouldBeEfficient()
    [Fact] void Serialize_10K_Objects_ShouldBeUnder500ms()

    // 并发测试 (2 个)
    [Fact] void Serialize_Concurrent_ShouldBeThreadSafe()
    [Fact] void Deserialize_Concurrent_ShouldBeThreadSafe()

    // 错误处理测试 (3 个)
    [Fact] void Deserialize_InvalidJson_ShouldThrowException()
    [Fact] void Deserialize_MismatchedType_ShouldThrowException()
    [Fact] void Serialize_CircularReference_ShouldHandleGracefully()
}
```

**预估时间**: 2 小时
**覆盖率目标**: 85%+

---

## 阶段 2: 传输层测试 (P0) ⭐

### 2.1 InMemoryMessageTransport 测试

**文件**: `tests/Catga.Tests/Transport/InMemoryMessageTransportTests.cs`

**测试用例** (12 个):

```csharp
public class InMemoryMessageTransportTests
{
    // 基础功能测试 (4 个)
    [Fact] async Task PublishAsync_ValidMessage_ShouldSucceed()
    [Fact] async Task SubscribeAsync_ValidTopic_ShouldReceiveMessages()
    [Fact] async Task UnsubscribeAsync_ShouldStopReceivingMessages()
    [Fact] async Task PublishAsync_NoSubscribers_ShouldNotThrow()

    // 多订阅者测试 (2 个)
    [Fact] async Task PublishAsync_MultipleSubscribers_ShouldDeliverToAll()
    [Fact] async Task SubscribeAsync_SameTopic_ShouldReceiveIndependently()

    // QoS 测试 (3 个)
    [Fact] async Task PublishAsync_QoS0_ShouldDeliverAtMostOnce()
    [Fact] async Task PublishAsync_QoS1_ShouldDeliverAtLeastOnce()
    [Fact] async Task PublishAsync_QoS2_ShouldDeliverExactlyOnce()

    // 并发测试 (2 个)
    [Fact] async Task PublishAsync_Concurrent_ShouldHandleCorrectly()
    [Fact] async Task SubscribeAsync_Concurrent_ShouldBeThreadSafe()

    // 错误处理测试 (1 个)
    [Fact] async Task SubscribeAsync_HandlerThrows_ShouldNotAffectOthers()
}
```

**预估时间**: 2 小时
**覆盖率目标**: 85%+

---

### 2.2 NatsMessageTransport 测试

**文件**: `tests/Catga.Tests/Transport/NatsMessageTransportTests.cs`

**测试用例** (18 个):

```csharp
public class NatsMessageTransportTests
{
    // 连接测试 (3 个)
    [Fact] async Task Constructor_ValidOptions_ShouldConnect()
    [Fact] async Task Dispose_ShouldCloseConnection()
    [Fact] async Task Reconnect_AfterDisconnect_ShouldWork()

    // JetStream 发布测试 (4 个)
    [Fact] async Task PublishAsync_ToJetStream_ShouldSucceed()
    [Fact] async Task PublishAsync_WithMetadata_ShouldPreserveMetadata()
    [Fact] async Task PublishAsync_LargeMessage_ShouldHandle()
    [Fact] async Task PublishAsync_Batch_ShouldBeEfficient()

    // JetStream 订阅测试 (4 个)
    [Fact] async Task SubscribeAsync_ToJetStream_ShouldReceiveMessages()
    [Fact] async Task SubscribeAsync_WithConsumerGroup_ShouldLoadBalance()
    [Fact] async Task SubscribeAsync_Durable_ShouldResumeAfterRestart()
    [Fact] async Task SubscribeAsync_WithFilter_ShouldFilterMessages()

    // QoS 测试 (3 个)
    [Fact] async Task PublishAsync_QoS0_ShouldNotWaitForAck()
    [Fact] async Task PublishAsync_QoS1_ShouldWaitForAck()
    [Fact] async Task PublishAsync_QoS2_ShouldEnsureExactlyOnce()

    // 错误处理测试 (2 个)
    [Fact] async Task PublishAsync_ConnectionLost_ShouldRetry()
    [Fact] async Task SubscribeAsync_InvalidStream_ShouldThrowException()

    // 性能测试 (2 个)
    [Fact] async Task PublishAsync_10K_Messages_ShouldBeUnder5s()
    [Fact] async Task SubscribeAsync_HighThroughput_ShouldNotDropMessages()
}
```

**注意**: 需要 NATS 测试容器或 Mock

**预估时间**: 3 小时
**覆盖率目标**: 80%+

---

## 阶段 3: Redis 持久化测试 (P0) ⭐

### 3.1 OptimizedRedisOutboxStore 测试

**文件**: `tests/Catga.Tests/Persistence/Redis/RedisOutboxStoreTests.cs`

**测试用例** (15 个):

```csharp
public class RedisOutboxStoreTests
{
    // 基础功能测试 (5 个)
    [Fact] async Task AddAsync_ValidMessage_ShouldStore()
    [Fact] async Task GetPendingAsync_ShouldReturnUnpublishedMessages()
    [Fact] async Task MarkAsPublishedAsync_ShouldUpdateStatus()
    [Fact] async Task DeleteAsync_ShouldRemoveMessage()
    [Fact] async Task GetByIdAsync_ShouldReturnMessage()

    // 批量操作测试 (3 个)
    [Fact] async Task AddBatchAsync_100Messages_ShouldBeEfficient()
    [Fact] async Task GetPendingAsync_WithLimit_ShouldRespectLimit()
    [Fact] async Task MarkAsPublishedBatchAsync_ShouldUpdateAll()

    // 过期清理测试 (2 个)
    [Fact] async Task CleanupExpiredAsync_ShouldRemoveOldMessages()
    [Fact] async Task GetPendingAsync_ShouldExcludeExpired()

    // 并发测试 (3 个)
    [Fact] async Task AddAsync_Concurrent_ShouldBeThreadSafe()
    [Fact] async Task GetPendingAsync_Concurrent_ShouldNotDuplicate()
    [Fact] async Task MarkAsPublishedAsync_Concurrent_ShouldHandleRaceConditions()

    // 错误恢复测试 (2 个)
    [Fact] async Task AddAsync_RedisDown_ShouldThrowException()
    [Fact] async Task GetPendingAsync_AfterReconnect_ShouldWork()
}
```

**预估时间**: 2 小时
**覆盖率目标**: 85%+

---

### 3.2 RedisInboxPersistence 测试

**文件**: `tests/Catga.Tests/Persistence/Redis/RedisInboxPersistenceTests.cs`

**测试用例** (12 个):

```csharp
public class RedisInboxPersistenceTests
{
    // 幂等性测试 (4 个)
    [Fact] async Task HasProcessedAsync_NewMessage_ShouldReturnFalse()
    [Fact] async Task HasProcessedAsync_ProcessedMessage_ShouldReturnTrue()
    [Fact] async Task MarkAsProcessedAsync_ShouldStoreMessageId()
    [Fact] async Task HasProcessedAsync_ExpiredMessage_ShouldReturnFalse()

    // 批量处理测试 (2 个)
    [Fact] async Task MarkAsProcessedBatchAsync_ShouldStoreAll()
    [Fact] async Task HasProcessedBatchAsync_ShouldCheckAll()

    // 过期清理测试 (2 个)
    [Fact] async Task CleanupExpiredAsync_ShouldRemoveOldEntries()
    [Fact] async Task GetProcessedCountAsync_ShouldReturnCorrectCount()

    // 并发测试 (2 个)
    [Fact] async Task MarkAsProcessedAsync_Concurrent_ShouldBeThreadSafe()
    [Fact] async Task HasProcessedAsync_Concurrent_ShouldBeConsistent()

    // 错误处理测试 (2 个)
    [Fact] async Task MarkAsProcessedAsync_RedisDown_ShouldThrowException()
    [Fact] async Task HasProcessedAsync_InvalidMessageId_ShouldReturnFalse()
}
```

**预估时间**: 1.5 小时
**覆盖率目标**: 85%+

---

### 3.3 RedisDistributedCache 测试

**文件**: `tests/Catga.Tests/Persistence/Redis/RedisDistributedCacheTests.cs`

**测试用例** (12 个):

```csharp
public class RedisDistributedCacheTests
{
    // 基础操作测试 (5 个)
    [Fact] async Task GetAsync_ExistingKey_ShouldReturnValue()
    [Fact] async Task GetAsync_NonExistingKey_ShouldReturnNull()
    [Fact] async Task SetAsync_ValidKeyValue_ShouldStore()
    [Fact] async Task RemoveAsync_ExistingKey_ShouldDelete()
    [Fact] async Task ExistsAsync_ShouldReturnCorrectStatus()

    // 过期时间测试 (2 个)
    [Fact] async Task SetAsync_WithExpiration_ShouldExpireAfterTime()
    [Fact] async Task GetAsync_ExpiredKey_ShouldReturnNull()

    // 批量操作测试 (2 个)
    [Fact] async Task GetManyAsync_ShouldReturnAllValues()
    [Fact] async Task SetManyAsync_ShouldStoreAllValues()

    // 并发测试 (2 个)
    [Fact] async Task SetAsync_Concurrent_ShouldHandleCorrectly()
    [Fact] async Task GetAsync_Concurrent_ShouldBeThreadSafe()

    // 错误处理测试 (1 个)
    [Fact] async Task GetAsync_RedisDown_ShouldThrowException()
}
```

**预估时间**: 1.5 小时
**覆盖率目标**: 85%+

---

### 3.4 RedisDistributedLock 测试

**文件**: `tests/Catga.Tests/Persistence/Redis/RedisDistributedLockTests.cs`

**测试用例** (15 个):

```csharp
public class RedisDistributedLockTests
{
    // 基础锁操作测试 (5 个)
    [Fact] async Task AcquireAsync_AvailableLock_ShouldSucceed()
    [Fact] async Task AcquireAsync_LockedResource_ShouldWait()
    [Fact] async Task ReleaseAsync_AcquiredLock_ShouldRelease()
    [Fact] async Task AcquireAsync_WithTimeout_ShouldTimeout()
    [Fact] async Task TryAcquireAsync_LockedResource_ShouldReturnFalse()

    // 锁超时测试 (3 个)
    [Fact] async Task AcquireAsync_WithExpiration_ShouldAutoRelease()
    [Fact] async Task AcquireAsync_ExpiredLock_ShouldReacquire()
    [Fact] async Task RenewAsync_ShouldExtendLockTime()

    // 并发竞争测试 (4 个)
    [Fact] async Task AcquireAsync_Concurrent_OnlyOneShouldSucceed()
    [Fact] async Task AcquireAsync_HighContention_ShouldHandleCorrectly()
    [Fact] async Task ReleaseAsync_Concurrent_ShouldNotAffectOthers()
    [Fact] async Task AcquireAsync_10Threads_ShouldSerialize()

    // 错误处理测试 (3 个)
    [Fact] async Task ReleaseAsync_NotOwned_ShouldThrowException()
    [Fact] async Task AcquireAsync_RedisDown_ShouldThrowException()
    [Fact] async Task AcquireAsync_AfterReconnect_ShouldWork()
}
```

**预估时间**: 2 小时
**覆盖率目标**: 85%+

---

### 3.5 RedisIdempotencyStore 测试

**文件**: `tests/Catga.Tests/Persistence/Redis/RedisIdempotencyStoreTests.cs`

**测试用例** (12 个):

```csharp
public class RedisIdempotencyStoreTests
{
    // 基础功能测试 (4 个)
    [Fact] async Task TryGetAsync_NewKey_ShouldReturnNull()
    [Fact] async Task TryGetAsync_ExistingKey_ShouldReturnCachedResult()
    [Fact] async Task TryStoreAsync_NewKey_ShouldStore()
    [Fact] async Task TryStoreAsync_ExistingKey_ShouldReturnFalse()

    // 过期测试 (2 个)
    [Fact] async Task TryGetAsync_ExpiredKey_ShouldReturnNull()
    [Fact] async Task TryStoreAsync_WithExpiration_ShouldExpire()

    // 并发测试 (4 个)
    [Fact] async Task TryStoreAsync_Concurrent_OnlyOneShouldSucceed()
    [Fact] async Task TryGetAsync_Concurrent_ShouldBeThreadSafe()
    [Fact] async Task TryStoreAsync_HighContention_ShouldHandleCorrectly()
    [Fact] async Task TryStoreAsync_100Concurrent_ShouldSerialize()

    // 清理测试 (2 个)
    [Fact] async Task CleanupExpiredAsync_ShouldRemoveOldEntries()
    [Fact] async Task GetCountAsync_ShouldReturnCorrectCount()
}
```

**预估时间**: 1.5 小时
**覆盖率目标**: 85%+

---

## 阶段 4: ASP.NET Core 集成测试 (P1)

### 4.1 RPC 端点测试

**文件**: `tests/Catga.AspNetCore.Tests/Rpc/RpcEndpointTests.cs`

**测试用例** (12 个):

```csharp
public class RpcEndpointTests
{
    // 基础 RPC 测试 (4 个)
    [Fact] async Task RpcCall_ValidRequest_ShouldReturnResponse()
    [Fact] async Task RpcCall_InvalidRequest_ShouldReturn400()
    [Fact] async Task RpcCall_HandlerThrows_ShouldReturn500()
    [Fact] async Task RpcCall_NotFound_ShouldReturn404()

    // 超时测试 (2 个)
    [Fact] async Task RpcCall_WithTimeout_ShouldTimeout()
    [Fact] async Task RpcCall_LongRunning_ShouldComplete()

    // 并发测试 (2 个)
    [Fact] async Task RpcCall_Concurrent_ShouldHandleCorrectly()
    [Fact] async Task RpcCall_HighLoad_ShouldMaintainPerformance()

    // 序列化测试 (2 个)
    [Fact] async Task RpcCall_ComplexObject_ShouldSerializeCorrectly()
    [Fact] async Task RpcCall_LargePayload_ShouldHandle()

    // 错误处理测试 (2 个)
    [Fact] async Task RpcCall_MalformedJson_ShouldReturn400()
    [Fact] async Task RpcCall_MissingHandler_ShouldReturn404()
}
```

**预估时间**: 2 小时
**覆盖率目标**: 80%+

---

### 4.2 Catga 端点测试

**文件**: `tests/Catga.AspNetCore.Tests/CatgaEndpointTests.cs`

**测试用例** (10 个):

```csharp
public class CatgaEndpointTests
{
    // 端点映射测试 (3 个)
    [Fact] void MapCatgaEndpoints_ShouldRegisterRoutes()
    [Fact] async Task CommandEndpoint_ShouldInvokeMediator()
    [Fact] async Task QueryEndpoint_ShouldReturnResult()

    // 请求处理测试 (3 个)
    [Fact] async Task PostCommand_ValidPayload_ShouldReturn200()
    [Fact] async Task GetQuery_ValidParams_ShouldReturnData()
    [Fact] async Task PostCommand_InvalidPayload_ShouldReturn400()

    // 响应格式化测试 (2 个)
    [Fact] async Task CommandEndpoint_ShouldReturnCatgaResult()
    [Fact] async Task QueryEndpoint_ShouldFormatResponse()

    // 错误处理测试 (2 个)
    [Fact] async Task CommandEndpoint_HandlerFails_ShouldReturnError()
    [Fact] async Task QueryEndpoint_NotFound_ShouldReturn404()
}
```

**预估时间**: 1.5 小时
**覆盖率目标**: 75%+

---

## 阶段 5: Source Generator 测试 (P1)

### 5.1 分析器测试

**文件**: `tests/Catga.SourceGenerator.Tests/Analyzers/AnalyzerTests.cs`

**测试用例** (18 个):

```csharp
public class AnalyzerTests
{
    // CATGA001 测试 (6 个)
    [Fact] void MissingMemoryPackable_ShouldReportDiagnostic()
    [Fact] void HasMemoryPackable_ShouldNotReportDiagnostic()
    [Fact] void NonMessageType_ShouldNotReportDiagnostic()
    [Fact] void PartialClass_WithMemoryPackable_ShouldNotReport()
    [Fact] void InheritedMessage_ShouldCheckAttribute()
    [Fact] void GenericMessage_ShouldCheckAttribute()

    // CATGA002 测试 (6 个)
    [Fact] void MissingSerializerRegistration_ShouldReportDiagnostic()
    [Fact] void HasSerializerRegistration_ShouldNotReportDiagnostic()
    [Fact] void MultipleSerializers_ShouldNotReportDiagnostic()
    [Fact] void CustomSerializer_ShouldNotReportDiagnostic()
    [Fact] void SerializerInDifferentMethod_ShouldNotReport()
    [Fact] void SerializerInBaseClass_ShouldNotReport()

    // 其他分析器测试 (6 个)
    [Fact] void MissingHandler_ShouldReportDiagnostic()
    [Fact] void DuplicateHandler_ShouldReportDiagnostic()
    [Fact] void InvalidMessageType_ShouldReportDiagnostic()
    [Fact] void MissingAotAttribute_ShouldReportDiagnostic()
    [Fact] void InvalidPipelineBehavior_ShouldReportDiagnostic()
    [Fact] void CircularDependency_ShouldReportDiagnostic()
}
```

**预估时间**: 3 小时
**覆盖率目标**: 85%+

---

### 5.2 代码修复测试

**文件**: `tests/Catga.SourceGenerator.Tests/CodeFixes/CodeFixTests.cs`

**测试用例** (12 个):

```csharp
public class CodeFixTests
{
    // CATGA001 修复测试 (4 个)
    [Fact] async Task AddMemoryPackable_ShouldAddAttribute()
    [Fact] async Task AddMemoryPackable_ShouldMakePartial()
    [Fact] async Task AddMemoryPackable_ShouldPreserveOtherAttributes()
    [Fact] async Task AddMemoryPackable_ShouldFormatCorrectly()

    // CATGA002 修复测试 (4 个)
    [Fact] async Task AddSerializerRegistration_ShouldAddUseMemoryPack()
    [Fact] async Task AddSerializerRegistration_ShouldAddUseJson()
    [Fact] async Task AddSerializerRegistration_ShouldAddCustomSerializer()
    [Fact] async Task AddSerializerRegistration_ShouldPlaceCorrectly()

    // 其他修复测试 (4 个)
    [Fact] async Task AddHandler_ShouldGenerateHandlerClass()
    [Fact] async Task AddAotAttribute_ShouldAddDynamicallyAccessedMembers()
    [Fact] async Task FixPipelineBehavior_ShouldCorrectSignature()
    [Fact] async Task RemoveDuplicateHandler_ShouldKeepOne()
}
```

**预估时间**: 2 小时
**覆盖率目标**: 80%+

---

## 阶段 6: 扩展测试 (P2)

### 6.1 Pipeline 行为扩展测试

**文件**: `tests/Catga.Tests/Pipeline/PipelineBehaviorExtendedTests.cs`

**测试用例** (15 个):

```csharp
public class PipelineBehaviorExtendedTests
{
    // 边界情况测试 (5 个)
    [Fact] async Task RetryBehavior_MaxRetriesExceeded_ShouldFail()
    [Fact] async Task ValidationBehavior_EmptyRules_ShouldPass()
    [Fact] async Task IdempotencyBehavior_ConcurrentRequests_ShouldHandleCorrectly()
    [Fact] async Task LoggingBehavior_LargePayload_ShouldTruncate()
    [Fact] async Task TimeoutBehavior_LongRunning_ShouldCancel()

    // 组合测试 (5 个)
    [Fact] async Task MultipleBehaviors_ShouldExecuteInOrder()
    [Fact] async Task RetryWithValidation_ShouldValidateBeforeRetry()
    [Fact] async Task IdempotencyWithLogging_ShouldLogCorrectly()
    [Fact] async Task AllBehaviors_ShouldWorkTogether()
    [Fact] async Task CustomBehavior_ShouldIntegrate()

    // 性能测试 (3 个)
    [Fact] async Task PipelineOverhead_ShouldBeLessThan10Percent()
    [Fact] async Task BehaviorChain_10Behaviors_ShouldBeEfficient()
    [Fact] async Task PipelineExecution_1000Requests_ShouldMaintainPerformance()

    // 错误传播测试 (2 个)
    [Fact] async Task BehaviorThrows_ShouldPropagateCorrectly()
    [Fact] async Task InnerBehaviorFails_ShouldExecuteOuterCleanup()
}
```

**预估时间**: 2 小时
**覆盖率目标**: 90%+

---

### 6.2 幂等性存储扩展测试

**文件**: `tests/Catga.Tests/Idempotency/IdempotencyStoreExtendedTests.cs`

**测试用例** (12 个):

```csharp
public class IdempotencyStoreExtendedTests
{
    // 高并发测试 (4 个)
    [Fact] async Task TryStore_1000Concurrent_ShouldSerialize()
    [Fact] async Task TryGet_HighContention_ShouldBeConsistent()
    [Fact] async Task TryStore_RaceCondition_ShouldHandleCorrectly()
    [Fact] async Task ShardedStore_UniformDistribution_ShouldBalance()

    // 内存管理测试 (3 个)
    [Fact] async Task Store_LargeVolume_ShouldNotLeak()
    [Fact] async Task Cleanup_ShouldReleaseMemory()
    [Fact] async Task Store_WithExpiration_ShouldAutoCleanup()

    // 性能测试 (3 个)
    [Fact] async Task TryGet_CacheHit_ShouldBeLessThan100ns()
    [Fact] async Task TryStore_ShouldBeLessThan500ns()
    [Fact] async Task Store_1M_Entries_ShouldHandleEfficiently()

    // 边界测试 (2 个)
    [Fact] async Task Store_MaxCapacity_ShouldEvictOldest()
    [Fact] async Task Store_EmptyKey_ShouldThrowException()
}
```

**预估时间**: 1.5 小时
**覆盖率目标**: 90%+

---

## 阶段 7: 性能基准测试扩展 (P2)

### 7.1 幂等性性能基准测试

**文件**: `benchmarks/Catga.Benchmarks/IdempotencyPerformanceBenchmarks.cs`

**基准测试** (8 个):

```csharp
[MemoryDiagnoser]
public class IdempotencyPerformanceBenchmarks
{
    [Benchmark(Baseline = true)]
    public async Task IdempotencyStore_CacheHit()

    [Benchmark]
    public async Task IdempotencyStore_CacheMiss()

    [Benchmark]
    public async Task IdempotencyStore_Store_New()

    [Benchmark]
    public async Task IdempotencyStore_Store_Update()

    [Benchmark]
    public async Task IdempotencyStore_Concurrent_10()

    [Benchmark]
    public async Task IdempotencyStore_Concurrent_100()

    [Benchmark]
    public async Task IdempotencyStore_Cleanup()

    [Benchmark]
    public async Task IdempotencyStore_Shards_Comparison()
}
```

**性能目标**:
- Cache Hit: < 100ns
- Cache Miss: < 200ns
- Store: < 500ns
- Cleanup: < 10ms

**预估时间**: 2 小时

---

### 7.2 Pipeline 性能基准测试

**文件**: `benchmarks/Catga.Benchmarks/PipelinePerformanceBenchmarks.cs`

**基准测试** (10 个):

```csharp
[MemoryDiagnoser]
public class PipelinePerformanceBenchmarks
{
    [Benchmark(Baseline = true)]
    public async Task Pipeline_NoBehavior()

    [Benchmark]
    public async Task Pipeline_WithRetry()

    [Benchmark]
    public async Task Pipeline_WithValidation()

    [Benchmark]
    public async Task Pipeline_WithIdempotency()

    [Benchmark]
    public async Task Pipeline_WithLogging()

    [Benchmark]
    public async Task Pipeline_AllBehaviors()

    [Benchmark]
    public async Task Pipeline_3Behaviors()

    [Benchmark]
    public async Task Pipeline_5Behaviors()

    [Benchmark]
    public async Task Pipeline_10Behaviors()

    [Benchmark]
    public async Task Pipeline_CustomBehavior()
}
```

**性能目标**:
- No Behavior: < 50μs (Baseline)
- + Retry: < 80μs (+60%)
- + Validation: < 70μs (+40%)
- + All: < 120μs (+140%)

**预估时间**: 2 小时

---

## 阶段 8: 集成测试 (P2)

### 8.1 端到端 CQRS 流程测试

**文件**: `tests/Catga.IntegrationTests/E2E/CqrsFlowTests.cs`

**测试用例** (10 个):

```csharp
public class CqrsFlowTests
{
    // 完整流程测试 (4 个)
    [Fact] async Task CompleteFlow_CommandToEvent_ShouldWork()
    [Fact] async Task CompleteFlow_WithOutbox_ShouldEnsureDelivery()
    [Fact] async Task CompleteFlow_WithInbox_ShouldEnsureIdempotency()
    [Fact] async Task CompleteFlow_WithAllFeatures_ShouldWork()

    // 分布式场景测试 (3 个)
    [Fact] async Task DistributedFlow_MultipleNodes_ShouldLoadBalance()
    [Fact] async Task DistributedFlow_NodeFailure_ShouldRecover()
    [Fact] async Task DistributedFlow_NetworkPartition_ShouldHandle()

    // 性能测试 (3 个)
    [Fact] async Task HighThroughput_10K_Commands_ShouldComplete()
    [Fact] async Task LowLatency_P99_ShouldBeLessThan100ms()
    [Fact] async Task SustainedLoad_1Hour_ShouldMaintainPerformance()
}
```

**预估时间**: 4 小时
**覆盖率目标**: 端到端场景覆盖

---

## 📊 执行计划总结

### 时间估算

| 阶段 | 任务 | 预估时间 | 优先级 |
|------|------|---------|--------|
| **阶段 1** | 序列化器测试 | 4 小时 | P0 ⭐ |
| **阶段 2** | 传输层测试 | 5 小时 | P0 ⭐ |
| **阶段 3** | Redis 持久化测试 | 8.5 小时 | P0 ⭐ |
| **阶段 4** | ASP.NET Core 测试 | 3.5 小时 | P1 |
| **阶段 5** | Source Generator 测试 | 5 小时 | P1 |
| **阶段 6** | 扩展测试 | 3.5 小时 | P2 |
| **阶段 7** | 性能基准测试扩展 | 4 小时 | P2 |
| **阶段 8** | 集成测试 | 4 小时 | P2 |
| **总计** | | **37.5 小时** | ~5 工作日 |

### P0 任务 (关键路径) - 17.5 小时

1. ✅ 序列化器测试 (4h)
2. ✅ 传输层测试 (5h)
3. ✅ Redis 持久化测试 (8.5h)

**完成后覆盖率**: ~75%

### P1 任务 (重要) - 8.5 小时

4. ✅ ASP.NET Core 测试 (3.5h)
5. ✅ Source Generator 测试 (5h)

**完成后覆盖率**: ~80%

### P2 任务 (可选) - 11.5 小时

6. ✅ 扩展测试 (3.5h)
7. ✅ 性能基准测试扩展 (4h)
8. ✅ 集成测试 (4h)

**完成后覆盖率**: ~85%+

---

## 🎯 成功标准

### 覆盖率目标

- ✅ **整体覆盖率**: ≥ 85%
- ✅ **核心模块覆盖率**: ≥ 90%
- ✅ **关键路径覆盖率**: 100%

### 测试质量

- ✅ **所有测试通过**: 100%
- ✅ **测试稳定性**: 100% (无 flaky tests)
- ✅ **测试执行时间**: < 2 分钟 (单元测试)
- ✅ **测试可维护性**: 高 (清晰命名、良好结构)

### 性能基准

- ✅ **基准测试套件**: ≥ 12 个
- ✅ **性能指标达标**: 所有关键路径 < 1μs
- ✅ **零分配验证**: Gen0 = 0 for hot paths
- ✅ **性能报告**: HTML + Markdown 格式

---

## 🚀 立即开始

**推荐执行顺序**:

### 第 1 天 (8 小时) - P0 核心测试
1. ✅ 序列化器测试 (4h)
2. ✅ 传输层测试 - Part 1 (4h)

### 第 2 天 (8 小时) - P0 持久化测试
3. ✅ 传输层测试 - Part 2 (1h)
4. ✅ Redis Outbox/Inbox 测试 (4h)
5. ✅ Redis Cache/Lock 测试 (3h)

### 第 3 天 (8 小时) - P0 + P1
6. ✅ Redis Idempotency 测试 (1.5h)
7. ✅ ASP.NET Core 测试 (3.5h)
8. ✅ Source Generator 测试 - Part 1 (3h)

### 第 4 天 (8 小时) - P1 + P2
9. ✅ Source Generator 测试 - Part 2 (2h)
10. ✅ 扩展测试 (3.5h)
11. ✅ 性能基准测试扩展 (2.5h)

### 第 5 天 (5.5 小时) - P2 + 验证
12. ✅ 性能基准测试扩展 - Part 2 (1.5h)
13. ✅ 集成测试 (4h)

---

**Catga** - 迈向 85%+ 测试覆盖的高质量 CQRS 框架 🚀

