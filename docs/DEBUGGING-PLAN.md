# Catga Native Debugging Support - Implementation Plan

## 🎯 Goals

1. **Easy Message Tracing** - Users can easily see complete message flow
2. **Zero Configuration** - Debugging works out-of-the-box
3. **Native Support** - Built into Catga core, not external tools
4. **Cross-Transport** - Works with InMemory, NATS, Redis
5. **Memory Efficient** - No memory waste, use pooling and caching
6. **Production Ready** - Can be enabled/disabled via config

---

## 📋 Required Features

### 1. Message Flow Visualization

**What users need to see**:
```
Request ID: abc123
├── Command: CreateOrderCommand
│   ├── Handler: CreateOrderHandler
│   │   ├── Repository.SaveAsync() - 2ms
│   │   ├── Inventory.ReserveStock() - 5ms
│   │   └── Mediator.PublishAsync(OrderCreatedEvent) - 1ms
│   └── Total: 8ms
└── Events Published:
    ├── OrderCreatedEvent → 2 handlers
    │   ├── NotificationHandler - 3ms
    │   └── AnalyticsHandler - 1ms
    └── InventoryReservedEvent → 1 handler
        └── LogHandler - <1ms
```

### 2. Tracing Context

**Propagation**:
- ✅ Correlation ID (spans requests)
- ✅ Trace ID (distributed tracing)
- ✅ Parent ID (message hierarchy)
- ✅ Timestamp (ordering)
- ✅ Transport metadata (NATS subject, Redis key)

### 3. Debug UI/Console

**Output Formats**:
- Console (colorized tree view)
- Structured JSON (for external tools)
- Aspire Dashboard (OpenTelemetry)
- Custom debug endpoints

---

## 🏗️ Architecture Design

### Core Components

#### 1. `MessageFlowTracker` (Singleton, Memory-Efficient)

```csharp
public sealed class MessageFlowTracker
{
    private readonly ConcurrentDictionary<string, FlowContext> _activeFlows;
    private readonly ObjectPool<FlowContext> _contextPool;  // Memory pooling
    private readonly int _maxActiveFlows = 1000;            // Limit memory
    
    public FlowContext BeginFlow(string correlationId);
    public void RecordStep(string correlationId, StepInfo step);
    public void EndFlow(string correlationId);
    public FlowSummary GetFlow(string correlationId);
}
```

**Memory optimization**:
- Object pooling for `FlowContext`
- LRU eviction when exceeding `_maxActiveFlows`
- Weak references for completed flows
- Auto-cleanup after TTL (default 5 minutes)

#### 2. `FlowContext` (Pooled, Reusable)

```csharp
public sealed class FlowContext : IResettable
{
    public string CorrelationId { get; set; }
    public string TraceId { get; set; }
    public DateTime StartTime { get; set; }
    public List<StepInfo> Steps { get; } = new(capacity: 16);  // Pre-sized
    
    public void Reset()
    {
        CorrelationId = string.Empty;
        TraceId = string.Empty;
        Steps.Clear();  // Reuse list
    }
}
```

#### 3. `DebugMiddleware` (Zero-allocation)

```csharp
public sealed class DebugMiddleware<TRequest, TResponse> 
    : IPipelineBehavior<TRequest, TResponse>
{
    private readonly MessageFlowTracker _tracker;
    
    public async ValueTask<CatgaResult<TResponse>> HandleAsync(...)
    {
        if (!_options.EnableDebug) return await next();  // Fast path
        
        var flowContext = _tracker.BeginFlow(request.CorrelationId);
        // Record steps...
        return result;
    }
}
```

---

## 🔧 Implementation Plan

### Phase 1: Core Infrastructure (High Priority)

#### 1.1 MessageFlowTracker
- [ ] Create `MessageFlowTracker` with object pooling
- [ ] Implement LRU eviction strategy
- [ ] Add TTL-based auto-cleanup
- [ ] Memory limit enforcement

#### 1.2 FlowContext & StepInfo
- [ ] Define `FlowContext` (pooled)
- [ ] Define `StepInfo` struct (value type, no allocation)
- [ ] Implement `IResettable` for pooling
- [ ] Add timestamps and duration tracking

#### 1.3 Integration Points
- [ ] Add to `CatgaMediator` (before/after handler)
- [ ] Add to `Pipeline` behaviors
- [ ] Add to `EventPublisher` (event flow)
- [ ] Add to `Repository` (data access)

### Phase 2: Transport-Specific Tracking (Medium Priority)

#### 2.1 NATS Integration
- [ ] Extract NATS headers (subject, message-id, timestamp)
- [ ] Track publish/subscribe operations
- [ ] Record JetStream sequence numbers
- [ ] Capture NATS metadata without copying

#### 2.2 Redis Integration
- [ ] Track Redis operations (GET, SET, PUBLISH)
- [ ] Record key patterns
- [ ] Capture TTL information
- [ ] Use Redis metadata efficiently

#### 2.3 InMemory Support
- [ ] Track in-memory message routing
- [ ] Record handler invocations
- [ ] Minimal overhead in InMemory mode

### Phase 3: Visualization & Output (Medium Priority)

#### 3.1 Console Output
- [ ] Colorized tree view (using ANSI colors)
- [ ] Compact format (one-line summary)
- [ ] Detailed format (full tree)
- [ ] Configurable verbosity

#### 3.2 Debug Endpoints
- [ ] `GET /debug/flows` - List active flows
- [ ] `GET /debug/flows/{correlationId}` - Flow details
- [ ] `GET /debug/stats` - Statistics
- [ ] `DELETE /debug/flows/{correlationId}` - Clear flow

#### 3.3 Structured Output
- [ ] JSON export format
- [ ] OpenTelemetry span export
- [ ] Custom formatters

### Phase 4: Performance & Memory Optimization (High Priority)

#### 4.1 Object Pooling
```csharp
// Use Microsoft.Extensions.ObjectPool
private readonly ObjectPool<FlowContext> _pool = 
    new DefaultObjectPool<FlowContext>(new FlowContextPolicy());

public FlowContext Get() => _pool.Get();
public void Return(FlowContext context)
{
    context.Reset();
    _pool.Return(context);
}
```

#### 4.2 Memory Limits
```csharp
public class DebugOptions
{
    public bool EnableDebug { get; set; } = false;
    public int MaxActiveFlows { get; set; } = 1000;        // Limit active flows
    public TimeSpan FlowTTL { get; set; } = TimeSpan.FromMinutes(5);
    public int MaxStepsPerFlow { get; set; } = 100;        // Prevent abuse
    public bool EnableConsoleOutput { get; set; } = true;
}
```

#### 4.3 Lazy Initialization
```csharp
// Only allocate when debug is enabled
private MessageFlowTracker? _tracker;

public MessageFlowTracker GetTracker()
{
    if (!_options.EnableDebug) return null;
    return _tracker ??= new MessageFlowTracker(_options);
}
```

---

## 💡 User Experience

### Enable Debugging (One Line)

```csharp
builder.Services.AddCatga()
    .UseMemoryPack()
    .WithDebug();  // ← Enable native debugging
```

### View Message Flow

**Console Output** (automatic):
```
[12:34:56.123] Request abc123 started
  ├─ Command: CreateOrderCommand
  │  ├─ Handler: CreateOrderHandler (8ms)
  │  │  ├─ SaveOrder (2ms)
  │  │  ├─ ReserveStock (5ms)
  │  │  └─ PublishEvent (1ms)
  │  └─ Events: 2 published
  └─ Total: 8ms ✅
```

**API Query**:
```bash
curl http://localhost:5000/debug/flows/abc123
```

**Response**:
```json
{
  "correlationId": "abc123",
  "traceId": "xyz789",
  "startTime": "2025-10-15T12:34:56Z",
  "duration": "8ms",
  "steps": [
    {
      "type": "Command",
      "name": "CreateOrderCommand",
      "handler": "CreateOrderHandler",
      "duration": "8ms",
      "substeps": [
        { "name": "SaveOrder", "duration": "2ms" },
        { "name": "ReserveStock", "duration": "5ms" },
        { "name": "PublishEvent", "duration": "1ms" }
      ]
    }
  ],
  "events": [
    { "name": "OrderCreatedEvent", "handlers": 2, "duration": "4ms" }
  ]
}
```

---

## 🚀 Performance Considerations

### Memory Budget

| Component | Memory (per flow) | Max Flows | Total |
|-----------|------------------|-----------|-------|
| FlowContext | ~1 KB | 1,000 | ~1 MB |
| StepInfo (x100) | ~50 bytes | 100,000 | ~5 MB |
| **Total** | | | **~6 MB** |

**With pooling**: Reuse objects, actual memory < 2 MB

### CPU Overhead

| Operation | Without Debug | With Debug | Overhead |
|-----------|--------------|------------|----------|
| SendAsync | 0.8 μs | 1.2 μs | +50% (~0.4 μs) |
| PublishAsync | 0.7 μs | 1.0 μs | +43% (~0.3 μs) |

**Mitigation**:
- Fast path when debug disabled
- Lazy initialization
- Inline critical paths

### Transport Overhead

**NATS**:
- Use existing headers (no new allocations)
- Piggyback on message metadata
- Zero copy for trace context

**Redis**:
- Use pipeline for batch operations
- Store debug info in separate keys (optional)
- TTL-based auto-cleanup

---

## 📊 Implementation Priority

### High Priority (Do Now)
1. [ ] MessageFlowTracker with object pooling
2. [ ] FlowContext with memory limits
3. [ ] DebugMiddleware integration
4. [ ] Console colorized output

### Medium Priority (Do Next)
5. [ ] Debug API endpoints
6. [ ] NATS header extraction
7. [ ] Redis metadata tracking
8. [ ] JSON export format

### Low Priority (Nice to Have)
9. [ ] Custom visualizers
10. [ ] Performance profiling
11. [ ] Advanced filtering

---

## ✅ Success Criteria

```
✅ Enable with one line: .WithDebug()
✅ Console shows message flow automatically
✅ Memory overhead: < 2 MB
✅ CPU overhead: < 0.5 μs per operation
✅ Works with NATS, Redis, InMemory
✅ Kubernetes-ready debug endpoints
✅ Zero config for basic usage
✅ All tests still passing
```

---

## 🎯 Example Usage

### Basic Usage

```csharp
// Enable debugging
builder.Services.AddCatga()
    .WithDebug();  // Automatic console output

// Use normally
await mediator.SendAsync(new CreateOrder(...));

// Console automatically shows:
// [12:34:56] CreateOrder abc123
//   ├─ CreateOrderHandler (8ms)
//   │  ├─ Save (2ms)
//   │  ├─ Reserve (5ms)
//   │  └─ Publish (1ms)
//   └─ ✅ Success
```

### Advanced Usage

```csharp
// Configure debug options
builder.Services.AddCatga()
    .WithDebug(options =>
    {
        options.MaxActiveFlows = 5000;
        options.EnableConsoleOutput = true;
        options.EnableApiEndpoints = true;
        options.IncludeTransportMetadata = true;  // NATS/Redis details
    });

// Query flow
var flow = await debugApi.GetFlowAsync("abc123");
Console.WriteLine(flow.ToTreeView());
```

---

<div align="center">

**🎯 Make debugging as simple as adding one line!**

Ready to implement!

</div>

