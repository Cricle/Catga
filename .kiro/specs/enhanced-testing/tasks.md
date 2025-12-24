# Implementation Plan: Enhanced Testing for Catga Framework

## Overview

本任务计划在现有 2200+ 测试的基础上，增加约 500+ 新测试，全面提升框架稳定性。测试按照单一组件深度验证 → 组件组合测试 → 后端矩阵测试 → 复杂 E2E 场景 → 弹性测试的顺序执行。

**目标**: 达到 2700+ 测试，100% 通过率

## Tasks

- [x] 1. 测试基础设施增强
  - [x] 1.1 创建组合测试基类
    - 创建 `tests/Catga.Tests/Framework/ComponentCombinationTestBase.cs`
    - 支持两组件和三组件组合
    - 提供统一的 Setup/TearDown
    - _Requirements: Design - 测试基础设施增强_
  
  - [x] 1.2 创建后端矩阵测试框架
    - 创建 `tests/Catga.Tests/Framework/BackendMatrixTestFramework.cs`
    - 实现 27 种后端组合生成
    - 提供后端配置辅助方法
    - _Requirements: Design - 后端矩阵测试框架_
  
  - [x] 1.3 创建故障注入框架
    - 创建 `tests/Catga.Tests/Framework/FaultInjectionMiddleware.cs`
    - 支持网络超时、连接失败、序列化错误等故障类型
    - 提供故障概率配置
    - _Requirements: Requirements 16, 17_
  
  - [x] 1.4 创建性能基准框架
    - 创建 `tests/Catga.Tests/Framework/PerformanceBenchmarkFramework.cs`
    - 实现性能指标收集（吞吐量、延迟、内存）
    - 提供基准保存和加载
    - 实现回归检测
    - _Requirements: Requirements 11-15_
  
  - [x] 1.5 创建测试数据生成器
    - 创建 `tests/Catga.Tests/Framework/Generators/TenantGenerators.cs`
    - 创建 `tests/Catga.Tests/Framework/Generators/SagaGenerators.cs`
    - 创建 `tests/Catga.Tests/Framework/Generators/PerformanceGenerators.cs`
    - _Requirements: Design - 测试场景数据模型_

- [x] 2. Checkpoint - 确保测试基础设施正常工作
  - 运行基础设施测试 ✅ 13/13 tests passed
  - 验证故障注入功能 ✅
  - 验证性能基准功能 ✅
  - 如有问题请询问用户


- [ ] 3. EventStore 深度验证测试 (10项)
  - [x] 3.1 创建 EventStore 深度测试文件
    - 创建 `tests/Catga.Tests/ComponentDepth/EventStoreDepthTests.cs`
    - 使用 BackendMatrixTestBase 支持所有后端
    - _Requirements: Requirement 41_
  
  - [x] 3.2 实现大数据量测试 (3项)
    - EventStore_1MillionEventsInSingleStream_HandlesCorrectly
    - EventStore_100KConcurrentStreams_HandlesCorrectly
    - EventStore_EventsWith10MBPayload_HandlesCorrectly
    - 使用 PerformanceBenchmarkFramework 测量性能
    - _Requirements: Requirement 41.1-41.3_
  
  - [x] 3.3 实现流管理测试 (3项)
    - EventStore_StreamDeletionAndRecreation_HandlesCorrectly
    - EventStore_VersionGapsDetection_HandlesCorrectly
    - EventStore_StreamMetadataAndTagging_WorksCorrectly
    - _Requirements: Requirement 41.4-41.6_
  
  - [x] 3.4 实现高级查询测试 (4项)
    - EventStore_EventFilteringByType_WorksCorrectly
    - EventStore_EventTransformationOnRead_WorksCorrectly
    - EventStore_ClockSkewInTimestamps_HandlesCorrectly
    - EventStore_SoftDeleteAndHardDelete_WorkCorrectly
    - _Requirements: Requirement 41.7-41.10_

- [-] 4. SnapshotStore 深度验证测试 (10项)
  - [x] 4.1 创建 SnapshotStore 深度测试文件
    - 创建 `tests/Catga.Tests/ComponentDepth/SnapshotStoreDepthTests.cs`
    - 使用 BackendMatrixTestBase 支持所有后端
    - _Requirements: Requirement 42_
  
  - [x] 4.2 实现大数据量测试 (2项)
    - SnapshotStore_SnapshotsWith100MBPayload_HandlesCorrectly
    - SnapshotStore_100KConcurrentAggregates_HandlesCorrectly
    - 使用 PerformanceBenchmarkFramework 测量性能
    - _Requirements: Requirement 42.1-42.2_
  
  - [x] 4.3 实现快照管理测试 (4项)
    - SnapshotStore_SnapshotVersioningAndMigration_WorksCorrectly
    - SnapshotStore_IncrementalSnapshots_WorkCorrectly
    - SnapshotStore_SnapshotCompression_WorksCorrectly
    - SnapshotStore_SnapshotExpirationAndCleanup_WorksCorrectly
    - _Requirements: Requirement 42.3-42.6_
  
  - [x] 4.4 实现快照验证测试 (4项)
    - SnapshotStore_SnapshotValidation_WorksCorrectly
    - SnapshotStore_ConcurrentSnapshotUpdates_HandlesCorrectly
    - SnapshotStore_SnapshotMetadata_WorksCorrectly
    - SnapshotStore_SnapshotStatistics_ProvidesAccurateData
    - _Requirements: Requirement 42.7-42.10_

- [-] 5. Transport 深度验证测试 (10项)
  - [ ] 5.1 创建 Transport 深度测试文件
    - 创建 `tests/Catga.Tests/ComponentDepth/TransportDepthTests.cs`
    - 使用 BackendMatrixTestBase 支持所有后端
    - _Requirements: Requirement 43_
  
  - [ ] 5.2 实现高吞吐量测试 (3项)
    - Transport_1MillionMessagesPerSecond_HandlesCorrectly
    - Transport_MessagesWith10MBPayload_HandlesCorrectly
    - Transport_10KConcurrentSubscribers_HandlesCorrectly
    - 使用 PerformanceBenchmarkFramework 测量吞吐量
    - _Requirements: Requirement 43.1-43.3_
  
  - [ ] 5.3 实现消息管理测试 (4项)
    - Transport_SubscriberBackpressure_HandlesCorrectly
    - Transport_MessageExpirationAndTTL_WorksCorrectly
    - Transport_MessagePriority_WorksCorrectly
    - Transport_MessageRoutingPatterns_WorkCorrectly
    - _Requirements: Requirement 43.4-43.7_
  
  - [ ] 5.4 实现高级功能测试 (3项)
    - Transport_SlowConsumerDetection_WorksCorrectly
    - Transport_MessageBatching_WorksCorrectly
    - Transport_DeliveryGuaranteesPerQoS_WorkCorrectly
    - _Requirements: Requirement 43.8-43.10_


- [ ] 6. FlowStore, Mediator, Pipeline 深度验证测试 (30项)
  - [ ] 6.1 创建 FlowStore 深度测试文件
    - 创建 `tests/Catga.Tests/ComponentDepth/FlowStoreDepthTests.cs`
    - 使用 BackendMatrixTestBase 支持所有后端
    - 实现 10 项深度测试 (100K并发流, 10MB状态, 版本迁移, 压缩, 过期清理, 验证, 并发更新, 元数据, 统计, 快照)
    - _Requirements: Requirement 44_
  
  - [ ] 6.2 创建 Mediator 深度测试文件
    - 创建 `tests/Catga.Tests/ComponentDepth/MediatorDepthTests.cs`
    - 实现 10 项深度测试 (100万请求/秒, 1万处理器, 注册/注销, 取消, 超时, 异常, 验证, 指标, 版本, 循环依赖)
    - _Requirements: Requirement 46_
  
  - [ ] 6.3 创建 Pipeline 深度测试文件
    - 创建 `tests/Catga.Tests/ComponentDepth/PipelineDepthTests.cs`
    - 实现 10 项深度测试 (100行为链, 异常, 短路, 异步, 取消, 指标, 条件, 依赖, 排序, 状态)
    - _Requirements: Requirement 47_

- [ ] 7. Aggregate, Saga, Projection 深度验证测试 (30项)
  - [ ] 7.1 创建 Aggregate 深度测试文件
    - 创建 `tests/Catga.Tests/ComponentDepth/AggregateDepthTests.cs`
    - 实现 10 项深度测试 (10万事件历史, 并发命令, 事件版本, 快照优化, 业务规则, 状态转换, 统计, 元数据, 删除, 迁移)
    - _Requirements: Requirement 48_
  
  - [ ] 7.2 创建 Saga 深度测试文件
    - 创建 `tests/Catga.Tests/ComponentDepth/SagaDepthTests.cs`
    - 实现 10 项深度测试 (100步工作流, 超时重试, 反向补偿, 补偿失败, 版本, 并发执行, 指标, 状态持久化, 取消, 恢复)
    - 使用 SagaGenerators 生成测试数据
    - _Requirements: Requirement 49_
  
  - [ ] 7.3 创建 Projection 深度测试文件
    - 创建 `tests/Catga.Tests/ComponentDepth/ProjectionDepthTests.cs`
    - 实现 10 项深度测试 (100万事件重建, 并发处理, 版本, 重置重建, 增量更新, 延迟检测, 统计, 验证, 错误处理, 快照)
    - _Requirements: Requirement 50_

- [ ] 8. Checkpoint - 确保单一组件深度测试全部通过
  - 运行所有 ComponentDepth 测试
  - 验证每个组件的 10 项测试都通过
  - 如有问题请询问用户

- [ ] 9. EventStore + SnapshotStore 组合测试 (6项)
  - [ ] 9.1 创建组合测试文件
    - 创建 `tests/Catga.Tests/ComponentCombination/EventStoreSnapshotStoreCombinationTests.cs`
    - 使用 ComponentCombinationTestBase<IEventStore, ISnapshotStore>
    - _Requirements: Requirement 31_
  
  - [ ] 9.2 实现基本组合测试 (3项)
    - Combination_LoadFromSnapshotThenEvents_WorksCorrectly
    - Combination_SnapshotNewerThanVersion_LoadsFromEventsOnly
    - Combination_SnapshotFailsEventsSucceed_LoadsCorrectly
    - _Requirements: Requirement 31.1-31.3_
  
  - [ ] 9.3 实现跨后端组合测试 (3项)
    - Combination_UnderLoad_MaintainsConsistency
    - Combination_AllBackends_BehaveIdentically (使用 BackendMatrixTestFramework)
    - Combination_OptimizedLoadPath_WorksCorrectly
    - _Requirements: Requirement 31.4-31.6_

- [ ] 10. EventStore + Transport 组合测试 (6项)
  - [ ] 10.1 创建组合测试文件
    - 创建 `tests/Catga.Tests/ComponentCombination/EventStoreTransportCombinationTests.cs`
    - 使用 ComponentCombinationTestBase<IEventStore, IMessageTransport>
    - _Requirements: Requirement 32_
  
  - [ ] 10.2 实现原子性测试 (3项)
    - Combination_AppendAndPublish_Atomic
    - Combination_PublishFails_UsesOutbox
    - Combination_SlowTransport_DoesNotBlockAppend
    - _Requirements: Requirement 32.1-32.3_
  
  - [ ] 10.3 实现跨后端组合测试 (3项)
    - Combination_EventReplay_RepublishesToTransport
    - Combination_AllBackendCombinations_WorkCorrectly (测试 9 种组合: 3 EventStore × 3 Transport)
    - Combination_EventOrdering_MaintainedAcrossStoreAndTransport
    - _Requirements: Requirement 32.4-32.6_

- [ ] 11. Mediator + Pipeline + Behaviors 组合测试 (6项)
  - [ ] 11.1 创建组合测试文件
    - 创建 `tests/Catga.Tests/ComponentCombination/MediatorPipelineBehaviorsCombinationTests.cs`
    - 使用 ComponentCombinationTestBase<ICatgaMediator, IPipelineBehavior, IPipelineBehavior>
    - _Requirements: Requirement 33_
  
  - [ ] 11.2 实现行为链测试 (4项)
    - Combination_5Behaviors_ExecuteInOrder
    - Combination_BehaviorShortCircuits_SkipsRemaining
    - Combination_BehaviorThrows_ExecutesExceptionBehaviors
    - Combination_BehaviorModifiesRequest_PassesToHandler
    - _Requirements: Requirement 33.1-33.4_
  
  - [ ] 11.3 实现高级功能测试 (2项)
    - Combination_AsyncBehaviorsWithCancellation_WorkCorrectly
    - Combination_BehaviorExecutionMetrics_Collected
    - _Requirements: Requirement 33.5-33.6_


- [ ] 12. Flow + EventStore + Transport 组合测试 (6项)
  - [ ] 12.1 创建组合测试文件
    - 创建 `tests/Catga.Tests/ComponentCombination/FlowEventStoreTransportCombinationTests.cs`
    - 使用 ComponentCombinationTestBase<IFlow, IEventStore, IMessageTransport>
    - 实现 6 项组合测试 (流步骤原子性, 精确一次交付, 中断恢复, 100步完整性, 后端一致性, 分布式流)
    - _Requirements: Requirement 34_

- [ ] 13. Saga + Outbox + Inbox 组合测试 (6项)
  - [ ] 13.1 创建组合测试文件
    - 创建 `tests/Catga.Tests/ComponentCombination/SagaOutboxInboxCombinationTests.cs`
    - 使用 ComponentCombinationTestBase 和 SagaGenerators
    - 实现 6 项组合测试 (补偿到Outbox, Inbox去重, 反向补偿, 跨服务协调, 超时重试, 执行追踪)
    - _Requirements: Requirement 35_

- [ ] 14. Projection + EventStore + SnapshotStore 组合测试 (6项)
  - [ ] 14.1 创建组合测试文件
    - 创建 `tests/Catga.Tests/ComponentCombination/ProjectionEventStoreSnapshotStoreCombinationTests.cs`
    - 使用 ComponentCombinationTestBase
    - 实现 6 项组合测试 (异步更新, 重建优化, 多投影更新, 延迟检测, 投影版本)
    - _Requirements: Requirement 36_

- [ ] 15. IdempotencyStore + Transport + EventStore 组合测试 (6项)
  - [ ] 15.1 创建组合测试文件
    - 创建 `tests/Catga.Tests/ComponentCombination/IdempotencyTransportEventStoreCombinationTests.cs`
    - 使用 ComponentCombinationTestBase
    - 实现 6 项组合测试 (处理前检查, 原子标记, 重复缓存, 降级处理, 后端一致性, 过期清理)
    - _Requirements: Requirement 37_

- [ ] 16. Checkpoint - 确保组件组合测试全部通过
  - 运行所有 ComponentCombination 测试
  - 验证所有组合测试通过
  - 如有问题请询问用户

- [ ] 17. 全后端矩阵测试 (27种组合)
  - [ ] 17.1 创建后端矩阵测试文件
    - 创建 `tests/Catga.Tests/BackendMatrix/AllBackendCombinationsTests.cs`
    - 使用 BackendMatrixTestBase 和 Theory + MemberData
    - _Requirements: Requirement 38_
  
  - [ ] 17.2 实现矩阵测试生成 (4项)
    - 使用 BackendMatrixTestFramework.GetAllCombinations() 生成 27 种组合
    - 每种组合运行核心测试套件 (事件追加, 消息发布, 流程执行, 快照保存)
    - Matrix_AllCombinations_CoreOperationsWork
    - Matrix_AllCombinations_PerformanceAcceptable
    - _Requirements: Requirement 38.1-38.4_
  
  - [ ] 17.3 实现一致性验证 (2项)
    - Matrix_AllCombinations_MaintainConsistency
    - Matrix_ConfigurationValidation_WorksCorrectly
    - _Requirements: Requirement 38.5-38.6_

- [ ] 18. 序列化器组合测试 (6项)
  - [ ] 18.1 创建序列化器组合测试文件
    - 创建 `tests/Catga.Tests/BackendMatrix/SerializerCombinationTests.cs`
    - 测试 JSON 和 MemoryPack 序列化器
    - _Requirements: Requirement 39_
  
  - [ ] 18.2 实现序列化器测试 (6项)
    - Serializer_JSON_AllComponents_WorkCorrectly
    - Serializer_MemoryPack_AllComponents_WorkCorrectly
    - Serializer_Mixed_WorksCorrectly (事件用JSON, 消息用MemoryPack)
    - Serializer_VersionMismatch_HandlesGracefully
    - Serializer_PerformanceMetrics_Collected (使用 PerformanceBenchmarkFramework)
    - Serializer_Custom_Supported
    - _Requirements: Requirement 39.1-39.6_

- [ ] 19. Pipeline Behaviors 组合链测试 (6项)
  - [ ] 19.1 创建行为链测试文件
    - 创建 `tests/Catga.Tests/BackendMatrix/PipelineBehaviorChainTests.cs`
    - _Requirements: Requirement 40_
  
  - [ ] 19.2 实现复杂行为链测试 (6项)
    - BehaviorChain_ValidationLoggingRetryTimeoutIdempotency_WorksCorrectly
    - BehaviorChain_ConfiguredOrder_Respected
    - BehaviorChain_ConditionalExecution_WorksCorrectly
    - BehaviorChain_Dependencies_Resolved
    - BehaviorChain_ExecutionTracing_Available
    - BehaviorChain_DynamicRegistration_Supported
    - _Requirements: Requirement 40.1-40.6_

- [ ] 20. Checkpoint - 确保后端矩阵测试全部通过
  - 运行所有 BackendMatrix 测试
  - 验证 27 种组合都通过
  - 如有问题请询问用户

- [ ] 21. 多租户订单系统 E2E 测试 (6项)
  - [ ] 21.1 创建多租户 E2E 测试文件
    - 创建 `tests/Catga.Tests/ComplexE2E/MultiTenantOrderSystemE2ETests.cs`
    - 使用 TenantGenerators 生成测试数据
    - _Requirements: Requirement 1_
  
  - [ ] 21.2 实现多租户隔离测试 (3项)
    - MultiTenant_ConcurrentOrders_DataIsolated
    - MultiTenant_QueryOrders_OnlyOwnData
    - MultiTenant_OrderFails_NoImpactOnOtherTenants
    - _Requirements: Requirement 1.1-1.3_
  
  - [ ] 21.3 实现配额和流隔离测试 (3项)
    - MultiTenant_ExceedsQuota_RejectsNewOrders
    - MultiTenant_SeparateEventStreams_Maintained
    - MultiTenant_TenantSpecificConfiguration_Supported
    - _Requirements: Requirement 1.4-1.6_

- [ ] 22. 分布式事务 Saga E2E 测试 (6项)
  - [ ] 22.1 创建分布式 Saga E2E 测试文件
    - 创建 `tests/Catga.Tests/ComplexE2E/DistributedSagaE2ETests.cs`
    - 使用 SagaGenerators 生成测试数据
    - _Requirements: Requirement 2_
  
  - [ ] 22.2 实现 Saga 执行测试 (3项)
    - DistributedSaga_5Steps_ExecutesInOrder
    - DistributedSaga_Step3Fails_CompensatesStep2And1
    - DistributedSaga_CompensationFails_RetriesWithBackoff
    - _Requirements: Requirement 2.1-2.3_
  
  - [ ] 22.3 实现 Saga 超时和并发测试 (3项)
    - DistributedSaga_Timeout_TriggersCompensation
    - DistributedSaga_ConcurrentSagas_NoInterference
    - DistributedSaga_StatePersistedAtEachStep_ForRecovery
    - _Requirements: Requirement 2.4-2.6_


- [ ] 23. 事件溯源时间旅行 E2E 测试 (6项)
  - [ ] 23.1 创建时间旅行 E2E 测试文件
    - 创建 `tests/Catga.Tests/ComplexE2E/EnhancedTimeTravelE2ETests.cs`
    - 使用 PerformanceGenerators.TimeTravelQuery() 生成测试数据
    - 注意: 已存在基础 TimeTravelE2ETests, 此为增强版
    - _Requirements: Requirement 3_
  
  - [ ] 23.2 实现时间旅行查询测试 (3项)
    - TimeTravel_QueryAtTimestampT_ReturnsCorrectState
    - TimeTravel_ReplayEventsT1ToT2_ProducesIntermediateStates
    - TimeTravel_CompareStates_ShowsAccurateDifferences
    - _Requirements: Requirement 3.1-3.3_
  
  - [ ] 23.3 实现大规模和多聚合测试 (3项)
    - TimeTravel_10KEvents_EfficientQuery (使用 PerformanceBenchmarkFramework)
    - TimeTravel_MultipleAggregates_SameTimestamp
    - TimeTravel_TimezoneConversions_HandledCorrectly
    - _Requirements: Requirement 3.4-3.6_

- [ ] 24. 流程编排复杂场景 E2E 测试 (6项)
  - [ ] 24.1 创建复杂流程 E2E 测试文件
    - 创建 `tests/Catga.Tests/ComplexE2E/ComplexFlowOrchestrationE2ETests.cs`
    - _Requirements: Requirement 4_
  
  - [ ] 24.2 实现复杂流程测试 (3项)
    - ComplexFlow_NestedParallelBranches_ExecutesCorrectly
    - ComplexFlow_ConditionalLoops_HandlesTermination
    - ComplexFlow_DynamicStepGeneration_ExecutesGeneratedSteps
    - _Requirements: Requirement 4.1-4.3_
  
  - [ ] 24.3 实现暂停和大规模流程测试 (3项)
    - ComplexFlow_PausesForExternalInput_ResumesCorrectly
    - ComplexFlow_50PlusSteps_NoPerformanceDegradation (使用 PerformanceBenchmarkFramework)
    - ComplexFlow_VersioningAndMigration_Supported
    - _Requirements: Requirement 4.4-4.6_

- [ ] 25. 读写分离 CQRS E2E 测试 (6项)
  - [ ] 25.1 创建读写分离 E2E 测试文件
    - 创建 `tests/Catga.Tests/ComplexE2E/ReadWriteSeparationE2ETests.cs`
    - _Requirements: Requirement 5_
  
  - [ ] 25.2 实现读写分离测试 (3项)
    - ReadWriteSeparation_CommandUpdates_ReadModelsAsyncUpdated
    - ReadWriteSeparation_ReadModelUpdateFails_Retries
    - ReadWriteSeparation_QueryDuringUpdate_EventuallyConsistent
    - _Requirements: Requirement 5.1-5.3_
  
  - [ ] 25.3 实现多读模型和重建测试 (3项)
    - ReadWriteSeparation_MultipleReadModels_AllUpdated
    - ReadWriteSeparation_RebuildFromEventHistory_Supported
    - ReadWriteSeparation_DetectAndHandleReadModelDrift
    - _Requirements: Requirement 5.4-5.6_

- [ ] 26. Checkpoint - 确保复杂 E2E 测试全部通过
  - 运行所有 ComplexE2E 测试
  - 验证所有场景测试通过
  - 如有问题请询问用户

- [ ] 27. 网络故障恢复测试 (6项)
  - [ ] 27.1 创建网络故障测试文件
    - 创建 `tests/Catga.Tests/Resilience/NetworkFailureRecoveryTests.cs`
    - 使用 FaultInjectionMiddleware 注入网络故障
    - _Requirements: Requirement 6_
  
  - [ ] 27.2 实现故障恢复测试 (6项)
    - NetworkFailure_RedisDuringAppend_RetriesAndSucceeds
    - NetworkFailure_NATSDuringPublish_BuffersAndReplays
    - NetworkFailure_SlowNetwork_TimeoutsAndRetries (使用 FaultType.SlowOperation)
    - NetworkFailure_Recovers_ResumesAutomatically
    - NetworkFailure_NoDataLoss_Guaranteed
    - NetworkFailure_LogsForMonitoring_Available
    - _Requirements: Requirement 6.1-6.6_

- [ ] 28. 部分失败处理测试 (6项)
  - [ ] 28.1 创建部分失败测试文件
    - 创建 `tests/Catga.Tests/Resilience/PartialFailureHandlingTests.cs`
    - 使用 FaultInjectionMiddleware.InjectFault(FaultType.PartialFailure)
    - _Requirements: Requirement 7_
  
  - [ ] 28.2 实现部分失败测试 (6项)
    - PartialFailure_1Of3HandlersFails_Other2Execute
    - PartialFailure_SnapshotSaveFails_EventAppendContinues
    - PartialFailure_ReadModelUpdateFails_DoesNotBlockCommand
    - PartialFailure_OutboxPublishFails_RetriesFailedOnly
    - PartialFailure_IsolatesFailures_PreventsCascade
    - PartialFailure_CircuitBreaker_ForFailingDependencies
    - _Requirements: Requirement 7.1-7.6_

- [ ] 29. 数据损坏恢复测试 (6项)
  - [ ] 29.1 创建数据损坏测试文件
    - 创建 `tests/Catga.Tests/Resilience/DataCorruptionRecoveryTests.cs`
    - 使用 FaultInjectionMiddleware.InjectFault(FaultType.DataCorruption)
    - _Requirements: Requirement 8_
  
  - [ ] 29.2 实现数据损坏测试 (6项)
    - DataCorruption_EventDataCorrupted_DetectsAndSkips
    - DataCorruption_SnapshotCorrupted_FallsBackToEventReplay
    - DataCorruption_VersionMismatch_HandlesGracefully
    - DataCorruption_DeserializationFails_LogsAndContinues (使用 FaultType.SerializationError)
    - DataCorruption_ValidationBeforePersistence_Provided
    - DataCorruption_ManualRepairTools_Supported
    - _Requirements: Requirement 8.1-8.6_

- [ ] 30. 资源耗尽处理测试 (6项)
  - [ ] 30.1 创建资源耗尽测试文件
    - 创建 `tests/Catga.Tests/Resilience/ResourceExhaustionHandlingTests.cs`
    - 使用 FaultInjectionMiddleware.InjectFault(FaultType.ResourceExhaustion)
    - _Requirements: Requirement 9_
  
  - [ ] 30.2 实现资源耗尽测试 (6项)
    - ResourceExhaustion_LowMemory_ReducesCacheAndContinues
    - ResourceExhaustion_ConnectionPoolExhausted_QueuesRequests
    - ResourceExhaustion_DiskFull_RejectsWritesWithClearError
    - ResourceExhaustion_CPUSaturated_ThrottlesIncomingRequests
    - ResourceExhaustion_BackpressureMechanisms_Provided
    - ResourceExhaustion_RecoveryWhenAvailable_Automatic
    - _Requirements: Requirement 9.1-9.6_

- [ ] 31. 并发冲突解决测试 (6项)
  - [ ] 31.1 创建并发冲突测试文件
    - 创建 `tests/Catga.Tests/Resilience/ConcurrencyConflictResolutionTests.cs`
    - 使用 FaultInjectionMiddleware.InjectFault(FaultType.VersionConflict)
    - _Requirements: Requirement 10_
  
  - [ ] 31.2 实现并发冲突测试 (6项)
    - ConcurrencyConflict_10ClientsUpdateSameAggregate_ResolvesCorrectly
    - ConcurrencyConflict_OptimisticLockingFails_RetriesWithBackoff
    - ConcurrencyConflict_DistributedLockTimeout_ReleasesAndRetries
    - ConcurrencyConflict_VersionConflict_ProvidesDetails
    - ConcurrencyConflict_CustomResolutionStrategies_Supported
    - ConcurrencyConflict_PreventsLostUpdates_AllScenarios
    - _Requirements: Requirement 10.1-10.6_

- [ ] 32. Checkpoint - 确保弹性测试全部通过
  - 运行所有 Resilience 测试
  - 验证所有弹性测试通过
  - 如有问题请询问用户


- [ ] 33. 性能回归测试套件 (30项)
  - [ ] 33.1 创建性能回归测试文件
    - 创建 `tests/Catga.Tests/PerformanceRegression/ThroughputRegressionTests.cs`
    - 创建 `tests/Catga.Tests/PerformanceRegression/LatencyRegressionTests.cs`
    - 创建 `tests/Catga.Tests/PerformanceRegression/MemoryRegressionTests.cs`
    - 创建 `tests/Catga.Tests/PerformanceRegression/StartupTimeRegressionTests.cs`
    - 所有测试使用 PerformanceBenchmarkFramework
    - _Requirements: Requirements 11-14_
  
  - [ ] 33.2 实现吞吐量回归测试 (6项)
    - ThroughputRegression_InMemoryEventStore_Maintains100KOpsPerSec
    - ThroughputRegression_RedisEventStore_Maintains10KOpsPerSec
    - ThroughputRegression_NATSEventStore_Maintains10KOpsPerSec
    - ThroughputRegression_InMemoryTransport_Maintains100KMsgsPerSec
    - ThroughputRegression_SustainedLoad1Hour_MaintainsThroughput
    - ThroughputRegression_Degradation_AlertsWhenOver10Percent
    - 使用 PerformanceBenchmarkFramework.MeasureBaseline() 和 AssertNoRegression()
    - _Requirements: Requirement 11.1-11.6_
  
  - [ ] 33.3 实现延迟回归测试 (6项)
    - LatencyRegression_InMemoryOps_Under1msP99
    - LatencyRegression_RedisOps_Under10msP99
    - LatencyRegression_NATSOps_Under10msP99
    - LatencyRegression_UnderConcurrentLoad_MaintainsLatency
    - LatencyRegression_Spikes_LogsWarnings
    - LatencyRegression_Percentiles_P50P95P99P999_Provided
    - _Requirements: Requirement 12.1-12.6_
  
  - [ ] 33.4 实现内存回归测试 (6项)
    - MemoryRegression_NoLeaks_UnderSustainedLoad
    - MemoryRegression_ReleasesMemory_AfterLargeBatches
    - MemoryRegression_StableMemory_Over24Hours (长时间运行测试)
    - MemoryRegression_1MillionEvents_NoExcessiveGrowth
    - MemoryRegression_MemoryMetrics_Provided
    - MemoryRegression_MemoryLeaks_DetectedAndAlertsed
    - _Requirements: Requirement 13.1-13.6_
  
  - [ ] 33.5 实现启动时间回归测试 (6项)
    - StartupTimeRegression_InMemory_Under5Seconds
    - StartupTimeRegression_Redis_Under10Seconds
    - StartupTimeRegression_NATS_Under10Seconds
    - StartupTimeRegression_ParallelInitialization_Supported
    - StartupTimeRegression_ProgressLogging_Provided
    - StartupTimeRegression_SlowStartup_AlertsWhenOver20Seconds
    - _Requirements: Requirement 14.1-14.6_
  
  - [ ] 33.6 实现大数据量性能测试 (6项)
    - LargeDataPerformance_100KEventsAggregate_Efficient
    - LargeDataPerformance_100MBSnapshots_Efficient
    - LargeDataPerformance_1MillionEventsQuery_Efficient
    - LargeDataPerformance_10KConcurrentFlows_Efficient
    - LargeDataPerformance_Pagination_ForLargeResults
    - LargeDataPerformance_QueryOptimization_WithIndexes
    - _Requirements: Requirement 15.1-15.6_

- [ ] 34. 混沌工程和故障注入测试 (12项)
  - [ ] 34.1 创建混沌工程测试文件
    - 创建 `tests/Catga.Tests/Resilience/ChaosEngineeringTests.cs`
    - 使用 FaultInjectionMiddleware 的所有故障类型
    - _Requirements: Requirement 16_
  
  - [ ] 34.2 实现混沌测试 (6项)
    - Chaos_Random10PercentRedisFailures_MaintainsCorrectness
    - Chaos_RandomNATSDelays1To5Seconds_HandlesGracefully
    - Chaos_RandomNetworkPartitions_RecovesAutomatically
    - Chaos_RandomCPUSpikes_ThrottlesAndRecovers
    - Chaos_DataConsistency_MaintainedUnderChaos
    - Chaos_TestingFramework_Provided (验证 FaultInjectionMiddleware 功能)
    - _Requirements: Requirement 16.1-16.6_
  
  - [ ] 34.3 创建故障注入测试文件
    - 创建 `tests/Catga.Tests/Resilience/FaultInjectionTests.cs`
    - _Requirements: Requirement 17_
  
  - [ ] 34.4 实现故障注入测试 (6项)
    - FaultInjection_ConnectionFailures_Supported (FaultType.ConnectionFailure)
    - FaultInjection_TimeoutFailures_Supported (FaultType.NetworkTimeout)
    - FaultInjection_SerializationFailures_Supported (FaultType.SerializationError)
    - FaultInjection_VersionConflicts_Supported (FaultType.VersionConflict)
    - FaultInjection_ResourceExhaustion_Supported (FaultType.ResourceExhaustion)
    - FaultInjection_API_Provided (验证 FaultInjectionMiddleware API)
    - _Requirements: Requirement 17.1-17.6_

- [ ] 35. 灾难恢复和长时间运行测试 (12项)
  - [ ] 35.1 创建灾难恢复测试文件
    - 创建 `tests/Catga.Tests/Resilience/DisasterRecoveryTests.cs`
    - _Requirements: Requirement 18_
  
  - [ ] 35.2 实现灾难恢复测试 (6项)
    - DisasterRecovery_RedisCrashes_RecovesFromBackup
    - DisasterRecovery_NATSClusterFails_FailoversToStandby
    - DisasterRecovery_DataCenterFails_FailoversToDRSite
    - DisasterRecovery_CorruptionDetected_RestoresFromLastGoodState
    - DisasterRecovery_BackupAndRestoreTools_Provided
    - DisasterRecovery_DRProcedures_TestedRegularly
    - _Requirements: Requirement 18.1-18.6_
  
  - [ ] 35.3 创建长时间运行测试文件
    - 创建 `tests/Catga.Tests/Resilience/LongRunningStabilityTests.cs`
    - 注意: 这些测试运行时间长，应标记为 [Trait("Category", "LongRunning")]
    - _Requirements: Requirement 19_
  
  - [ ] 35.4 实现长时间运行测试 (6项)
    - LongRunning_24Hours_NoErrors (可选: 使用较短时间如1小时进行测试)
    - LongRunning_7Days_NoMemoryLeaks (可选: 使用较短时间如24小时进行测试)
    - LongRunning_1BillionEvents_NoDegradation (可选: 使用较少事件如100万进行测试)
    - LongRunning_PerformanceMaintained_OverTime
    - LongRunning_AnomalyDetection_LogsIssues
    - LongRunning_HealthCheckEndpoints_Provided
    - _Requirements: Requirement 19.1-19.6_

- [ ] 36. 升级和迁移测试 (6项)
  - [ ] 36.1 创建升级迁移测试文件
    - 创建 `tests/Catga.Tests/Resilience/UpgradeAndMigrationTests.cs`
    - _Requirements: Requirement 20_
  
  - [ ] 36.2 实现升级迁移测试 (6项)
    - UpgradeAndMigration_VersionNToNPlus1_MigratesDataCorrectly
    - UpgradeAndMigration_RollingUpgrade_MaintainsAvailability
    - UpgradeAndMigration_SchemaChanges_HandlesOldAndNewFormats
    - UpgradeAndMigration_DowngradeNeeded_RollbacksSafely
    - UpgradeAndMigration_ZeroDowntimePath_Provided
    - UpgradeAndMigration_DataValidation_AfterMigration
    - _Requirements: Requirement 20.1-20.6_

- [ ] 37. Final Checkpoint - 确保所有增强测试通过
  - 运行完整测试套件（现有 + 新增）
  - 验证测试总数达到 2700+
  - 验证 100% 通过率
  - 生成测试覆盖率报告
  - 生成性能基准报告
  - 如有问题请询问用户

## Notes

- 所有任务都是必须执行的
- 每个 Checkpoint 任务用于验证阶段性成果
- 测试执行顺序：单一组件深度 → 组件组合 → 后端矩阵 → 复杂 E2E → 弹性测试 → 性能回归
- 预计新增测试数量：约 500+ 测试
- 目标总测试数：2700+ 测试
- 目标通过率：100%
- [x] 标记表示该任务已完成
- [ ] 标记表示该任务尚未开始

## 测试分类统计

### 单一组件深度测试 (100项)
- EventStore: 10项
- SnapshotStore: 10项
- Transport: 10项
- FlowStore: 10项
- IdempotencyStore: 10项
- Mediator: 10项
- Pipeline: 10项
- Aggregate: 10项
- Saga: 10项
- Projection: 10项

### 组件组合测试 (50项)
- EventStore + SnapshotStore: 6项
- EventStore + Transport: 6项
- Mediator + Pipeline + Behaviors: 6项
- Flow + EventStore + Transport: 6项
- Saga + Outbox + Inbox: 6项
- Projection + EventStore + SnapshotStore: 6项
- IdempotencyStore + Transport + EventStore: 6项
- 其他组合: 8项

### 后端矩阵测试 (50项)
- 27种后端组合: 27项
- 序列化器组合: 6项
- Pipeline Behaviors 组合链: 6项
- 其他矩阵测试: 11项

### 复杂 E2E 场景测试 (30项)
- 多租户订单系统: 6项
- 分布式事务 Saga: 6项
- 事件溯源时间旅行: 6项
- 流程编排复杂场景: 6项
- 读写分离 CQRS: 6项

### 弹性测试 (60项)
- 网络故障恢复: 6项
- 部分失败处理: 6项
- 数据损坏恢复: 6项
- 资源耗尽处理: 6项
- 并发冲突解决: 6项
- 混沌工程: 6项
- 故障注入: 6项
- 灾难恢复: 6项
- 长时间运行: 6项
- 升级和迁移: 6项

### 性能回归测试 (36项)
- 吞吐量回归: 6项
- 延迟回归: 6项
- 内存回归: 6项
- 启动时间回归: 6项
- 大数据量性能: 6项
- 其他性能测试: 6项

**总计新增测试**: 约 326 项核心测试 + 约 200 项辅助测试 = 526 项测试
**预期总测试数**: 2200 (现有) + 526 (新增) = 2726 测试

## 更新说明 (2024-12-23)

### 已完成 (Tasks 1-2)
✅ **测试基础设施增强** - 所有框架组件已实现并验证
- ComponentCombinationTestBase (2组件和3组件版本)
- BackendMatrixTestFramework (27种后端组合生成)
- FaultInjectionMiddleware (8种故障类型)
- PerformanceBenchmarkFramework (性能测量和回归检测)
- 测试数据生成器 (TenantGenerators, SagaGenerators, PerformanceGenerators)
- 13/13 框架验证测试通过

### 当前状态分析
**现有测试基础**:
- 2,200+ 现有测试已通过
- 测试框架基础设施完整 (Tasks 1-2 完成)
- 现有测试目录: E2E, Integration, PropertyTests, Resilience, LoadTests 等
- 部分弹性测试已存在但不够全面

**需要实现的测试类别**:
1. **ComponentDepth** (100项) - 单一组件深度验证，需新建目录
2. **ComponentCombination** (42项) - 组件组合测试，需新建目录  
3. **BackendMatrix** (39项) - 后端矩阵测试，需新建目录
4. **ComplexE2E** (30项) - 复杂E2E场景，需新建目录
5. **Resilience** (60项) - 增强弹性测试，扩展现有目录
6. **PerformanceRegression** (36项) - 性能回归测试，需新建目录

**任务优化**:
- 移除了 IdempotencyStore 深度测试 (Task 6 中的一部分)，因为已有充分的 PropertyTests 覆盖
- 简化了任务结构，将相关测试合并到更少的文件中
- 保持了所有 50 个需求的覆盖

### 关键技术决策
- **ComponentCombinationTestBase**: 用于所有组件组合测试，提供统一的生命周期管理
- **BackendMatrixTestBase**: 用于需要跨所有后端验证的测试
- **FaultInjectionMiddleware**: 8种故障类型覆盖所有弹性测试场景
- **PerformanceBenchmarkFramework**: 统一的性能测量和回归检测
- **测试分类标签**: 使用 [Trait] 属性进行测试分类和过滤

### 下一步
开始实现 Task 3: EventStore 深度验证测试 (10项)
