# Requirements Document

## Introduction

本文档定义了对 Catga CQRS 框架进行全面 TDD 验证测试的需求。采用测试驱动开发（TDD）模式，对 InMemory、Redis、NATS 三种后端的所有组件进行系统性测试，确保代码正确性和一致性。

## Glossary

- **Catga**: 高性能、100% AOT 兼容的分布式 CQRS 框架
- **TDD**: 测试驱动开发，先写测试再验证实现
- **InMemory**: 内存存储后端，用于开发和测试
- **Redis**: Redis 分布式存储后端
- **NATS**: NATS JetStream 分布式存储后端
- **Store**: 存储接口实现（EventStore, SnapshotStore, IdempotencyStore 等）
- **Transport**: 消息传输接口实现
- **Flow**: 工作流引擎组件
- **Property_Test**: 基于属性的测试，验证代码在各种输入下的行为

## Requirements

---

## Part 1: InMemory 后端全面测试

### Requirement 1: InMemoryEventStore TDD 测试

**User Story:** As a developer, I want to verify InMemoryEventStore works correctly, so that I can use it for development and testing.

#### Acceptance Criteria

##### 1.1 基本 CRUD 操作
1. THE InMemoryEventStore SHALL append events to a stream successfully
2. THE InMemoryEventStore SHALL read events from a stream in correct order
3. THE InMemoryEventStore SHALL read events from a specific version
4. THE InMemoryEventStore SHALL return empty list for non-existent stream
5. THE InMemoryEventStore SHALL support reading all events across streams

##### 1.2 版本控制
6. THE InMemoryEventStore SHALL track stream version correctly after each append
7. THE InMemoryEventStore SHALL reject append with wrong expected version (optimistic concurrency)
8. THE InMemoryEventStore SHALL support ExpectedVersion.Any for unconditional append
9. THE InMemoryEventStore SHALL support ExpectedVersion.NoStream for new streams
10. THE InMemoryEventStore SHALL throw ConcurrencyException on version conflict

##### 1.3 边界条件
11. THE InMemoryEventStore SHALL handle null stream ID gracefully
12. THE InMemoryEventStore SHALL handle empty event list append
13. THE InMemoryEventStore SHALL handle very long stream IDs (>1000 chars)
14. THE InMemoryEventStore SHALL handle stream with 100,000+ events
15. THE InMemoryEventStore SHALL handle concurrent appends to same stream
16. THE InMemoryEventStore SHALL handle concurrent appends to different streams

##### 1.4 属性测试
17. FOR ALL event sequences, appending then reading SHALL return identical events
18. FOR ALL streams, version SHALL equal total event count
19. FOR ALL concurrent operations, no events SHALL be lost or duplicated

### Requirement 2: InMemorySnapshotStore TDD 测试

**User Story:** As a developer, I want to verify InMemorySnapshotStore works correctly, so that I can optimize aggregate loading.

#### Acceptance Criteria

##### 2.1 基本操作
1. THE InMemorySnapshotStore SHALL save snapshot successfully
2. THE InMemorySnapshotStore SHALL load latest snapshot for aggregate
3. THE InMemorySnapshotStore SHALL return null for non-existent aggregate
4. THE InMemorySnapshotStore SHALL overwrite older snapshot with newer version
5. THE InMemorySnapshotStore SHALL delete snapshot successfully

##### 2.2 版本管理
6. THE InMemorySnapshotStore SHALL store snapshot with correct version
7. THE InMemorySnapshotStore SHALL load snapshot at specific version
8. THE InMemorySnapshotStore SHALL reject snapshot with lower version than existing

##### 2.3 边界条件
9. THE InMemorySnapshotStore SHALL handle null aggregate ID
10. THE InMemorySnapshotStore SHALL handle very large snapshot data (>10MB)
11. THE InMemorySnapshotStore SHALL handle concurrent save operations
12. THE InMemorySnapshotStore SHALL handle snapshot with complex nested objects

##### 2.4 属性测试
13. FOR ALL snapshots, saving then loading SHALL return identical data
14. FOR ALL aggregates, only latest snapshot SHALL be retrievable

### Requirement 3: InMemoryIdempotencyStore TDD 测试

**User Story:** As a developer, I want to verify InMemoryIdempotencyStore prevents duplicate processing.

#### Acceptance Criteria

##### 3.1 基本操作
1. THE InMemoryIdempotencyStore SHALL mark message as processed
2. THE InMemoryIdempotencyStore SHALL check if message was processed
3. THE InMemoryIdempotencyStore SHALL return false for unprocessed message
4. THE InMemoryIdempotencyStore SHALL return true for processed message

##### 3.2 过期处理
5. THE InMemoryIdempotencyStore SHALL support TTL for processed records
6. THE InMemoryIdempotencyStore SHALL remove expired records automatically
7. THE InMemoryIdempotencyStore SHALL allow reprocessing after expiration

##### 3.3 边界条件
8. THE InMemoryIdempotencyStore SHALL handle null message ID
9. THE InMemoryIdempotencyStore SHALL handle very long message IDs
10. THE InMemoryIdempotencyStore SHALL handle concurrent check-and-mark operations
11. THE InMemoryIdempotencyStore SHALL handle 1,000,000+ stored IDs

##### 3.4 属性测试
12. FOR ALL message IDs, marking then checking SHALL return true
13. FOR ALL concurrent operations, each ID SHALL be processed exactly once

### Requirement 4: InMemoryMessageTransport TDD 测试

**User Story:** As a developer, I want to verify InMemoryMessageTransport delivers messages correctly.

#### Acceptance Criteria

##### 4.1 发布/订阅
1. THE InMemoryMessageTransport SHALL publish message to topic
2. THE InMemoryMessageTransport SHALL deliver message to all subscribers
3. THE InMemoryMessageTransport SHALL support multiple subscribers per topic
4. THE InMemoryMessageTransport SHALL support wildcard topic subscriptions
5. THE InMemoryMessageTransport SHALL unsubscribe successfully

##### 4.2 请求/响应
6. THE InMemoryMessageTransport SHALL send request and receive response
7. THE InMemoryMessageTransport SHALL timeout if no response received
8. THE InMemoryMessageTransport SHALL handle request cancellation

##### 4.3 消息顺序
9. THE InMemoryMessageTransport SHALL deliver messages in order per topic
10. THE InMemoryMessageTransport SHALL maintain FIFO order for single publisher

##### 4.4 边界条件
11. THE InMemoryMessageTransport SHALL handle null topic
12. THE InMemoryMessageTransport SHALL handle empty message payload
13. THE InMemoryMessageTransport SHALL handle very large messages (>1MB)
14. THE InMemoryMessageTransport SHALL handle 10,000+ concurrent subscribers
15. THE InMemoryMessageTransport SHALL handle subscriber throwing exception
16. THE InMemoryMessageTransport SHALL handle rapid subscribe/unsubscribe

##### 4.5 属性测试
17. FOR ALL published messages, all active subscribers SHALL receive them
18. FOR ALL request-response pairs, response SHALL match request correlation

### Requirement 5: InMemoryDslFlowStore TDD 测试

**User Story:** As a developer, I want to verify InMemoryDslFlowStore persists flow state correctly.

#### Acceptance Criteria

##### 5.1 基本操作
1. THE InMemoryDslFlowStore SHALL save flow state
2. THE InMemoryDslFlowStore SHALL load flow state by ID
3. THE InMemoryDslFlowStore SHALL update existing flow state
4. THE InMemoryDslFlowStore SHALL delete flow state
5. THE InMemoryDslFlowStore SHALL list all flow states

##### 5.2 查询功能
6. THE InMemoryDslFlowStore SHALL query flows by status
7. THE InMemoryDslFlowStore SHALL query flows by type
8. THE InMemoryDslFlowStore SHALL query flows by date range

##### 5.3 检查点和恢复
9. THE InMemoryDslFlowStore SHALL save checkpoint
10. THE InMemoryDslFlowStore SHALL restore from checkpoint
11. THE InMemoryDslFlowStore SHALL handle checkpoint versioning

##### 5.4 边界条件
12. THE InMemoryDslFlowStore SHALL handle null flow ID
13. THE InMemoryDslFlowStore SHALL handle complex nested flow state
14. THE InMemoryDslFlowStore SHALL handle concurrent updates to same flow
15. THE InMemoryDslFlowStore SHALL handle 100,000+ stored flows

##### 5.5 属性测试
16. FOR ALL flow states, saving then loading SHALL return identical state
17. FOR ALL checkpoints, restore SHALL produce consistent state

### Requirement 6: InMemory Inbox/Outbox Store TDD 测试

**User Story:** As a developer, I want to verify InMemory Inbox/Outbox stores work correctly for reliable messaging.

#### Acceptance Criteria

##### 6.1 Outbox 操作
1. THE MemoryOutboxStore SHALL add message to outbox
2. THE MemoryOutboxStore SHALL mark message as sent
3. THE MemoryOutboxStore SHALL retrieve pending messages
4. THE MemoryOutboxStore SHALL delete sent messages after retention period

##### 6.2 Inbox 操作
5. THE MemoryInboxStore SHALL add message to inbox
6. THE MemoryInboxStore SHALL mark message as processed
7. THE MemoryInboxStore SHALL check for duplicate messages
8. THE MemoryInboxStore SHALL retrieve unprocessed messages

##### 6.3 边界条件
9. THE Inbox/Outbox SHALL handle concurrent add operations
10. THE Inbox/Outbox SHALL handle message ordering
11. THE Inbox/Outbox SHALL handle very large message payloads

##### 6.4 属性测试
12. FOR ALL outbox messages, pending then sent SHALL transition correctly
13. FOR ALL inbox messages, duplicate detection SHALL be accurate

---

## Part 2: Redis 后端全面测试

### Requirement 7: RedisEventStore TDD 测试

**User Story:** As a developer, I want to verify RedisEventStore works correctly for production use.

#### Acceptance Criteria

##### 7.1 基本 CRUD 操作
1. THE RedisEventStore SHALL append events to a stream successfully
2. THE RedisEventStore SHALL read events from a stream in correct order
3. THE RedisEventStore SHALL read events from a specific version
4. THE RedisEventStore SHALL return empty list for non-existent stream
5. THE RedisEventStore SHALL support reading all events across streams

##### 7.2 版本控制
6. THE RedisEventStore SHALL track stream version correctly
7. THE RedisEventStore SHALL reject append with wrong expected version
8. THE RedisEventStore SHALL use Redis transactions for atomicity
9. THE RedisEventStore SHALL support optimistic locking with WATCH

##### 7.3 Redis 特定功能
10. THE RedisEventStore SHALL use correct key prefix
11. THE RedisEventStore SHALL support key expiration if configured
12. THE RedisEventStore SHALL handle Redis connection failure gracefully
13. THE RedisEventStore SHALL reconnect automatically after failure

##### 7.4 边界条件
14. THE RedisEventStore SHALL handle stream with 1,000,000+ events
15. THE RedisEventStore SHALL handle concurrent appends from multiple clients
16. THE RedisEventStore SHALL handle Redis cluster failover
17. THE RedisEventStore SHALL handle network partition scenarios

##### 7.5 属性测试
18. FOR ALL event sequences, Redis storage SHALL be identical to InMemory behavior
19. FOR ALL concurrent operations, Redis transactions SHALL ensure consistency

### Requirement 8: RedisSnapshotStore TDD 测试

**User Story:** As a developer, I want to verify RedisSnapshotStore works correctly.

#### Acceptance Criteria

##### 8.1 基本操作
1. THE RedisSnapshotStore SHALL save snapshot to Redis
2. THE RedisSnapshotStore SHALL load snapshot from Redis
3. THE RedisSnapshotStore SHALL delete snapshot from Redis
4. THE RedisSnapshotStore SHALL handle snapshot versioning

##### 8.2 Redis 特定功能
5. THE RedisSnapshotStore SHALL use correct key structure
6. THE RedisSnapshotStore SHALL support TTL for snapshots
7. THE RedisSnapshotStore SHALL handle large snapshots efficiently

##### 8.3 边界条件
8. THE RedisSnapshotStore SHALL handle Redis connection failure
9. THE RedisSnapshotStore SHALL handle concurrent snapshot updates
10. THE RedisSnapshotStore SHALL handle snapshot data >100MB

##### 8.4 属性测试
11. FOR ALL snapshots, Redis behavior SHALL match InMemory behavior

### Requirement 9: RedisIdempotencyStore TDD 测试

**User Story:** As a developer, I want to verify RedisIdempotencyStore prevents duplicates in distributed scenarios.

#### Acceptance Criteria

##### 9.1 基本操作
1. THE RedisIdempotencyStore SHALL mark message as processed with TTL
2. THE RedisIdempotencyStore SHALL check if message was processed
3. THE RedisIdempotencyStore SHALL use Redis SET NX for atomic check-and-set

##### 9.2 分布式场景
4. THE RedisIdempotencyStore SHALL prevent duplicates across multiple instances
5. THE RedisIdempotencyStore SHALL handle race conditions correctly
6. THE RedisIdempotencyStore SHALL expire records after TTL

##### 9.3 边界条件
7. THE RedisIdempotencyStore SHALL handle Redis connection failure
8. THE RedisIdempotencyStore SHALL handle very high throughput (10,000+ ops/sec)

##### 9.4 属性测试
9. FOR ALL distributed operations, exactly-once semantics SHALL be maintained

### Requirement 10: RedisMessageTransport TDD 测试

**User Story:** As a developer, I want to verify RedisMessageTransport works correctly.

#### Acceptance Criteria

##### 10.1 发布/订阅
1. THE RedisMessageTransport SHALL publish message via Redis Pub/Sub
2. THE RedisMessageTransport SHALL subscribe to Redis channels
3. THE RedisMessageTransport SHALL support pattern subscriptions

##### 10.2 Streams 功能
4. THE RedisMessageTransport SHALL use Redis Streams for durable messaging
5. THE RedisMessageTransport SHALL support consumer groups
6. THE RedisMessageTransport SHALL handle message acknowledgment

##### 10.3 边界条件
7. THE RedisMessageTransport SHALL handle Redis connection failure
8. THE RedisMessageTransport SHALL handle slow consumers
9. THE RedisMessageTransport SHALL handle message backpressure

##### 10.4 属性测试
10. FOR ALL messages, delivery guarantees SHALL match configuration

### Requirement 11: RedisDslFlowStore TDD 测试

**User Story:** As a developer, I want to verify RedisDslFlowStore persists flow state correctly.

#### Acceptance Criteria

##### 11.1 基本操作
1. THE RedisDslFlowStore SHALL save flow state to Redis
2. THE RedisDslFlowStore SHALL load flow state from Redis
3. THE RedisDslFlowStore SHALL update flow state atomically
4. THE RedisDslFlowStore SHALL delete flow state

##### 11.2 分布式功能
5. THE RedisDslFlowStore SHALL support distributed locking for flow updates
6. THE RedisDslFlowStore SHALL handle concurrent flow executions
7. THE RedisDslFlowStore SHALL support flow state expiration

##### 11.3 边界条件
8. THE RedisDslFlowStore SHALL handle Redis connection failure during save
9. THE RedisDslFlowStore SHALL handle very large flow states
10. THE RedisDslFlowStore SHALL handle checkpoint recovery after crash

##### 11.4 属性测试
11. FOR ALL flow operations, Redis behavior SHALL match InMemory behavior

### Requirement 12: Redis Inbox/Outbox Store TDD 测试

**User Story:** As a developer, I want to verify Redis Inbox/Outbox stores work correctly.

#### Acceptance Criteria

##### 12.1 Outbox 操作
1. THE RedisOutboxStore SHALL add message to outbox atomically
2. THE RedisOutboxStore SHALL retrieve pending messages efficiently
3. THE RedisOutboxStore SHALL mark messages as sent
4. THE RedisOutboxStore SHALL support batch operations

##### 12.2 Inbox 操作
5. THE RedisInboxStore SHALL detect duplicates across instances
6. THE RedisInboxStore SHALL mark messages as processed
7. THE RedisInboxStore SHALL support TTL for processed records

##### 12.3 边界条件
8. THE Redis Inbox/Outbox SHALL handle high throughput scenarios
9. THE Redis Inbox/Outbox SHALL handle Redis failover

##### 12.4 属性测试
10. FOR ALL messages, exactly-once delivery SHALL be guaranteed

---

## Part 3: NATS 后端全面测试

### Requirement 13: NatsJSEventStore TDD 测试

**User Story:** As a developer, I want to verify NatsJSEventStore works correctly with JetStream.

#### Acceptance Criteria

##### 13.1 基本 CRUD 操作
1. THE NatsJSEventStore SHALL append events to JetStream
2. THE NatsJSEventStore SHALL read events from stream
3. THE NatsJSEventStore SHALL read events from specific sequence
4. THE NatsJSEventStore SHALL create stream if not exists

##### 13.2 JetStream 特定功能
5. THE NatsJSEventStore SHALL configure stream retention policy
6. THE NatsJSEventStore SHALL support stream replication
7. THE NatsJSEventStore SHALL handle consumer acknowledgment
8. THE NatsJSEventStore SHALL support message replay

##### 13.3 版本控制
9. THE NatsJSEventStore SHALL track stream sequence for versioning
10. THE NatsJSEventStore SHALL support optimistic concurrency via headers

##### 13.4 边界条件
11. THE NatsJSEventStore SHALL handle NATS connection failure
12. THE NatsJSEventStore SHALL handle stream limits (max messages, max bytes)
13. THE NatsJSEventStore SHALL handle slow consumer scenarios
14. THE NatsJSEventStore SHALL handle cluster node failure

##### 13.5 属性测试
15. FOR ALL event sequences, NATS behavior SHALL match InMemory behavior
16. FOR ALL replayed events, order SHALL be preserved

### Requirement 14: NatsSnapshotStore TDD 测试

**User Story:** As a developer, I want to verify NatsSnapshotStore works correctly.

#### Acceptance Criteria

##### 14.1 基本操作
1. THE NatsSnapshotStore SHALL save snapshot to NATS KV
2. THE NatsSnapshotStore SHALL load snapshot from NATS KV
3. THE NatsSnapshotStore SHALL delete snapshot
4. THE NatsSnapshotStore SHALL support snapshot versioning via KV revision

##### 14.2 NATS KV 特定功能
5. THE NatsSnapshotStore SHALL create KV bucket if not exists
6. THE NatsSnapshotStore SHALL support KV watch for changes
7. THE NatsSnapshotStore SHALL handle KV bucket replication

##### 14.3 边界条件
8. THE NatsSnapshotStore SHALL handle NATS connection failure
9. THE NatsSnapshotStore SHALL handle large snapshots
10. THE NatsSnapshotStore SHALL handle concurrent updates

##### 14.4 属性测试
11. FOR ALL snapshots, NATS behavior SHALL match InMemory behavior

### Requirement 15: NatsMessageTransport TDD 测试

**User Story:** As a developer, I want to verify NatsMessageTransport works correctly.

#### Acceptance Criteria

##### 15.1 Core NATS 功能
1. THE NatsMessageTransport SHALL publish message to subject
2. THE NatsMessageTransport SHALL subscribe to subject
3. THE NatsMessageTransport SHALL support wildcard subscriptions
4. THE NatsMessageTransport SHALL support request-reply pattern

##### 15.2 JetStream 功能
5. THE NatsMessageTransport SHALL publish to JetStream for durability
6. THE NatsMessageTransport SHALL support durable consumers
7. THE NatsMessageTransport SHALL support queue groups
8. THE NatsMessageTransport SHALL handle message acknowledgment

##### 15.3 边界条件
9. THE NatsMessageTransport SHALL handle NATS connection failure
10. THE NatsMessageTransport SHALL handle slow consumer detection
11. THE NatsMessageTransport SHALL handle message size limits
12. THE NatsMessageTransport SHALL handle cluster failover

##### 15.4 属性测试
13. FOR ALL messages, delivery guarantees SHALL match QoS configuration

### Requirement 16: NatsDslFlowStore TDD 测试

**User Story:** As a developer, I want to verify NatsDslFlowStore persists flow state correctly.

#### Acceptance Criteria

##### 16.1 基本操作
1. THE NatsDslFlowStore SHALL save flow state to NATS
2. THE NatsDslFlowStore SHALL load flow state from NATS
3. THE NatsDslFlowStore SHALL update flow state
4. THE NatsDslFlowStore SHALL delete flow state

##### 16.2 NATS 特定功能
5. THE NatsDslFlowStore SHALL use KV for state storage
6. THE NatsDslFlowStore SHALL support flow state watching
7. THE NatsDslFlowStore SHALL handle exactly-once semantics

##### 16.3 边界条件
8. THE NatsDslFlowStore SHALL handle NATS connection failure
9. THE NatsDslFlowStore SHALL handle concurrent flow updates
10. THE NatsDslFlowStore SHALL handle checkpoint recovery

##### 16.4 属性测试
11. FOR ALL flow operations, NATS behavior SHALL match InMemory behavior

### Requirement 17: NATS Inbox/Outbox Store TDD 测试

**User Story:** As a developer, I want to verify NATS Inbox/Outbox stores work correctly.

#### Acceptance Criteria

##### 17.1 Outbox 操作
1. THE NatsJSOutboxStore SHALL add message to outbox stream
2. THE NatsJSOutboxStore SHALL retrieve pending messages
3. THE NatsJSOutboxStore SHALL mark messages as sent

##### 17.2 Inbox 操作
4. THE NatsJSInboxStore SHALL detect duplicate messages
5. THE NatsJSInboxStore SHALL mark messages as processed
6. THE NatsJSInboxStore SHALL support message deduplication window

##### 17.3 边界条件
7. THE NATS Inbox/Outbox SHALL handle connection failure
8. THE NATS Inbox/Outbox SHALL handle high throughput

##### 17.4 属性测试
9. FOR ALL messages, exactly-once semantics SHALL be maintained

---

## Part 4: 跨后端一致性测试

### Requirement 18: EventStore 跨后端一致性

**User Story:** As a developer, I want all EventStore implementations to behave identically.

#### Acceptance Criteria

1. FOR ALL EventStore implementations (InMemory, Redis, NATS), append behavior SHALL be identical
2. FOR ALL EventStore implementations, read behavior SHALL be identical
3. FOR ALL EventStore implementations, version tracking SHALL be identical
4. FOR ALL EventStore implementations, concurrency handling SHALL be identical
5. THE test suite SHALL run same tests against all three backends
6. THE test suite SHALL compare results for consistency

### Requirement 19: SnapshotStore 跨后端一致性

**User Story:** As a developer, I want all SnapshotStore implementations to behave identically.

#### Acceptance Criteria

1. FOR ALL SnapshotStore implementations, save/load behavior SHALL be identical
2. FOR ALL SnapshotStore implementations, versioning SHALL be identical
3. FOR ALL SnapshotStore implementations, delete behavior SHALL be identical

### Requirement 20: Transport 跨后端一致性

**User Story:** As a developer, I want all Transport implementations to behave identically.

#### Acceptance Criteria

1. FOR ALL Transport implementations, publish/subscribe SHALL be identical
2. FOR ALL Transport implementations, request-reply SHALL be identical
3. FOR ALL Transport implementations, message ordering SHALL be identical

### Requirement 21: FlowStore 跨后端一致性

**User Story:** As a developer, I want all FlowStore implementations to behave identically.

#### Acceptance Criteria

1. FOR ALL FlowStore implementations, state persistence SHALL be identical
2. FOR ALL FlowStore implementations, checkpoint/restore SHALL be identical
3. FOR ALL FlowStore implementations, query behavior SHALL be identical

---

## Part 5: 通用边界条件测试

### Requirement 22: 空值和默认值边界

**User Story:** As a developer, I want to verify all components handle null and default values correctly.

#### Acceptance Criteria

##### 22.1 空值处理
1. ALL public APIs SHALL throw ArgumentNullException for null required parameters
2. ALL public APIs SHALL handle null optional parameters gracefully
3. ALL stores SHALL handle null IDs with appropriate exceptions

##### 22.2 空集合处理
4. ALL stores SHALL handle empty event lists
5. ALL stores SHALL handle empty query results
6. ALL transports SHALL handle empty message payloads

##### 22.3 默认值处理
7. ALL stores SHALL handle default(Guid) IDs
8. ALL stores SHALL handle default(DateTime) timestamps
9. ALL options SHALL have sensible defaults

### Requirement 23: 数值边界

**User Story:** As a developer, I want to verify all components handle numeric boundaries correctly.

#### Acceptance Criteria

##### 23.1 版本号边界
1. ALL EventStores SHALL handle version = 0
2. ALL EventStores SHALL handle version = long.MaxValue
3. ALL EventStores SHALL handle negative version numbers

##### 23.2 超时边界
4. ALL operations SHALL handle TimeSpan.Zero timeout
5. ALL operations SHALL handle TimeSpan.MaxValue timeout
6. ALL operations SHALL handle negative timeout values

##### 23.3 计数边界
7. ALL batch operations SHALL handle count = 0
8. ALL batch operations SHALL handle count = int.MaxValue
9. ALL pagination SHALL handle page size boundaries

### Requirement 24: 并发和线程安全

**User Story:** As a developer, I want to verify all components are thread-safe.

#### Acceptance Criteria

##### 24.1 并发读写
1. ALL stores SHALL handle concurrent reads safely
2. ALL stores SHALL handle concurrent writes with proper locking
3. ALL stores SHALL handle concurrent read-write operations

##### 24.2 并发订阅
4. ALL transports SHALL handle concurrent subscribe/unsubscribe
5. ALL transports SHALL handle concurrent message delivery
6. ALL transports SHALL handle subscriber exceptions without affecting others

##### 24.3 并发流程
7. ALL FlowStores SHALL handle concurrent flow updates
8. ALL FlowStores SHALL handle concurrent checkpoint saves
9. ALL FlowStores SHALL prevent lost updates

### Requirement 25: 序列化往返测试

**User Story:** As a developer, I want to verify all data survives serialization round-trips.

#### Acceptance Criteria

##### 25.1 事件序列化
1. FOR ALL event types, JSON serialization round-trip SHALL preserve all data
2. FOR ALL event types, MemoryPack serialization round-trip SHALL preserve all data
3. FOR ALL events with complex types, nested objects SHALL be preserved

##### 25.2 快照序列化
4. FOR ALL snapshot types, serialization round-trip SHALL preserve all data
5. FOR ALL snapshots with collections, collection contents SHALL be preserved

##### 25.3 消息序列化
6. FOR ALL message types, transport serialization SHALL preserve all data
7. FOR ALL messages with metadata, metadata SHALL be preserved

### Requirement 26: 错误处理和恢复

**User Story:** As a developer, I want to verify all components handle errors gracefully.

#### Acceptance Criteria

##### 26.1 连接错误
1. ALL Redis stores SHALL handle connection failure with retry
2. ALL NATS stores SHALL handle connection failure with retry
3. ALL stores SHALL provide meaningful error messages

##### 26.2 超时错误
4. ALL operations SHALL respect configured timeouts
5. ALL operations SHALL throw OperationCanceledException on timeout
6. ALL operations SHALL clean up resources on timeout

##### 26.3 数据错误
7. ALL stores SHALL handle corrupted data gracefully
8. ALL stores SHALL handle schema version mismatches
9. ALL stores SHALL provide recovery mechanisms

### Requirement 27: 性能基准

**User Story:** As a developer, I want to verify performance meets requirements.

#### Acceptance Criteria

##### 27.1 吞吐量
1. InMemory EventStore SHALL handle 100,000+ events/second
2. Redis EventStore SHALL handle 10,000+ events/second
3. NATS EventStore SHALL handle 10,000+ events/second

##### 27.2 延迟
4. InMemory operations SHALL complete in <1ms (p99)
5. Redis operations SHALL complete in <10ms (p99)
6. NATS operations SHALL complete in <10ms (p99)

##### 27.3 内存
7. ALL stores SHALL not leak memory under sustained load
8. ALL stores SHALL handle memory pressure gracefully

---

## Part 6: 集成和端到端测试

### Requirement 28: 完整 CQRS 流程测试

**User Story:** As a developer, I want to verify complete CQRS workflows work correctly.

#### Acceptance Criteria

1. THE system SHALL handle command -> event -> projection flow
2. THE system SHALL handle query with read model
3. THE system SHALL handle saga/process manager workflows
4. THE system SHALL handle compensation on failure

### Requirement 29: 分布式场景测试

**User Story:** As a developer, I want to verify distributed scenarios work correctly.

#### Acceptance Criteria

1. THE system SHALL handle multiple instances with shared Redis
2. THE system SHALL handle multiple instances with shared NATS
3. THE system SHALL handle instance failure and recovery
4. THE system SHALL handle network partition scenarios

### Requirement 30: AOT 兼容性测试

**User Story:** As a developer, I want to verify AOT compatibility.

#### Acceptance Criteria

1. ALL stores SHALL work with Native AOT compilation
2. ALL serialization SHALL work without runtime reflection
3. ALL DI registrations SHALL work with AOT
4. ALL source generators SHALL produce AOT-compatible code
