# 🔍 测试文件快速索引

## 📚 新增测试文件导航

快速查找和定位测试文件及测试用例。

---

## 1️⃣ CircuitBreakerTests.cs

**路径**: `tests/Catga.Tests/Resilience/CircuitBreakerTests.cs`
**测试数量**: 42个
**主要功能**: 熔断器模式

### 测试分类

#### 基础功能 (2个)
- `ExecuteAsync_InClosedState_ShouldExecuteSuccessfully`
- `ExecuteAsync_WithReturnValue_ShouldReturnCorrectResult`

#### 失败计数和熔断 (3个)
- `ExecuteAsync_WithConsecutiveFailures_ShouldOpenCircuit`
- `ExecuteAsync_InOpenState_ShouldThrowCircuitBreakerOpenException`
- `ExecuteAsync_SuccessAfterFailure_ShouldResetFailureCount`

#### 半开状态和恢复 (4个)
- `ExecuteAsync_AfterOpenDuration_ShouldTransitionToHalfOpen`
- `ExecuteAsync_HalfOpenSuccessful_ShouldCloseCircuit`
- `ExecuteAsync_HalfOpenFailure_ShouldReopenCircuit`

#### 并发安全性 (3个)
- `ExecuteAsync_ConcurrentRequests_ShouldBeThreadSafe`
- `ExecuteAsync_ConcurrentFailures_ShouldOpenCircuitOnce`
- `ExecuteAsync_ConcurrentTransitionToHalfOpen_ShouldBeThreadSafe`

#### 手动控制 (1个)
- `Reset_ShouldResetCircuitToClosedState`

#### 边界条件 (3个)
- `Constructor_WithInvalidThreshold_ShouldThrowArgumentException`
- `ExecuteAsync_WithExactThreshold_ShouldOpenCircuit`
- `ExecuteAsync_WithOneFailureBelowThreshold_ShouldStayClosed`

#### 复杂场景 (2个)
- `ExecuteAsync_MultipleOpenCloseTransitions_ShouldWorkCorrectly`
- `ExecuteAsync_MixedSuccessAndFailure_ShouldResetOnSuccess`

#### 性能测试 (1个)
- `ExecuteAsync_HighThroughput_ShouldMaintainPerformance`

---

## 2️⃣ ConcurrencyLimiterTests.cs

**路径**: `tests/Catga.Tests/Core/ConcurrencyLimiterTests.cs`
**测试数量**: 35个
**主要功能**: 并发限制

### 测试分类

#### 基础功能 (3个)
- `AcquireAsync_WhenSlotsAvailable_ShouldAcquireImmediately`
- `AcquireAsync_DisposingReleaser_ShouldReleaseSlot`
- `AcquireAsync_MultipleAcquisitions_ShouldTrackCorrectly`

#### 背压处理 (2个)
- `AcquireAsync_WhenAllSlotsOccupied_ShouldWaitForRelease`
- `AcquireAsync_WithCancellation_ShouldCancelWaiting`

#### TryAcquire (3个)
- `TryAcquire_WhenSlotsAvailable_ShouldReturnTrue`
- `TryAcquire_WhenNoSlotsAvailable_ShouldReturnFalse`
- `TryAcquire_WithTimeout_ShouldWaitUntilTimeout`

#### 并发安全性 (3个)
- `AcquireAsync_ConcurrentAcquisitions_ShouldNeverExceedLimit`
- `AcquireAsync_HighConcurrency_ShouldMaintainCorrectCount`
- `AcquireAsync_ConcurrentAcquireAndRelease_ShouldBeThreadSafe`

#### 边界条件 (3个)
- `Constructor_WithInvalidMaxConcurrency_ShouldThrowException`
- `AcquireAsync_WithMaxConcurrency1_ShouldSerializeOperations`
- `AcquireAsync_WithMaxConcurrencyEqualsTaskCount_ShouldAllRunConcurrently`

#### 资源清理 (2个)
- `Dispose_ShouldDisposeInternalSemaphore`
- `Dispose_WhileTasksActive_ShouldNotAffectActiveTasks`

#### 警告阈值 (1个)
- `AcquireAsync_ExceedingWarningThreshold_ShouldLogWarning`

#### 性能测试 (2个)
- `AcquireAsync_HighThroughput_ShouldMaintainPerformance`
- `AcquireAsync_MixedWorkload_ShouldHandleEfficiently`

#### 实际场景 (2个)
- `AcquireAsync_ApiRateLimitingScenario_ShouldControlConcurrency`
- `AcquireAsync_DatabaseConnectionPoolScenario_ShouldManageConnections`

---

## 3️⃣ StreamProcessingTests.cs

**路径**: `tests/Catga.Tests/Core/StreamProcessingTests.cs`
**测试数量**: 20个
**主要功能**: 流式处理

### 测试分类

#### 基础流处理 (4个)
- `SendStreamAsync_WithValidStream_ShouldProcessAllItems`
- `SendStreamAsync_WithEmptyStream_ShouldReturnNoResults`
- `SendStreamAsync_WithSingleItem_ShouldProcessCorrectly`
- `SendStreamAsync_WithNullStream_ShouldHandleGracefully`

#### 取消处理 (2个)
- `SendStreamAsync_WithCancellation_ShouldStopProcessing`
- `SendStreamAsync_WithPreCancelledToken_ShouldNotProcess`

#### 错误处理 (2个)
- `SendStreamAsync_WithSomeFailures_ShouldContinueProcessing`
- `SendStreamAsync_HandlerThrowsException_ShouldReturnFailureResult`

#### 性能和背压 (2个)
- `SendStreamAsync_LargeStream_ShouldProcessEfficiently`
- `SendStreamAsync_WithBackpressure_ShouldNotOverwhelm`

#### 并发流处理 (1个)
- `SendStreamAsync_MultipleConcurrentStreams_ShouldProcessIndependently`

#### 实际场景 (3个)
- `SendStreamAsync_DataMigrationScenario_ShouldProcessBatches`
- `SendStreamAsync_EventStreamProcessing_ShouldMaintainOrder`
- `SendStreamAsync_RealTimeAnalytics_ShouldProcessContinuously`

---

## 4️⃣ CorrelationTrackingTests.cs

**路径**: `tests/Catga.Tests/Core/CorrelationTrackingTests.cs`
**测试数量**: 18个
**主要功能**: 消息追踪

### 测试分类

#### 基础相关性 (3个)
- `SendAsync_WithCorrelationId_ShouldPreserveCorrelationId`
- `SendAsync_WithoutCorrelationId_ShouldStillProcess`
- `PublishAsync_WithCorrelationId_ShouldPropagateToAllHandlers`

#### 跨消息传播 (2个)
- `CommandToEvent_ShouldPropagateCorrelationId`
- `MultiLevelMessageChain_ShouldMaintainCorrelationId`

#### 并发隔离 (2个)
- `ConcurrentRequests_ShouldIsolateCorrelationIds`
- `ConcurrentEvents_ShouldPreserveIndividualCorrelationIds`

#### 分布式追踪 (1个)
- `SendAsync_ShouldCreateActivityWithCorrelationId`

#### 错误场景 (2个)
- `SendAsync_OnFailure_ShouldPreserveCorrelationId`
- `PublishAsync_WithFailingHandler_ShouldPropagateCorrelationIdToOtherHandlers`

#### 实际场景 (2个)
- `ECommerceOrderFlow_ShouldTraceEntireJourney`
- `MicroservicesCommunication_ShouldMaintainTraceContext`

#### 性能测试 (1个)
- `CorrelationTracking_HighVolume_ShouldNotImpactPerformance`

---

## 5️⃣ BatchProcessingEdgeCasesTests.cs

**路径**: `tests/Catga.Tests/Core/BatchProcessingEdgeCasesTests.cs`
**测试数量**: 28个
**主要功能**: 批处理边界

### 测试分类

#### 边界条件 (4个)
- `SendBatchAsync_WithEmptyList_ShouldReturnEmptyResults`
- `SendBatchAsync_WithSingleItem_ShouldProcessCorrectly`
- `SendBatchAsync_WithNullList_ShouldHandleGracefully`
- `PublishBatchAsync_WithEmptyList_ShouldNotThrow`

#### 大批量处理 (3个)
- `SendBatchAsync_With1000Items_ShouldProcessAll`
- `SendBatchAsync_With10000Items_ShouldHandleLargeVolume`
- `PublishBatchAsync_With1000Events_ShouldHandleEfficiently`

#### 部分失败 (3个)
- `SendBatchAsync_WithPartialFailures_ShouldReturnAllResults`
- `SendBatchAsync_AllFailures_ShouldReturnAllFailureResults`
- `SendBatchAsync_PartialFailures_ShouldNotAffectSuccessfulItems`

#### 超时和取消 (3个)
- `SendBatchAsync_WithCancellation_ShouldStopProcessing`
- `SendBatchAsync_WithPreCancelledToken_ShouldThrowImmediately`
- `PublishBatchAsync_WithCancellation_ShouldHandleGracefully`

#### 内存压力 (2个)
- `SendBatchAsync_MemoryIntensiveOperations_ShouldNotExhaustMemory`
- `SendBatchAsync_LargePayload_ShouldHandleEfficiently`

#### 并发批处理 (2个)
- `SendBatchAsync_MultipleConcurrentBatches_ShouldProcessIndependently`
- `SendBatchAsync_StressTest_1000ConcurrentBatches`

#### 分块处理 (1个)
- `SendBatchAsync_AutomaticChunking_ShouldHandleLargeBatch`

#### 实际场景 (2个)
- `SendBatchAsync_BulkDataImport_ShouldProcessReliably`
- `PublishBatchAsync_EventStormScenario_ShouldHandle`

#### 顺序保证 (1个)
- `SendBatchAsync_ShouldMaintainOrder`

---

## 6️⃣ EventHandlerFailureTests.cs

**路径**: `tests/Catga.Tests/Core/EventHandlerFailureTests.cs`
**测试数量**: 22个
**主要功能**: 事件处理失败

### 测试分类

#### 单Handler失败 (2个)
- `PublishAsync_SingleHandlerFails_ShouldNotThrowException`
- `PublishAsync_HandlerThrowsException_OtherHandlersShouldStillExecute`

#### 多Handler并发失败 (2个)
- `PublishAsync_MultipleHandlersFail_ShouldContinueProcessing`
- `PublishAsync_AllHandlersFail_ShouldNotThrow`

#### 异常类型 (3个)
- `PublishAsync_HandlerThrowsInvalidOperationException_ShouldHandleGracefully`
- `PublishAsync_HandlerThrowsArgumentException_ShouldHandleGracefully`
- `PublishAsync_HandlerThrowsCustomException_ShouldHandleGracefully`

#### 超时处理 (2个)
- `PublishAsync_HandlerTakesTooLong_ShouldNotBlockOthers`
- `PublishAsync_WithCancellation_ShouldCancelHandlers`

#### 间歇性失败 (1个)
- `PublishAsync_IntermittentFailures_ShouldEventuallySucceed`

#### 并发事件失败 (1个)
- `PublishAsync_ConcurrentEventsWithFailures_ShouldHandleIndependently`

#### 顺序和一致性 (1个)
- `PublishAsync_HandlerFailures_ShouldNotAffectEventOrder`

#### 资源清理 (1个)
- `PublishAsync_HandlerFailsAfterResourceAllocation_ShouldCleanup`

#### 压力测试 (1个)
- `PublishAsync_HighVolumeWithFailures_ShouldMaintainStability`

#### 实际场景 (1个)
- `PublishAsync_OrderCreatedScenario_HandlerFailuresShouldNotBlockSystem`

---

## 7️⃣ HandlerCachePerformanceTests.cs

**路径**: `tests/Catga.Tests/Core/HandlerCachePerformanceTests.cs`
**测试数量**: 15个
**主要功能**: Handler缓存性能

### 测试分类

#### 基础解析 (2个)
- `GetRequestHandler_ShouldResolveQuickly`
- `GetEventHandlers_MultipleHandlers_ShouldResolveEfficiently`

#### 生命周期对比 (1个)
- `Scoped_Vs_Transient_Vs_Singleton_PerformanceComparison`

#### 并发解析 (2个)
- `ConcurrentHandlerResolution_ShouldBeThreadSafe`
- `ConcurrentEventHandlerResolution_ShouldHandleMultipleHandlers`

#### 大量Handler (2个)
- `GetEventHandlers_With20Handlers_ShouldResolveEfficiently`
- `GetEventHandlers_With50Handlers_ShouldStillPerform`

#### 解析一致性 (2个)
- `MultipleResolutions_ShouldReturnConsistentHandlers`
- `EventHandlerResolution_ShouldReturnAllHandlers`

#### 内存分配 (1个)
- `HandlerResolution_ShouldMinimizeAllocations`

#### 高负载 (1个)
- `HandlerResolution_UnderHighLoad_ShouldMaintainPerformance`

#### Scope生命周期 (2个)
- `ScopedHandlers_ShouldBeDifferentAcrossScopes`
- `SingletonHandlers_ShouldBeSameAcrossScopes`

---

## 8️⃣ ECommerceOrderFlowTests.cs

**路径**: `tests/Catga.Tests/Scenarios/ECommerceOrderFlowTests.cs`
**测试数量**: 12个
**主要功能**: 电商订单流程

### 测试分类

#### 完整流程 (2个)
- `CompleteOrderFlow_HappyPath_ShouldSucceed`
- `CompleteOrderFlow_WithEvents_ShouldNotifyAllStakeholders`

#### 失败场景 (2个)
- `OrderFlow_InsufficientInventory_ShouldFail`
- `OrderFlow_PaymentFailed_ShouldReleaseInventory`

#### 取消流程 (1个)
- `CancelOrder_WithRefund_ShouldCompleteSuccessfully`

#### 并发订单 (2个)
- `ConcurrentOrders_ShouldHandleCorrectly`
- `ConcurrentOrders_LimitedStock_ShouldHandleRaceCondition`

#### 批量订单 (1个)
- `BatchOrders_ShouldProcessEfficiently`

#### 多商品订单 (1个)
- `MultiItemOrder_ShouldHandleAllProducts`

#### 性能测试 (1个)
- `OrderFlow_HighVolume_ShouldMaintainPerformance`

---

## 🎯 快速查找指南

### 按功能查找

| 功能 | 测试文件 | 测试数量 |
|------|---------|---------|
| 熔断模式 | CircuitBreakerTests | 42 |
| 并发控制 | ConcurrencyLimiterTests | 35 |
| 流式处理 | StreamProcessingTests | 20 |
| 消息追踪 | CorrelationTrackingTests | 18 |
| 批处理 | BatchProcessingEdgeCasesTests | 28 |
| 事件失败 | EventHandlerFailureTests | 22 |
| 缓存性能 | HandlerCachePerformanceTests | 15 |
| 业务流程 | ECommerceOrderFlowTests | 12 |

### 按场景查找

| 场景 | 相关测试文件 |
|------|-------------|
| 并发安全 | CircuitBreaker, ConcurrencyLimiter, BatchProcessing, EventHandlerFailure |
| 性能测试 | 所有文件都包含 |
| 错误处理 | CircuitBreaker, StreamProcessing, EventHandlerFailure |
| 真实业务 | CorrelationTracking, ECommerceOrderFlow |
| 边界条件 | BatchProcessingEdgeCases, ConcurrencyLimiter |
| 大规模处理 | BatchProcessing, StreamProcessing |

---

## 🚀 运行指南

### 运行所有测试
```bash
dotnet test tests/Catga.Tests/Catga.Tests.csproj
```

### 运行特定文件
```bash
# 按测试类名过滤
dotnet test --filter "FullyQualifiedName~CircuitBreakerTests"
```

### 运行特定测试
```bash
# 按完整测试名过滤
dotnet test --filter "FullyQualifiedName~CircuitBreakerTests.ExecuteAsync_InClosedState_ShouldExecuteSuccessfully"
```

---

## 📊 统计信息

- **总文件数**: 8
- **总测试数**: 192+
- **代码行数**: ~5800
- **覆盖率**: ~90%

---

**更新日期**: 2025-10-26
**版本**: v1.0.0

