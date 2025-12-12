# 🚀 Getting Started with Catga

Welcome to Catga! This 5-minute guide will help you build your first high-performance CQRS application from scratch.

<div align="center">

**Nanosecond Latency · Low Memory · Zero Reflection · Source Generated · Production Ready**

</div>

---

## 📋 Prerequisites

- ✅ [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or later
- ✅ IDE: Visual Studio 2022+ / VS Code / Rider
- ✅ Basic C# and ASP.NET Core knowledge

---

## 🎯 Step 1: Create Project

### 1.1 Create Web API Project

```bash
# Create new project
dotnet new webapi -n MyFirstCatgaApp
cd MyFirstCatgaApp

# Remove default WeatherForecast (not needed)
rm WeatherForecast.cs Controllers/WeatherForecastController.cs
```

### 1.2 Install Catga Packages

```bash
# Core package (required)
dotnet add package Catga

# Transport layer (choose one)
dotnet add package Catga.Transport.InMemory  # Recommended: dev and monolith apps

# Optional: ASP.NET Core integration
dotnet add package Catga.AspNetCore
```

---

## 📦 Step 2: Configure Catga

Open `Program.cs` and configure Catga:

```csharp
using Catga;
using Catga.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ⭐ Add Catga services (one line, auto-registers all handlers)
// Use environment-based configuration for optimal performance
builder.Services.AddCatga(options =>
{
    if (builder.Environment.IsDevelopment())
        options.ForDevelopment();  // Detailed logging for debugging
    else
        options.Minimal();         // Max performance for production
});

// Optional: Add in-memory transport (development)
builder.Services.AddInMemoryTransport();

// Optional: Add ASP.NET Core endpoints
builder.Services.AddCatgaEndpoints();

// Add Controllers (for REST API)
builder.Services.AddControllers();

// Add Swagger (optional, recommended)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();

// Optional: Map Catga diagnostic endpoints
app.MapCatgaDiagnostics(); // Access /catga/health, /catga/metrics

app.Run();
```

**That's it!** Catga will automatically discover and register all handlers via source generator.

---

## 💬 Step 3: Define Messages

Create a `Messages/` folder and define your messages:

### Commands

```csharp
// Messages/CreateUserCommand.cs
using Catga.Abstractions;
using MemoryPack;

namespace MyFirstCatgaApp.Messages;

/// <summary>
/// Create user command.
/// MessageId is auto-generated (by source generator).
/// </summary>
[MemoryPackable]
public partial record CreateUserCommand(string Name, string Email) : IRequest<User>;

/// <summary>
/// User data.
/// </summary>
[MemoryPackable]
public partial record User(int Id, string Name, string Email);
```

### Events

```csharp
// Messages/UserCreatedEvent.cs
using Catga.Abstractions;
using MemoryPack;

namespace MyFirstCatgaApp.Messages;

/// <summary>
/// User created event.
/// Multiple handlers can subscribe to the same event.
/// </summary>
[MemoryPackable]
public partial record UserCreatedEvent(int UserId, string Name, string Email) : IEvent;
```

### Queries

```csharp
// Messages/GetUserQuery.cs
using Catga.Abstractions;
using MemoryPack;

namespace MyFirstCatgaApp.Messages;

/// <summary>
/// Get user query.
/// </summary>
[MemoryPackable]
public partial record GetUserQuery(int UserId) : IRequest<User?>;
```

---

## 🎯 Step 4: Implement Handlers

Create a `Handlers/` folder and implement your business logic:

### Command Handler

```csharp
// Handlers/CreateUserHandler.cs
using Catga.Abstractions;
using Catga.Core;
using MyFirstCatgaApp.Messages;

namespace MyFirstCatgaApp.Handlers;

/// <summary>
/// Create user handler.
/// Auto-registered by source generator.
/// </summary>
public class CreateUserHandler : IRequestHandler<CreateUserCommand, User>
{
    // Simulated database
    private static readonly List<User> _users = new();
    private static int _nextId = 1;

    public ValueTask<CatgaResult<User>> HandleAsync(
        CreateUserCommand request,
        CancellationToken cancellationToken = default)
    {
        // 1️⃣ Validation
        if (string.IsNullOrWhiteSpace(request.Name))
            return new(CatgaResult<User>.Failure("Name cannot be empty"));

        if (string.IsNullOrWhiteSpace(request.Email))
            return new(CatgaResult<User>.Failure("Email cannot be empty"));

        if (_users.Any(u => u.Email == request.Email))
            return new(CatgaResult<User>.Failure("Email already exists"));

        // 2️⃣ Create user
        var user = new User(_nextId++, request.Name, request.Email);
        _users.Add(user);

        // 3️⃣ Return success result
        return new(CatgaResult<User>.Success(user));

        // ✅ Auto tracing, auto metrics, auto error handling!
    }
}
```

### Query Handler

```csharp
// Handlers/GetUserHandler.cs
using Catga.Abstractions;
using Catga.Core;
using MyFirstCatgaApp.Messages;

namespace MyFirstCatgaApp.Handlers;

/// <summary>
/// Get user handler.
/// </summary>
public class GetUserHandler : IRequestHandler<GetUserQuery, User?>
{
    private readonly IUserRepository _repository;

    public GetUserHandler(IUserRepository repository)
    {
        _repository = repository;
    }

    public ValueTask<CatgaResult<User?>> HandleAsync(
        GetUserQuery request,
        CancellationToken cancellationToken = default)
    {
        var user = _repository.GetById(request.UserId);
        return new(CatgaResult<User?>.Success(user));
    }
}
```

### Event Handler

```csharp
// Handlers/UserCreatedEventHandler.cs
using Catga.Abstractions;
using MyFirstCatgaApp.Messages;

namespace MyFirstCatgaApp.Handlers;

/// <summary>
/// User created event handler.
/// Multiple event handlers can subscribe to the same event.
/// </summary>
public class UserCreatedEventHandler : IEventHandler<UserCreatedEvent>
{
    private readonly ILogger<UserCreatedEventHandler> _logger;

    public UserCreatedEventHandler(ILogger<UserCreatedEventHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(
        UserCreatedEvent @event,
        CancellationToken cancellationToken = default)
    {
        // Send welcome email, log audit, update statistics, etc.
        _logger.LogInformation(
            "User created: {UserId} - {Name} ({Email})",
            @event.UserId, @event.Name, @event.Email
        );

        // You can do anything here:
        // - Send emails
        // - Update cache
        // - Send to message queue
        // - Call other services
        return Task.CompletedTask;
    }
}
```

---

## 🌐 Step 5: Create API Controller

Create a `Controllers/` folder:

```csharp
// Controllers/UsersController.cs
using Microsoft.AspNetCore.Mvc;
using Catga.Abstractions;
using MyFirstCatgaApp.Messages;

namespace MyFirstCatgaApp.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly ICatgaMediator _mediator;

    public UsersController(ICatgaMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Create user.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(User), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
    {
        var command = new CreateUserCommand(request.Name, request.Email);
        var result = await _mediator.SendAsync(command);

        if (result.IsSuccess)
        {
            // Optional: Publish event
            await _mediator.PublishAsync(new UserCreatedEvent(
                result.Value.Id,
                result.Value.Name,
                result.Value.Email
            ));

            return Ok(result.Value);
        }
        else
        {
            return BadRequest(new { error = result.Error });
        }
    }

    /// <summary>
    /// Get user.
    /// </summary>
    [HttpGet("{userId}")]
    [ProducesResponseType(typeof(User), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUser(int userId)
    {
        var query = new GetUserQuery(userId);
        var result = await _mediator.SendAsync(query);

        if (result.IsSuccess && result.Value != null)
            return Ok(result.Value);
        else
            return NotFound();
    }
}

/// <summary>
/// Create user request DTO.
/// </summary>
public record CreateUserRequest(string Name, string Email);
```

---

## 🎉 Step 6: Run and Test

### 6.1 Start Application

```bash
dotnet run
```

Output:
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:7001
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
```

### 6.2 Open Swagger

Open browser: `https://localhost:7001/swagger`

### 6.3 Test API

#### Create User

```bash
curl -X POST https://localhost:7001/api/users \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Alice",
    "email": "alice@example.com"
  }'
```

Response:
```json
{
  "id": 1,
  "name": "Alice",
  "email": "alice@example.com"
}
```

#### Get User

```bash
curl https://localhost:7001/api/users/1
```

Response:
```json
{
  "id": 1,
  "name": "Alice",
  "email": "alice@example.com"
}
```

#### Check Logs

Check the console, you'll see event handling logs:
```
info: MyFirstCatgaApp.Handlers.UserCreatedEventHandler[0]
      User created: 1 - Alice (alice@example.com)
```

---

## 📊 Performance Benchmarks

Catga delivers excellent performance with minimal memory allocation:

### Real Benchmark Results

> BenchmarkDotNet on AMD Ryzen 7 5800H, .NET 9.0.8

| Operation | Catga (minimal) | MediatR | Memory Savings |
|-----------|-----------------|---------|----------------|
| **Command** | 206 ns | 185 ns | **88 B vs 424 B (4.8x less)** |
| **Query** | 205 ns | 208 ns | **32 B vs 368 B (11.5x less)** |
| **Event** | **119 ns** | 147 ns | **64 B vs 288 B (4.5x less)** |
| **Batch 100** | 13.9 μs | 13.4 μs | **8.8 KB vs 35.2 KB (4x less)** |

### Key Highlights

- ✅ **Event publishing 19% faster** than MediatR
- ✅ **Query performance on par** with MediatR
- ✅ **4-11x less memory allocation** across all operations

Run benchmarks yourself:
```bash
cd benchmarks/Catga.Benchmarks
dotnet run -c Release --filter *MediatRComparison*
```

---

## 🚀 Next Steps

### Extend Features

1. **Add Persistence**
   ```bash
   dotnet add package Catga.Persistence.Redis
   ```
   ```csharp
   builder.Services.AddRedisPersistence("localhost:6379");
   ```

2. **Add Distributed Messaging**
   ```bash
   dotnet add package Catga.Transport.Nats
   ```
   ```csharp
   builder.Services.AddNatsTransport("nats://localhost:4222");
   ```

3. **Add Serialization**
   ```bash
   dotnet add package Catga.Serialization.MemoryPack
   ```
   ```csharp
   builder.Services.AddCatga().UseMemoryPack();
   ```

4. **Add Testing**
   ```bash
   dotnet add package Catga.Testing
   dotnet add package xunit
   dotnet add package FluentAssertions
   ```

### Learning Resources

| Resource | Description | Time |
|----------|-------------|------|
| [Configuration Guide](./configuration.md) | Detailed configuration options | 30 min |
| [Architecture Overview](../architecture/overview.md) | Understand framework design | 30 min |
| [Error Handling](../guides/error-handling.md) | Exception handling and rollback | 20 min |
| [Performance Optimization](../guides/memory-optimization-guide.md) | Zero-allocation techniques | 1 hour |
| [Distributed Deployment](../deployment/kubernetes.md) | K8s deployment | 2 hours |
| [OrderSystem Example](../../examples/README.md) | Complete e-commerce system | 2 hours |

---

## 💡 FAQ

<details>
<summary>Q: Why are handlers auto-registered?</summary>

A: Catga uses a source generator to scan all classes implementing `IRequestHandler` or `IEventHandler` at compile time and automatically generates registration code. No manual registration needed!

</details>

<details>
<summary>Q: How is MessageId generated?</summary>

A: The source generator automatically generates `MessageId` property for all messages implementing `IMessage`, using Snowflake algorithm to ensure uniqueness and ordering.

</details>

<details>
<summary>Q: How to use Catga in tests?</summary>

A: Use the `Catga.Testing` package:

```csharp
var fixture = new CatgaTestFixture();
fixture.RegisterRequestHandler<CreateUserCommand, User, CreateUserHandler>();

var result = await fixture.Mediator.SendAsync(new CreateUserCommand("Test", "test@example.com"));
result.Should().BeSuccessful();
```

See [Testing Documentation](../../src/Catga.Testing/README.md)

</details>

<details>
<summary>Q: How to handle business exceptions?</summary>

A: Use `CatgaResult<T>`:

```csharp
// Success
return CatgaResult<User>.Success(user);

// Failure
return CatgaResult<User>.Failure("User not found");

// Exceptions are automatically caught and logged
```

See [Error Handling Guide](../guides/error-handling.md)

</details>

---

## 🎯 Complete Example

Check out the complete production-level example:

- **OrderSystem**: [OrderSystem 示例](/examples/README.md)
  - Complete e-commerce order system
  - Distributed deployment (3-node cluster)
  - Monitoring and tracing
  - Performance testing

---

## 📞 Get Help

- 💬 [GitHub Discussions](https://github.com/Cricle/Catga/discussions)
- 🐛 [Issue Tracker](https://github.com/Cricle/Catga/issues)
- 📚 [Full Documentation](../README.md)
- ⭐ [GitHub](https://github.com/Cricle/Catga)

---

<div align="center">

**Congratulations! You've mastered the basics of Catga!** 🎉

Now start building your high-performance CQRS application!

[Full Documentation](../README.md) · [Examples](../examples/basic-usage.md) · [Benchmarks](../BENCHMARK-RESULTS.md)

</div>


