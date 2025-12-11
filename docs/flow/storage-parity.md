# Flow DSL Storage Parity Documentation

## Overview
This document confirms that all three Flow DSL storage implementations (InMemory, Redis, NATS) have complete feature parity and implement the same `IDslFlowStore` interface.

## Feature Matrix

| Feature | InMemory | Redis | NATS | Notes |
|---------|----------|-------|------|-------|
| **Core CRUD Operations** |
| CreateAsync | ✅ | ✅ | ✅ | Create new flow snapshot |
| GetAsync | ✅ | ✅ | ✅ | Retrieve flow snapshot by ID |
| UpdateAsync | ✅ | ✅ | ✅ | Update with optimistic locking |
| DeleteAsync | ✅ | ✅ | ✅ | Delete flow snapshot |
| **Wait Conditions (WhenAll/WhenAny)** |
| SetWaitConditionAsync | ✅ | ✅ | ✅ | Set wait condition for correlation |
| GetWaitConditionAsync | ✅ | ✅ | ✅ | Retrieve wait condition |
| UpdateWaitConditionAsync | ✅ | ✅ | ✅ | Update existing wait condition |
| ClearWaitConditionAsync | ✅ | ✅ | ✅ | Remove wait condition |
| GetTimedOutWaitConditionsAsync | ✅ | ✅ | ✅ | Find timed-out conditions |
| **ForEach Progress Tracking** |
| SaveForEachProgressAsync | ✅ | ✅ | ✅ | Save progress for recovery |
| GetForEachProgressAsync | ✅ | ✅ | ✅ | Retrieve progress state |
| ClearForEachProgressAsync | ✅ | ✅ | ✅ | Clear progress data |

## Implementation Details

### InMemoryDslFlowStore
- **Location**: `src/Catga/Flow/InMemoryDslFlowStore.cs`
- **Storage**: ConcurrentDictionary collections
- **Use Case**: Development, testing, single-instance scenarios
- **Features**:
  - Thread-safe concurrent operations
  - No external dependencies
  - Clear() method for testing
  - Instant operations (no I/O)

### RedisDslFlowStore
- **Location**: `src/Catga.Persistence.Redis/Flow/RedisDslFlowStore.cs`
- **Storage**: Redis with Lua scripts
- **Use Case**: Production, distributed systems
- **Features**:
  - Atomic operations via Lua scripts
  - Distributed locking via version control
  - Sorted sets for timeout tracking
  - Configurable key prefix
  - JSON serialization support
  - TTL support (7 days for flows, 1 day for wait conditions)

### NatsDslFlowStore
- **Location**: `src/Catga.Persistence.Nats/Flow/NatsDslFlowStore.cs`
- **Storage**: NATS JetStream Key-Value Store
- **Use Case**: Production, event-driven systems
- **Features**:
  - Revision-based optimistic locking
  - Separate buckets for flows and wait conditions
  - Key encoding for special characters
  - File-based storage backend
  - Auto-initialization on first use
  - History retention (1 revision)

## Data Structures

### FlowSnapshot<TState>
All stores persist the same snapshot structure:
```csharp
- FlowId: string
- State: TState (IFlowState)
- Position: FlowPosition (int[] Path)
- Status: DslFlowStatus (Created/Running/Suspended/Completed/Failed)
- Error: string?
- WaitCondition: WaitCondition?
- CreatedAt: DateTime
- UpdatedAt: DateTime
- Version: int (for optimistic locking)
```

### WaitCondition
Supports WhenAll/WhenAny operations:
```csharp
- CorrelationId: string
- FlowIds: List<string>
- WaitType: WaitConditionType (WhenAll/WhenAny)
- CompletedCount: int
- Timeout: TimeSpan
- CreatedAt: DateTime
```

### ForEachProgress
Tracks ForEach loop execution:
```csharp
- ProcessedIndices: HashSet<int>
- FailedIndices: HashSet<int>
- TotalProcessed: int
- LastProcessedIndex: int
```

## Consistency Guarantees

### Optimistic Locking
All stores implement version-based optimistic locking:
- **InMemory**: Direct version comparison
- **Redis**: Lua script with version check
- **NATS**: Revision-based CAS operations

### Atomicity
- **InMemory**: Thread-safe via ConcurrentDictionary
- **Redis**: Atomic via Lua scripts
- **NATS**: Atomic via KV revision checks

### Durability
- **InMemory**: No durability (in-process only)
- **Redis**: Configurable persistence (RDB/AOF)
- **NATS**: JetStream file storage

## Performance Characteristics

| Operation | InMemory | Redis | NATS |
|-----------|----------|-------|------|
| Create | O(1) | O(1) + network | O(1) + network |
| Get | O(1) | O(1) + network | O(1) + network |
| Update | O(1) | O(1) + network | O(1) + network + revision check |
| Delete | O(1) | O(1) + network | O(1) + network |
| Timeout Scan | O(n) | O(log n) via ZSET | O(n) full scan |

## Testing

### Unit Tests
- `InMemoryDslFlowStoreTests.cs` - InMemory specific tests
- `RedisFlowStoreTests.cs` - Redis specific tests (requires Redis)
- `NatsFlowStoreTests.cs` - NATS specific tests (requires NATS)

### Contract Tests
- `FlowStoreContractTests.cs` - Common behavior verification
- `StorageParityValidationTests.cs` - Feature parity validation

### Integration Tests
- `ForEachIntegrationTests.cs` - ForEach with recovery
- `FlowRecoveryTests.cs` - Recovery scenarios
- `ConcurrencySafetyTests.cs` - Concurrent operations

## Migration Guide

### From InMemory to Redis
```csharp
// Before
services.AddSingleton<IDslFlowStore, InMemoryDslFlowStore>();

// After
services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect("localhost"));
services.AddSingleton<IDslFlowStore, RedisDslFlowStore>();
```

### From InMemory to NATS
```csharp
// Before
services.AddSingleton<IDslFlowStore, InMemoryDslFlowStore>();

// After
services.AddSingleton<INatsConnection>(new NatsConnection());
services.AddSingleton<IDslFlowStore, NatsDslFlowStore>();
```

## Conclusion

✅ **All three storage implementations (InMemory, Redis, NATS) are functionally complete and equivalent.**

Each implementation:
1. Fully implements the `IDslFlowStore` interface
2. Supports all required operations (CRUD, WaitConditions, ForEachProgress)
3. Provides optimistic locking for consistency
4. Handles all flow statuses and transitions
5. Supports concurrent operations safely
6. Can persist large data payloads
7. Provides timeout tracking mechanisms

The choice between them depends on deployment requirements:
- **InMemory**: Best for development, testing, and single-instance deployments
- **Redis**: Best for distributed systems requiring shared state
- **NATS**: Best for event-driven architectures with JetStream

All three can be used interchangeably without code changes, ensuring maximum flexibility for different deployment scenarios.
