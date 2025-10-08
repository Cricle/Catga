# Friendly API Design

Catga is designed to be **simple, intuitive, and powerful** while maintaining full Native AOT compatibility.

## 🎯 Design Principles

1. **Simple by Default** - Common tasks should be easy
2. **Explicit when Needed** - Advanced features don't hide complexity
3. **AOT First** - Zero reflection, compile-time safety
4. **Convention over Configuration** - Sensible defaults, minimal setup

## ✨ Developer Experience

### Before: Manual Registration (Complex)

```csharp
public void ConfigureServices(IServiceCollection services)
{
    // Manual registration - tedious and error-prone
    services.AddScoped<IRequestHandler<CreateUserCommand, CreateUserResponse>, CreateUserCommandHandler>();
    services.AddScoped<IRequestHandler<UpdateUserCommand, UpdateUserResponse>, UpdateUserCommandHandler>();
    services.AddScoped<IRequestHandler<DeleteUserCommand, Unit>, DeleteUserCommandHandler>();
    services.AddScoped<IRequestHandler<GetUserQuery, UserDto>, GetUserQueryHandler>();
    services.AddScoped<IRequestHandler<ListUsersQuery, List<UserDto>>, ListUsersQueryHandler>();
    
    services.AddScoped<IEventHandler<UserCreatedEvent>, UserCreatedEventHandler>();
    services.AddScoped<IEventHandler<UserCreatedEvent>, SendWelcomeEmailHandler>();
    services.AddScoped<IEventHandler<UserUpdatedEvent>, UserUpdatedEventHandler>();
    services.AddScoped<IEventHandler<UserDeletedEvent>, UserDeletedEventHandler>();
    
    // 50+ more handlers...
    
    // Easy to forget handlers or make typos
    // No compile-time safety
    // Hard to maintain
}
```

### After: Source Generator (Simple)

```csharp
public void ConfigureServices(IServiceCollection services)
{
    // ONE LINE - Source generator handles everything!
    services.AddCatga();
    services.AddGeneratedHandlers();  // ✨ Magic happens here
    
    // All handlers automatically discovered and registered
    // Compile-time safety
    // Fully AOT compatible
    // Easy to maintain
}
```

## 🚀 Quick Start

### 1. Minimal Setup

```csharp
using Catga;
using Catga.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// That's it! Ready to use
builder.Services.AddCatga();
builder.Services.AddGeneratedHandlers();

var app = builder.Build();
```

### 2. Simple Handler

```csharp
// No attributes needed - just implement the interface
public class CreateUserHandler : IRequestHandler<CreateUserCommand, User>
{
    public async Task<CatgaResult<User>> HandleAsync(
        CreateUserCommand request, 
        CancellationToken cancellationToken = default)
    {
        var user = new User { Name = request.Name };
        return CatgaResult<User>.Success(user);
    }
}
```

### 3. Use in API

```csharp
app.MapPost("/users", async (ICatgaMediator mediator, CreateUserCommand command) =>
{
    var result = await mediator.SendAsync<CreateUserCommand, User>(command);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
});
```

## 🎨 API Surface

### Core Types

```csharp
// Message contracts
public interface IMessage { }
public interface IRequest<TResponse> : IMessage { }
public interface IEvent : IMessage { }

// Handler contracts
public interface IRequestHandler<TRequest, TResponse> { }
public interface IEventHandler<TEvent> { }

// Mediator (single entry point)
public interface ICatgaMediator
{
    Task<CatgaResult<TResponse>> SendAsync<TRequest, TResponse>(TRequest request, ...);
    Task PublishAsync<TEvent>(TEvent @event, ...);
}

// Result type (explicit success/failure)
public class CatgaResult<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }
    
    public static CatgaResult<T> Success(T value);
    public static CatgaResult<T> Failure(string error);
}
```

### Extension Methods

```csharp
// Core setup
services.AddCatga(options => { ... });

// Source generator (recommended)
services.AddGeneratedHandlers();

// Serialization
services.AddSingleton<IMessageSerializer, JsonMessageSerializer>();

// Transport
services.AddNatsTransport(options => { ... });
services.AddRedisTransport(options => { ... });

// Persistence
services.AddRedisPersistence(options => { ... });
```

## 🏗 Progressive Disclosure

Catga follows the principle of **progressive disclosure**: simple things are simple, complex things are possible.

### Level 1: Basic Usage (90% of use cases)

```csharp
// Setup
services.AddCatga();
services.AddGeneratedHandlers();

// Use
await mediator.SendAsync<MyCommand, MyResponse>(command);
await mediator.PublishAsync(@event);
```

### Level 2: Add Features as Needed

```csharp
services.AddCatga(options =>
{
    options.EnableLogging = true;        // Logging
    options.EnableIdempotency = true;    // Idempotency
    options.EnableRetry = true;          // Retry
});
```

### Level 3: Advanced Scenarios

```csharp
// Custom behaviors
services.AddScoped<IPipelineBehavior, CustomBehavior>();

// Distributed messaging
services.AddNatsTransport(options => { ... });

// Outbox pattern
services.AddOutbox<NatsOutboxStore>();

// Custom serialization
services.AddSingleton<IMessageSerializer, CustomSerializer>();
```

## 💡 Design Decisions

### Why Source Generator?

**Problem**: Reflection-based registration isn't AOT compatible

**Solutions Considered**:
1. ❌ Reflection + `[DynamicallyAccessedMembers]` - Still has runtime overhead
2. ❌ Manual registration - Tedious and error-prone
3. ✅ **Source Generator** - Best of both worlds!

**Benefits**:
- ✅ Compile-time discovery
- ✅ Zero reflection
- ✅ Full AOT compatibility
- ✅ Better IDE experience
- ✅ No runtime overhead

### Why `CatgaResult<T>`?

**Problem**: Exceptions for control flow are expensive

**Solutions Considered**:
1. ❌ Throw exceptions - Performance cost, unclear API
2. ❌ Return null - Can't distinguish errors
3. ✅ **Result type** - Explicit success/failure

**Benefits**:
- ✅ Explicit error handling
- ✅ No exception overhead
- ✅ Chainable operations
- ✅ Clear API intent

### Why `ICatgaMediator`?

**Problem**: Direct handler dependencies create tight coupling

**Solutions Considered**:
1. ❌ Direct handler calls - Tight coupling
2. ❌ Event aggregator - Indirect, hard to trace
3. ✅ **Mediator** - Loose coupling, clear flow

**Benefits**:
- ✅ Single point of entry
- ✅ Easy to add cross-cutting concerns (logging, validation, etc.)
- ✅ Testable
- ✅ Supports both commands and events

## 📊 Comparison

| Feature | Catga | MediatR | MassTransit |
|---------|-------|---------|-------------|
| **Setup Complexity** | ⭐ Simple | ⭐⭐ Medium | ⭐⭐⭐ Complex |
| **AOT Support** | ✅ Full | ❌ Partial | ❌ Limited |
| **Source Generator** | ✅ Yes | ❌ No | ❌ No |
| **Distributed** | ✅ Yes | ❌ No | ✅ Yes |
| **Result Type** | ✅ Built-in | ❌ No | ❌ No |
| **Learning Curve** | ⭐ Easy | ⭐ Easy | ⭐⭐⭐ Steep |

## 🎓 Best Practices

### 1. Keep Handlers Simple

```csharp
// ✅ Good - Single responsibility
public class CreateUserHandler : IRequestHandler<CreateUserCommand, User>
{
    private readonly IUserRepository _repository;
    
    public CreateUserHandler(IUserRepository repository)
    {
        _repository = repository;
    }
    
    public async Task<CatgaResult<User>> HandleAsync(CreateUserCommand request, ...)
    {
        var user = new User { Name = request.Name };
        await _repository.SaveAsync(user);
        return CatgaResult<User>.Success(user);
    }
}
```

### 2. Use Events for Side Effects

```csharp
// ✅ Good - Decouple side effects
public class CreateUserHandler : IRequestHandler<CreateUserCommand, User>
{
    private readonly ICatgaMediator _mediator;
    
    public async Task<CatgaResult<User>> HandleAsync(CreateUserCommand request, ...)
    {
        var user = await CreateUser(request);
        
        // Publish event for side effects
        await _mediator.PublishAsync(new UserCreatedEvent { UserId = user.Id });
        
        return CatgaResult<User>.Success(user);
    }
}

// Separate handler for sending emails
public class SendWelcomeEmailHandler : IEventHandler<UserCreatedEvent>
{
    public Task HandleAsync(UserCreatedEvent @event, ...)
    {
        // Send welcome email
        return Task.CompletedTask;
    }
}
```

### 3. Explicit Error Handling

```csharp
// ✅ Good - Return explicit errors
public async Task<CatgaResult<User>> HandleAsync(CreateUserCommand request, ...)
{
    if (string.IsNullOrEmpty(request.Name))
        return CatgaResult<User>.Failure("Name is required");
    
    if (await _repository.ExistsAsync(request.Email))
        return CatgaResult<User>.Failure("Email already exists");
    
    var user = await CreateUser(request);
    return CatgaResult<User>.Success(user);
}
```

## 🔗 Related

- [Source Generator Guide](source-generator.md)
- [Getting Started](GETTING_STARTED.md)
- [AOT Guide](../aot/README.md)
- [Examples](../../examples/)
