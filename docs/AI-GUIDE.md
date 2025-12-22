# Catga Framework - AI Development Guide

> **For AI Assistants**: This guide provides comprehensive instructions for helping developers use the Catga CQRS framework effectively.

## 📋 Table of Contents

1. [Framework Overview](#framework-overview)
2. [Core Concepts](#core-concepts)
3. [Getting Started](#getting-started)
4. [Architecture Patterns](#architecture-patterns)
5. [Best Practices](#best-practices)
6. [Common Pitfalls](#common-pitfalls)
7. [Code Generation Guidelines](#code-generation-guidelines)
8. [Testing Strategies](#testing-strategies)
9. [Performance Optimization](#performance-optimization)
10. [Troubleshooting](#troubleshooting)

---

## Framework Overview

### What is Catga?

Catga is a high-performance, AOT-compatible CQRS (Command Query Responsibility Segregation) framework for .NET 8/9 that provides:

- **CQRS Pattern**: Separate command and query handling
- **Event Sourcing**: Complete event history tracking
- **Multiple Backends**: InMemory, Redis, NATS support
- **Distributed Messaging**: Pub/Sub with multiple transports
- **AOT Compilation**: Native AOT ready for optimal performance
- **Type Safety**: Full compile-time checking
- **Zero Boilerplate**: Minimal code, maximum features

### Key Features

✅ **Commands & Queries**: Type-safe request/response pattern  
✅ **Events**: Pub/Sub event handling  
✅ **Flow DSL**: Workflow orchestration  
✅ **Event Sourcing**: Aggregate state management  
✅ **Persistence**: Multiple storage backends  
✅ **Transport**: Multiple messaging backends  
✅ **Serialization**: MemoryPack for high performance  
✅ **Observability**: Built-in metrics and tracing  

---

## Core Concepts

### 1. Messages

All messages in Catga implement marker interfaces:

```csharp
// Commands - Write operations
public record CreateOrderCommand(string CustomerId, List<OrderItem> Items) 
    : IRequest<OrderCreatedResult>
{
    public long MessageId { get; init; }
}

// Queries - Read operations
public record GetOrderQuery(string OrderId) 
    : IRequest<Order?>
{
    public long MessageId { get; init; }
}

// Events - Domain events
public record OrderCreatedEvent(string OrderId, decimal Total) 
    : IEvent
{
    public long MessageId { get; init; }
}
```

**Important**: 
- All messages MUST have a `MessageId` property
- Use records for immutability
- Commands should be imperative (CreateOrder, UpdateOrder)
- Events should be past tense (OrderCreated, OrderUpdated)

### 2. Handlers

Handlers process messages:

```csharp
// Command Handler
public sealed class CreateOrderHandler(OrderStore store, ICatgaMediator mediator) 
    : IRequestHandler<CreateOrderCommand, OrderCreatedResult>
{
    public async ValueTask<CatgaResult<OrderCreatedResult>> HandleAsync(
        CreateOrderCommand cmd, CancellationToken ct = default)
    {
        // 1. Validate
        if (string.IsNullOrEmpty(cmd.CustomerId))
            return CatgaResult<OrderCreatedResult>.Failure("Customer ID required");
        
        // 2. Execute business logic
        var order = new Order(/* ... */);
        store.Save(order);
        
        // 3. Publish events
        await mediator.PublishAsync(new OrderCreatedEvent(order.Id, order.Total), ct);
        
        // 4. Return result
        return CatgaResult<OrderCreatedResult>.Success(
            new OrderCreatedResult(order.Id, order.Total));
    }
}

// Event Handler
public sealed class OrderEventLogger : IEventHandler<OrderCreatedEvent>
{
    public ValueTask HandleAsync(OrderCreatedEvent evt, CancellationToken ct = default)
    {
        Console.WriteLine($"Order {evt.OrderId} created: ${evt.Total}");
        return ValueTask.CompletedTask;
    }
}
```

**Important**:
- Handlers should be `sealed` for performance
- Use `ValueTask` for async operations
- Always handle `CancellationToken`
- Return `CatgaResult<T>` for commands/queries
- Event handlers return `ValueTask` (no result)

### 3. Mediator

The mediator routes messages to handlers:

```csharp
// Send command/query
var result = await mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(command);

// Publish event
await mediator.PublishAsync(new OrderCreatedEvent(orderId, total));
```

---

## Getting Started

### Step 1: Install Packages

```xml
<ItemGroup>
  <PackageReference Include="Catga" Version="0.1.0" />
  <PackageReference Include="Catga.Serialization.MemoryPack" Version="0.1.0" />
  
  <!-- Choose transport -->
  <PackageReference Include="Catga.Transport.InMemory" Version="0.1.0" />
  <!-- OR -->
  <PackageReference Include="Catga.Transport.Redis" Version="0.1.0" />
  <!-- OR -->
  <PackageReference Include="Catga.Transport.Nats" Version="0.1.0" />
  
  <!-- Choose persistence -->
  <PackageReference Include="Catga.Persistence.InMemory" Version="0.1.0" />
  <!-- OR -->
  <PackageReference Include="Catga.Persistence.Redis" Version="0.1.0" />
  <!-- OR -->
  <PackageReference Include="Catga.Persistence.Nats" Version="0.1.0" />
</ItemGroup>
```

### Step 2: Configure Services

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add Catga with MemoryPack serialization
var catga = builder.Services.AddCatga().UseMemoryPack();

// Configure persistence
catga.UseInMemory(); // Development
// OR
catga.UseRedis("localhost:6379"); // Production
// OR
builder.Services.AddNatsConnection("nats://localhost:4222");
catga.UseNats(); // High-performance

// Configure transport
builder.Services.AddInMemoryTransport(); // Development
// OR
builder.Services.AddRedisTransport("localhost:6379"); // Production
// OR
builder.Services.AddNatsTransport("nats://localhost:4222"); // High-performance

// Register handlers
builder.Services.AddCatgaHandlers();
```

### Step 3: Define Messages

```csharp
using MemoryPack;

[MemoryPackable]
public partial record CreateOrderCommand(string CustomerId, List<OrderItem> Items) 
    : IRequest<OrderCreatedResult>
{
    public long MessageId { get; init; }
}

[MemoryPackable]
public partial record OrderCreatedResult(string OrderId, decimal Total, DateTime CreatedAt);

[MemoryPackable]
public partial record OrderCreatedEvent(string OrderId, string CustomerId, decimal Total) 
    : IEvent
{
    public long MessageId { get; init; }
}
```

**Important**:
- Add `[MemoryPackable]` attribute
- Make record `partial`
- Include `MessageId` property

### Step 4: Implement Handlers

```csharp
public sealed class CreateOrderHandler(OrderStore store, ICatgaMediator mediator) 
    : IRequestHandler<CreateOrderCommand, OrderCreatedResult>
{
    public async ValueTask<CatgaResult<OrderCreatedResult>> HandleAsync(
        CreateOrderCommand cmd, CancellationToken ct = default)
    {
        var orderId = Guid.NewGuid().ToString("N")[..8];
        var total = cmd.Items.Sum(i => i.Price * i.Quantity);
        
        var order = new Order(orderId, cmd.CustomerId, cmd.Items, total);
        store.Save(order);
        
        await mediator.PublishAsync(
            new OrderCreatedEvent(orderId, cmd.CustomerId, total), ct);
        
        return CatgaResult<OrderCreatedResult>.Success(
            new OrderCreatedResult(orderId, total, DateTime.UtcNow));
    }
}
```

### Step 5: Use in API

```csharp
app.MapPost("/orders", async (CreateOrderRequest req, ICatgaMediator mediator) =>
{
    var command = new CreateOrderCommand(req.CustomerId, req.Items);
    var result = await mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(command);
    
    return result.IsSuccess 
        ? Results.Created($"/orders/{result.Value!.OrderId}", result.Value)
        : Results.BadRequest(result.Error);
});
```

---

## Architecture Patterns

### 1. CQRS Pattern

**Separate reads from writes:**

```csharp
// Write side - Commands
public record CreateOrderCommand(...) : IRequest<OrderCreatedResult>;
public record UpdateOrderCommand(...) : IRequest;

// Read side - Queries
public record GetOrderQuery(string OrderId) : IRequest<Order?>;
public record GetAllOrdersQuery : IRequest<List<Order>>;
```

**Benefits**:
- Optimized read/write models
- Independent scaling
- Clear separation of concerns

### 2. Event Sourcing

**Store events, not state:**

```csharp
public sealed class OrderAggregate
{
    private readonly List<object> _events = new();
    
    public void CreateOrder(string customerId, decimal total)
    {
        var evt = new OrderCreatedEvent(Id, customerId, total);
        Apply(evt);
        _events.Add(evt);
    }
    
    private void Apply(OrderCreatedEvent evt)
    {
        Id = evt.OrderId;
        CustomerId = evt.CustomerId;
        Total = evt.Total;
        Status = OrderStatus.Pending;
    }
    
    public IReadOnlyList<object> GetUncommittedEvents() => _events;
}
```

### 3. Domain Events

**Publish events for side effects:**

```csharp
// In handler
await mediator.PublishAsync(new OrderCreatedEvent(orderId, total), ct);

// Multiple subscribers
public sealed class OrderNotificationHandler : IEventHandler<OrderCreatedEvent>
{
    public async ValueTask HandleAsync(OrderCreatedEvent evt, CancellationToken ct)
    {
        await SendEmailAsync(evt.CustomerId, evt.OrderId);
    }
}

public sealed class OrderAnalyticsHandler : IEventHandler<OrderCreatedEvent>
{
    public async ValueTask HandleAsync(OrderCreatedEvent evt, CancellationToken ct)
    {
        await TrackOrderCreatedAsync(evt.OrderId, evt.Total);
    }
}
```

---

## Best Practices

### 1. Message Design

✅ **DO**:
- Use records for immutability
- Include all required data in message
- Use descriptive names
- Keep messages small and focused

❌ **DON'T**:
- Include behavior in messages
- Use mutable properties
- Share messages across bounded contexts
- Include infrastructure concerns

### 2. Handler Design

✅ **DO**:
- Keep handlers focused (single responsibility)
- Use dependency injection
- Handle errors gracefully
- Return meaningful results
- Make handlers `sealed`

❌ **DON'T**:
- Call other handlers directly
- Access database directly (use repositories)
- Throw exceptions for business logic failures
- Block async operations

### 3. Error Handling

```csharp
// Good - Return result
public async ValueTask<CatgaResult<Order>> HandleAsync(
    GetOrderQuery query, CancellationToken ct)
{
    var order = await repository.GetAsync(query.OrderId);
    
    if (order == null)
        return CatgaResult<Order>.Failure("Order not found");
    
    return CatgaResult<Order>.Success(order);
}

// Bad - Throw exception
public async ValueTask<CatgaResult<Order>> HandleAsync(
    GetOrderQuery query, CancellationToken ct)
{
    var order = await repository.GetAsync(query.OrderId);
    
    if (order == null)
        throw new OrderNotFoundException(); // ❌ Don't do this
    
    return CatgaResult<Order>.Success(order);
}
```

### 4. Serialization

✅ **DO**:
- Use MemoryPack for performance
- Add `[MemoryPackable]` to all messages
- Make records `partial`
- Use simple types (primitives, strings, lists)

❌ **DON'T**:
- Use complex inheritance hierarchies
- Include circular references
- Use interfaces in message properties
- Forget `MessageId` property

### 5. Testing

```csharp
[Fact]
public async Task CreateOrder_ValidCommand_ReturnsSuccess()
{
    // Arrange
    var store = new OrderStore();
    var mediator = Substitute.For<ICatgaMediator>();
    var handler = new CreateOrderHandler(store, mediator);
    
    var command = new CreateOrderCommand(
        "customer-1", 
        new List<OrderItem> { new("p1", "Product", 1, 99.99m) });
    
    // Act
    var result = await handler.HandleAsync(command);
    
    // Assert
    result.IsSuccess.Should().BeTrue();
    result.Value.Should().NotBeNull();
    result.Value!.Total.Should().Be(99.99m);
    
    await mediator.Received(1).PublishAsync(
        Arg.Any<OrderCreatedEvent>(), 
        Arg.Any<CancellationToken>());
}
```

---

## Common Pitfalls

### 1. Missing MessageId

❌ **Wrong**:
```csharp
public record CreateOrderCommand(string CustomerId) : IRequest<OrderCreatedResult>;
```

✅ **Correct**:
```csharp
public record CreateOrderCommand(string CustomerId) : IRequest<OrderCreatedResult>
{
    public long MessageId { get; init; }
}
```

### 2. Forgetting MemoryPackable

❌ **Wrong**:
```csharp
public record CreateOrderCommand(...) : IRequest<OrderCreatedResult>;
```

✅ **Correct**:
```csharp
[MemoryPackable]
public partial record CreateOrderCommand(...) : IRequest<OrderCreatedResult>;
```

### 3. Blocking Async Operations

❌ **Wrong**:
```csharp
public ValueTask<CatgaResult> HandleAsync(Command cmd, CancellationToken ct)
{
    var result = SomeAsyncOperation().Result; // ❌ Blocking!
    return ValueTask.FromResult(CatgaResult.Success());
}
```

✅ **Correct**:
```csharp
public async ValueTask<CatgaResult> HandleAsync(Command cmd, CancellationToken ct)
{
    var result = await SomeAsyncOperation(); // ✅ Async
    return CatgaResult.Success();
}
```

### 4. Not Handling Cancellation

❌ **Wrong**:
```csharp
public async ValueTask<CatgaResult> HandleAsync(Command cmd, CancellationToken ct)
{
    await Task.Delay(1000); // ❌ Ignores cancellation
}
```

✅ **Correct**:
```csharp
public async ValueTask<CatgaResult> HandleAsync(Command cmd, CancellationToken ct)
{
    await Task.Delay(1000, ct); // ✅ Respects cancellation
}
```

### 5. Mutable Messages

❌ **Wrong**:
```csharp
public class CreateOrderCommand : IRequest<OrderCreatedResult>
{
    public string CustomerId { get; set; } // ❌ Mutable
}
```

✅ **Correct**:
```csharp
[MemoryPackable]
public partial record CreateOrderCommand(string CustomerId) : IRequest<OrderCreatedResult>
{
    public long MessageId { get; init; }
}
```

---

## Code Generation Guidelines

### When Helping Developers

1. **Always include**:
   - `[MemoryPackable]` attribute
   - `partial` keyword for records
   - `MessageId` property
   - Proper interface (`IRequest<T>`, `IEvent`)

2. **Use proper naming**:
   - Commands: Imperative (CreateOrder, UpdateOrder)
   - Events: Past tense (OrderCreated, OrderUpdated)
   - Queries: Get/Find prefix (GetOrder, FindOrders)

3. **Follow patterns**:
   - Commands return `CatgaResult<T>`
   - Queries return `CatgaResult<T>`
   - Events return `ValueTask`

4. **Include error handling**:
   - Validate inputs
   - Return meaningful error messages
   - Don't throw exceptions for business logic

5. **Add documentation**:
   - XML comments for public APIs
   - Inline comments for complex logic
   - Examples in documentation

### Example Template

```csharp
/// <summary>
/// Creates a new order for a customer.
/// </summary>
[MemoryPackable]
public partial record CreateOrderCommand(
    string CustomerId, 
    List<OrderItem> Items) : IRequest<OrderCreatedResult>
{
    public long MessageId { get; init; }
}

/// <summary>
/// Result of creating an order.
/// </summary>
[MemoryPackable]
public partial record OrderCreatedResult(
    string OrderId, 
    decimal Total, 
    DateTime CreatedAt);

/// <summary>
/// Handles order creation.
/// </summary>
public sealed class CreateOrderHandler(
    IOrderRepository repository, 
    ICatgaMediator mediator) 
    : IRequestHandler<CreateOrderCommand, OrderCreatedResult>
{
    public async ValueTask<CatgaResult<OrderCreatedResult>> HandleAsync(
        CreateOrderCommand cmd, 
        CancellationToken ct = default)
    {
        // Validate
        if (string.IsNullOrEmpty(cmd.CustomerId))
            return CatgaResult<OrderCreatedResult>.Failure("Customer ID is required");
        
        if (cmd.Items == null || cmd.Items.Count == 0)
            return CatgaResult<OrderCreatedResult>.Failure("Order must have at least one item");
        
        // Execute
        var orderId = Guid.NewGuid().ToString("N")[..8];
        var total = cmd.Items.Sum(i => i.Price * i.Quantity);
        var now = DateTime.UtcNow;
        
        var order = new Order(orderId, cmd.CustomerId, cmd.Items, total, now);
        await repository.SaveAsync(order, ct);
        
        // Publish event
        await mediator.PublishAsync(
            new OrderCreatedEvent(orderId, cmd.CustomerId, total, now), ct);
        
        // Return result
        return CatgaResult<OrderCreatedResult>.Success(
            new OrderCreatedResult(orderId, total, now));
    }
}
```

---

## Testing Strategies

### Unit Testing

```csharp
public class CreateOrderHandlerTests
{
    [Fact]
    public async Task Handle_ValidCommand_CreatesOrder()
    {
        // Arrange
        var repository = Substitute.For<IOrderRepository>();
        var mediator = Substitute.For<ICatgaMediator>();
        var handler = new CreateOrderHandler(repository, mediator);
        
        var command = new CreateOrderCommand(
            "customer-1",
            new List<OrderItem> { new("p1", "Product", 1, 99.99m) });
        
        // Act
        var result = await handler.HandleAsync(command);
        
        // Assert
        result.IsSuccess.Should().BeTrue();
        await repository.Received(1).SaveAsync(
            Arg.Any<Order>(), 
            Arg.Any<CancellationToken>());
    }
    
    [Fact]
    public async Task Handle_EmptyCustomerId_ReturnsFailure()
    {
        // Arrange
        var handler = new CreateOrderHandler(null!, null!);
        var command = new CreateOrderCommand("", new List<OrderItem>());
        
        // Act
        var result = await handler.HandleAsync(command);
        
        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Customer ID");
    }
}
```

### Integration Testing

```csharp
public class OrderSystemIntegrationTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    
    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        
        builder.Services.AddCatga()
            .UseMemoryPack()
            .UseInMemory();
        
        builder.Services.AddInMemoryTransport();
        builder.Services.AddCatgaHandlers();
        
        _app = builder.Build();
        _app.MapPost("/orders", async (CreateOrderRequest req, ICatgaMediator mediator) =>
        {
            var result = await mediator.SendAsync<CreateOrderCommand, OrderCreatedResult>(
                new CreateOrderCommand(req.CustomerId, req.Items));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });
        
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
    }
    
    [Fact]
    public async Task CreateOrder_ValidRequest_ReturnsCreated()
    {
        // Arrange
        var request = new CreateOrderRequest(
            "customer-1",
            new List<OrderItem> { new("p1", "Product", 1, 99.99m) });
        
        // Act
        var response = await _client.PostAsJsonAsync("/orders", request);
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<OrderCreatedResult>();
        result.Should().NotBeNull();
        result!.Total.Should().Be(99.99m);
    }
    
    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        _client.Dispose();
    }
}
```

---

## Performance Optimization

### 1. Use MemoryPack

MemoryPack is 10-50x faster than JSON:

```csharp
// Configure
builder.Services.AddCatga().UseMemoryPack();
```

### 2. Choose Right Backend

- **InMemory**: Development, < 1ms latency
- **Redis**: Production, 1-5ms latency, distributed
- **NATS**: High-performance, < 2ms latency, 100k+ ops/sec

### 3. Enable AOT Compilation

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
</PropertyGroup>
```

### 4. Use ValueTask

```csharp
// Good - ValueTask for hot paths
public ValueTask<CatgaResult> HandleAsync(Command cmd, CancellationToken ct)
{
    if (IsValid(cmd))
        return ValueTask.FromResult(CatgaResult.Success());
    
    return HandleAsyncCore(cmd, ct);
}

private async ValueTask<CatgaResult> HandleAsyncCore(Command cmd, CancellationToken ct)
{
    // Async work
}
```

### 5. Batch Operations

```csharp
// Process multiple commands
var tasks = commands.Select(cmd => mediator.SendAsync(cmd));
var results = await Task.WhenAll(tasks);
```

---

## Troubleshooting

### Issue: "JsonTypeInfo metadata not provided"

**Cause**: Missing JSON serialization context

**Solution**: Add types to `JsonSerializerContext`:

```csharp
[JsonSerializable(typeof(Order))]
[JsonSerializable(typeof(OrderCreatedResult))]
internal partial class AppJsonContext : JsonSerializerContext;
```

### Issue: "Handler not found"

**Cause**: Handler not registered

**Solution**: Call `AddCatgaHandlers()`:

```csharp
builder.Services.AddCatgaHandlers();
```

### Issue: "MemoryPack serialization failed"

**Cause**: Missing `[MemoryPackable]` or `partial`

**Solution**: Add attributes:

```csharp
[MemoryPackable]
public partial record MyCommand(...) : IRequest<MyResult>;
```

### Issue: "Deadlock in async code"

**Cause**: Blocking async operations

**Solution**: Use `await` instead of `.Result`:

```csharp
// Bad
var result = SomeAsyncMethod().Result;

// Good
var result = await SomeAsyncMethod();
```

### Issue: "High memory usage"

**Cause**: Not disposing resources

**Solution**: Use `using` or implement `IDisposable`:

```csharp
await using var scope = serviceProvider.CreateAsyncScope();
var mediator = scope.ServiceProvider.GetRequiredService<ICatgaMediator>();
```

---

## Quick Reference

### Message Types

| Type | Interface | Returns | Use Case |
|------|-----------|---------|----------|
| Command | `IRequest<T>` | `CatgaResult<T>` | Write operations |
| Query | `IRequest<T>` | `CatgaResult<T>` | Read operations |
| Event | `IEvent` | `ValueTask` | Domain events |

### Handler Types

| Type | Interface | Method | Returns |
|------|-----------|--------|---------|
| Command/Query | `IRequestHandler<TRequest, TResponse>` | `HandleAsync` | `ValueTask<CatgaResult<TResponse>>` |
| Event | `IEventHandler<TEvent>` | `HandleAsync` | `ValueTask` |

### Configuration

```csharp
// Minimal setup
builder.Services.AddCatga()
    .UseMemoryPack()
    .UseInMemory();
builder.Services.AddInMemoryTransport();
builder.Services.AddCatgaHandlers();

// Production setup
builder.Services.AddCatga()
    .UseMemoryPack()
    .UseRedis("localhost:6379");
builder.Services.AddRedisTransport("localhost:6379");
builder.Services.AddCatgaHandlers();
```

---

## Additional Resources

- **Example**: See `examples/OrderSystem/` for complete working example
- **Documentation**: See `docs/` for detailed guides
- **Tests**: See `tests/Catga.Tests/` for test examples
- **Benchmarks**: See `benchmarks/` for performance comparisons

---

## Summary for AI Assistants

When helping developers with Catga:

1. ✅ Always include `[MemoryPackable]` and `partial`
2. ✅ Always include `MessageId` property
3. ✅ Use proper interfaces (`IRequest<T>`, `IEvent`)
4. ✅ Return `CatgaResult<T>` for commands/queries
5. ✅ Use `ValueTask` for async operations
6. ✅ Handle `CancellationToken` properly
7. ✅ Make handlers `sealed`
8. ✅ Validate inputs and return meaningful errors
9. ✅ Follow naming conventions
10. ✅ Include XML documentation

**Remember**: Catga is designed for simplicity and performance. Keep code minimal, type-safe, and AOT-compatible.
