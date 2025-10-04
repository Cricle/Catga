# 快速开始

本指南将帮助你在 5 分钟内启动并运行 Catga。

## 安装

### 使用 NuGet Package Manager

```bash
# 安装核心包
dotnet add package Catga

# 可选：安装 NATS 传输
dotnet add package Catga.Nats

# 可选：安装 Redis 持久化
dotnet add package Catga.Redis
```

### 使用 Package Manager Console

```powershell
Install-Package Catga
Install-Package Catga.Nats
Install-Package Catga.Redis
```

## 第一个 CQRS 应用

### 1. 定义消息

```csharp
using Catga.Messages;

// 查询
public record GetUserQuery(long UserId) : IQuery<User>;

// 命令
public record CreateUserCommand(string Name, string Email) : ICommand<long>;

// 事件
public record UserCreatedEvent(long UserId, string Name) : IEvent;

// 领域模型
public class User
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
```

### 2. 定义处理器

```csharp
using Catga.Handlers;
using Catga.Results;

// 查询处理器
public class GetUserHandler : IRequestHandler<GetUserQuery, User>
{
    private readonly IUserRepository _repository;

    public GetUserHandler(IUserRepository repository)
    {
        _repository = repository;
    }

    public async Task<TransitResult<User>> HandleAsync(
        GetUserQuery request,
        CancellationToken cancellationToken = default)
    {
        var user = await _repository.GetByIdAsync(request.UserId);
        
        if (user == null)
            return TransitResult<User>.Failure("User not found");
            
        return TransitResult<User>.Success(user);
    }
}

// 命令处理器
public class CreateUserHandler : IRequestHandler<CreateUserCommand, long>
{
    private readonly IUserRepository _repository;
    private readonly ITransitMediator _mediator;

    public CreateUserHandler(
        IUserRepository repository,
        ITransitMediator mediator)
    {
        _repository = repository;
        _mediator = mediator;
    }

    public async Task<TransitResult<long>> HandleAsync(
        CreateUserCommand request,
        CancellationToken cancellationToken = default)
    {
        var user = new User
        {
            Name = request.Name,
            Email = request.Email
        };
        
        var userId = await _repository.CreateAsync(user);
        
        // 发布事件
        await _mediator.PublishAsync(
            new UserCreatedEvent(userId, user.Name),
            cancellationToken);
            
        return TransitResult<long>.Success(userId);
    }
}

// 事件处理器
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
        _logger.LogInformation(
            "User created: {UserId} - {Name}",
            @event.UserId,
            @event.Name);
            
        return Task.CompletedTask;
    }
}
```

### 3. 注册服务

```csharp
using Catga.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// 注册 Catga
builder.Services.AddTransit(options =>
{
    // 使用开发环境预设（详细日志，无限流）
    options.ForDevelopment();
    
    // 或自定义配置
    // options.EnableLogging = true;
    // options.EnableTracing = true;
    // options.EnableIdempotency = true;
    // options.MaxConcurrentRequests = 1000;
});

// 注册处理器
builder.Services.AddRequestHandler<GetUserQuery, User, GetUserHandler>();
builder.Services.AddRequestHandler<CreateUserCommand, long, CreateUserHandler>();
builder.Services.AddEventHandler<UserCreatedEvent, UserCreatedEventHandler>();

// 注册仓储
builder.Services.AddSingleton<IUserRepository, InMemoryUserRepository>();

var app = builder.Build();
app.Run();
```

### 4. 使用 Mediator

#### 在 API Controller 中

```csharp
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly ITransitMediator _mediator;

    public UsersController(ITransitMediator mediator)
    {
        _mediator = mediator;
    }

    // GET api/users/5
    [HttpGet("{id}")]
    public async Task<ActionResult<User>> GetUser(long id)
    {
        var result = await _mediator.SendAsync<GetUserQuery, User>(
            new GetUserQuery(id));
            
        if (!result.IsSuccess)
            return NotFound(result.Error);
            
        return Ok(result.Value);
    }

    // POST api/users
    [HttpPost]
    public async Task<ActionResult<long>> CreateUser(
        [FromBody] CreateUserRequest request)
    {
        var result = await _mediator.SendAsync<CreateUserCommand, long>(
            new CreateUserCommand(request.Name, request.Email));
            
        if (!result.IsSuccess)
            return BadRequest(result.Error);
            
        return CreatedAtAction(
            nameof(GetUser),
            new { id = result.Value },
            result.Value);
    }
}

public record CreateUserRequest(string Name, string Email);
```

#### 在服务中

```csharp
public class UserService
{
    private readonly ITransitMediator _mediator;

    public UserService(ITransitMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<User?> GetUserAsync(long id)
    {
        var result = await _mediator.SendAsync<GetUserQuery, User>(
            new GetUserQuery(id));
            
        return result.IsSuccess ? result.Value : null;
    }

    public async Task<long> CreateUserAsync(string name, string email)
    {
        var result = await _mediator.SendAsync<CreateUserCommand, long>(
            new CreateUserCommand(name, email));
            
        if (!result.IsSuccess)
            throw new InvalidOperationException(result.Error);
            
        return result.Value!;
    }
}
```

## 配置选项

### 预设配置

```csharp
// 开发环境（所有日志，无限流）
services.AddTransit(opt => opt.ForDevelopment());

// 高性能（5000 并发，64 分片）
services.AddTransit(opt => opt.WithHighPerformance());

// 完整弹性（熔断器 + 限流）
services.AddTransit(opt => opt.WithResilience());

// 最小化（零开销）
services.AddTransit(opt => opt.Minimal());
```

### 自定义配置

```csharp
services.AddTransit(options =>
{
    // Pipeline Behaviors
    options.EnableLogging = true;
    options.EnableTracing = true;
    options.EnableIdempotency = true;
    options.EnableValidation = true;
    options.EnableRetry = true;
    
    // 性能
    options.MaxConcurrentRequests = 2000;
    options.IdempotencyShardCount = 64;
    options.IdempotencyRetentionHours = 24;
    
    // 弹性
    options.EnableCircuitBreaker = true;
    options.CircuitBreakerFailureThreshold = 10;
    options.EnableRateLimiting = true;
    options.RateLimitRequestsPerSecond = 1000;
    
    // 死信队列
    options.EnableDeadLetterQueue = true;
});
```

## 验证安装

创建一个简单的测试：

```csharp
// 测试查询
var result = await _mediator.SendAsync<GetUserQuery, User>(
    new GetUserQuery(1));

if (result.IsSuccess)
{
    Console.WriteLine($"User found: {result.Value.Name}");
}
else
{
    Console.WriteLine($"Error: {result.Error}");
}
```

## 下一步

- 📖 了解 [CQRS 模式](../architecture/cqrs.md)
- 🎯 学习 [Pipeline 行为](../architecture/pipeline-behaviors.md)
- 🚀 探索 [分布式事务](distributed-transactions.md)
- 📊 查看 [完整示例](../examples/simple-cqrs.md)

## 常见问题

### Q: 如何处理验证？

使用 `ValidationBehavior`：

```csharp
public class CreateUserValidator : IValidator<CreateUserCommand>
{
    public Task<List<string>> ValidateAsync(
        CreateUserCommand command,
        CancellationToken ct)
    {
        var errors = new List<string>();
        
        if (string.IsNullOrEmpty(command.Name))
            errors.Add("Name is required");
            
        if (string.IsNullOrEmpty(command.Email))
            errors.Add("Email is required");
            
        return Task.FromResult(errors);
    }
}

// 注册
services.AddValidator<CreateUserCommand, CreateUserValidator>();
```

### Q: 如何处理异常？

使用 Result 模式：

```csharp
var result = await _mediator.SendAsync<MyCommand, Result>(command);

if (!result.IsSuccess)
{
    // 处理错误
    _logger.LogError(result.Error);
    
    if (result.Exception != null)
    {
        // 处理异常
        _logger.LogError(result.Exception, "Command failed");
    }
}
```

### Q: 如何启用分布式追踪？

```csharp
services.AddTransit(options =>
{
    options.EnableTracing = true;
});

// 与 OpenTelemetry 集成
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Catga.Transit")
        .AddConsoleExporter());
```

---

**恭喜！** 🎉 你已经成功创建了第一个 Catga 应用！

