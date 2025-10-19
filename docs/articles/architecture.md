# Catga Architecture

## Overview

Catga is built on a **pluggable architecture** where Transport, Persistence, and Serialization layers are completely abstracted and interchangeable.

```
┌─────────────────────────────────────┐
│         Application Layer            │
│    (Commands, Events, Handlers)      │
└──────────────┬──────────────────────┘
               │
┌──────────────▼──────────────────────┐
│          Catga Core                  │
│  (Mediator, Pipeline, Behaviors)     │
└──┬───────────┬───────────┬──────────┘
   │           │           │
┌──▼──────┐ ┌─▼────────┐ ┌▼──────────┐
│Transport│ │Persistence│ │Serializer │
│  Layer  │ │   Layer   │ │   Layer   │
└──┬──────┘ └─┬────────┘ └┬──────────┘
   │          │            │
   │  ┌───────▼────┐       │
   │  │ InMemory   │       │
   │  │ Redis      │◄──────┤
   │  │ NATS       │       │
   └──►            │       │
      └────────────┘       │
                           │
      ┌────────────────────▼────┐
      │ JSON / MemoryPack / ... │
      └─────────────────────────┘
```

## Core Components

### 1. Mediator Pattern

`ICatgaMediator` is the central hub for all message routing:

```csharp
public interface ICatgaMediator
{
    // Commands (Request-Response)
    Task<CatgaResult<TResponse>> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default);

    // Events (Pub-Sub)
    Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent;
}
```

### 2. Pipeline Behaviors

Process messages through a pipeline of behaviors:

```csharp
Request → Logging → Validation → Idempotency → Handler → Response
```

Built-in behaviors:
- **LoggingBehavior** - Automatic logging
- **IdempotencyBehavior** - Duplicate detection
- **RetryBehavior** - Automatic retry with backoff

### 3. Transport Layer

Abstraction for message delivery:

```csharp
public interface IMessageTransport
{
    Task PublishAsync<TMessage>(TMessage message, ...);
    Task SendAsync<TMessage>(TMessage message, ...);
    Task SubscribeAsync<TMessage>(Func<TMessage, Task> handler, ...);
}
```

**Implementations**:
- `InMemoryMessageTransport` - Fast, local-only
- `RedisMessageTransport` - Redis Pub/Sub + Streams
- `NatsMessageTransport` - NATS Core + JetStream

### 4. Persistence Layer

Event sourcing and transactional outbox:

```csharp
public interface IEventStore
{
    ValueTask AppendAsync(string streamId, IReadOnlyList<IEvent> events);
    IAsyncEnumerable<IEvent> ReadAsync(string streamId);
}

public interface IOutboxStore
{
    ValueTask AddAsync(OutboxMessage message);
    ValueTask<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync();
    ValueTask MarkAsPublishedAsync(string messageId);
}
```

**Implementations**:
- `InMemoryEventStore` / `InMemoryOutboxStore`
- `RedisOutboxStore` - High-performance Redis
- `NatsJSEventStore` / `NatsJSOutboxStore` - NATS JetStream

### 5. Serialization Layer

Pluggable serialization for AOT compatibility:

```csharp
public interface IMessageSerializer
{
    byte[] Serialize<T>(T value);
    T? Deserialize<T>(byte[] data);
}
```

**Implementations**:
- `JsonMessageSerializer` - System.Text.Json
- `MemoryPackMessageSerializer` - Ultra-fast binary

## Quality of Service (QoS)

Catga supports two QoS levels:

### QoS 0: At-Most-Once
- Fastest delivery
- No acknowledgment
- Fire-and-forget
- Use case: Metrics, notifications

### QoS 1: At-Least-Once
- Guaranteed delivery
- Requires acknowledgment
- May duplicate
- Use case: Critical commands, events

## Outbox Pattern

Ensures reliable message publishing:

```
1. Save to database + outbox (same transaction)
2. Background publisher polls outbox
3. Publish to transport
4. Mark as published (delete from outbox)
```

## Inbox Pattern

Ensures idempotent message processing:

```
1. Check inbox for message ID
2. If processed, return cached result
3. Process message
4. Store result in inbox
```

## AOT Compatibility

Catga is 100% Native AOT compatible:

- ✅ No reflection in hot paths
- ✅ Source generator for registration
- ✅ Pluggable serialization
- ✅ All warnings suppressed with justification

## Performance

Key optimizations:

- **ArrayPool** - Zero-allocation encoding
- **Span<T>** - Zero-copy operations
- **Double-checked locking** - Fast initialization
- **FusionCache** - Intelligent caching
- **Lock-free** - Where possible

## Extensibility

Extend Catga through:

1. **Custom Behaviors** - `IPipelineBehavior<TRequest, TResponse>`
2. **Custom Transport** - `IMessageTransport`
3. **Custom Persistence** - `IEventStore`, `IOutboxStore`, `IInboxStore`
4. **Custom Serializer** - `IMessageSerializer`

## Next Steps

- [Configuration Guide](configuration.md) - Detailed configuration
- [Transport Layer](transport-layer.md) - Transport options
- [Persistence Layer](persistence-layer.md) - Persistence strategies

