# Catga 快速开始

本指南将在 5 分钟内带你构建第一个 Catga 应用。

---

## 📦 安装

### 1. 创建项目

```bash
dotnet new webapi -n MyApp
cd MyApp
```

### 2. 安装 NuGet 包

```bash
# 核心框架
dotnet add package Catga
dotnet add package Catga.InMemory

# AOT 兼容序列化
dotnet add package Catga.Serialization.MemoryPack

# Source Generator（自动注册）
dotnet add package Catga.SourceGenerator

# ASP.NET Core 集成
dotnet add package Catga.AspNetCore
```

---

## 🚀 快速示例

### 步骤 1: 定义消息

创建 `Messages.cs`：

```csharp
using Catga.Messages;
using MemoryPack;

namespace MyApp;

// 命令（有返回值）
[MemoryPackable]
public partial record CreateUserCommand(
    string Name,
    string Email
) : IRequest<UserCreatedResult>;

// 命令结果
[MemoryPackable]
public partial record UserCreatedResult(
    string UserId,
    DateTime CreatedAt
);

// 事件（通知）
[MemoryPackable]
public partial record UserCreatedEvent(
    string UserId,
    string Name,
    string Email,
    DateTime CreatedAt
) : IEvent;
```

### 步骤 2: 实现 Handler

创建 `Handlers/CreateUserHandler.cs`：

```csharp
using Catga;
using Catga.Core;
using Catga.Exceptions;

namespace MyApp.Handlers;

/// <summary>
/// 创建用户 Handler - 继承 SafeRequestHandler，无需 try-catch！
/// </summary>
public class CreateUserHandler : SafeRequestHandler<CreateUserCommand, UserCreatedResult>
{
    private readonly IUserRepository _repository;
    private readonly ICatgaMediator _mediator;

    public CreateUserHandler(
        IUserRepository repository,
        ICatgaMediator mediator,
        ILogger<CreateUserHandler> logger) : base(logger)
    {
        _repository = repository;
        _mediator = mediator;
    }

    /// <summary>
    /// 只需编写业务逻辑，框架自动处理异常！
    /// </summary>
    protected override async Task<UserCreatedResult> HandleCoreAsync(
        CreateUserCommand request,
        CancellationToken cancellationToken)
    {
        // 验证（直接抛出异常，框架自动转换为 CatgaResult.Failure）
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new CatgaException("Name is required");

        if (string.IsNullOrWhiteSpace(request.Email))
            throw new CatgaException("Email is required");

        // 检查重复
        if (await _repository.ExistsByEmailAsync(request.Email, cancellationToken))
            throw new CatgaException($"Email '{request.Email}' already exists");

        // 创建用户
        var userId = Guid.NewGuid().ToString("N");
        var user = new User
        {
            Id = userId,
            Name = request.Name,
            Email = request.Email,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.SaveAsync(user, cancellationToken);

        // 发布事件
        await _mediator.PublishAsync(new UserCreatedEvent(
            userId,
            request.Name,
            request.Email,
            user.CreatedAt
        ), cancellationToken);

        Logger.LogInformation("User created: {UserId}", userId);

        // 直接返回结果，无需包装为 CatgaResult！
        return new UserCreatedResult(userId, user.CreatedAt);
    }
}
```

### 步骤 3: 实现事件 Handler

创建 `Handlers/UserEventHandlers.cs`：

```csharp
using Catga.Handlers;

namespace MyApp.Handlers;

/// <summary>
/// 发送欢迎邮件
/// </summary>
public class SendWelcomeEmailHandler : IEventHandler<UserCreatedEvent>
{
    private readonly IEmailService _emailService;
    private readonly ILogger<SendWelcomeEmailHandler> _logger;

    public SendWelcomeEmailHandler(
        IEmailService emailService,
        ILogger<SendWelcomeEmailHandler> logger)
    {
        _emailService = emailService;
        _logger = logger;
    }

    public async Task HandleAsync(UserCreatedEvent @event, CancellationToken cancellationToken)
    {
        await _emailService.SendWelcomeEmailAsync(@event.Email, @event.Name);
        _logger.LogInformation("Welcome email sent to {Email}", @event.Email);
    }
}

/// <summary>
/// 更新统计
/// </summary>
public class UpdateUserStatsHandler : IEventHandler<UserCreatedEvent>
{
    private readonly IStatsService _statsService;

    public UpdateUserStatsHandler(IStatsService statsService)
    {
        _statsService = statsService;
    }

    public async Task HandleAsync(UserCreatedEvent @event, CancellationToken cancellationToken)
    {
        await _statsService.IncrementUserCountAsync(cancellationToken);
    }
}
```

### 步骤 4: 定义服务

创建 `Services/UserRepository.cs`：

```csharp
using Catga;

namespace MyApp.Services;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<bool> ExistsByEmailAsync(string email, CancellationToken ct = default);
    Task SaveAsync(User user, CancellationToken ct = default);
}

/// <summary>
/// 使用 [CatgaService] 属性，Source Generator 自动注册！
/// </summary>
[CatgaService(ServiceLifetime.Singleton, ServiceType = typeof(IUserRepository))]
public class InMemoryUserRepository : IUserRepository
{
    private readonly Dictionary<string, User> _users = new();
    private readonly ILogger<InMemoryUserRepository> _logger;

    public InMemoryUserRepository(ILogger<InMemoryUserRepository> logger)
    {
        _logger = logger;
    }

    public Task<User?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        _users.TryGetValue(id, out var user);
        return Task.FromResult(user);
    }

    public Task<bool> ExistsByEmailAsync(string email, CancellationToken ct = default)
    {
        return Task.FromResult(_users.Values.Any(u => u.Email == email));
    }

    public Task SaveAsync(User user, CancellationToken ct = default)
    {
        _users[user.Id] = user;
        _logger.LogDebug("User saved: {UserId}", user.Id);
        return Task.CompletedTask;
    }
}

// 领域模型
public class User
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Email { get; init; }
    public required DateTime CreatedAt { get; init; }
}
```

### 步骤 5: 配置应用

编辑 `Program.cs`：

```csharp
using Catga;
using MyApp;
using MyApp.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. 配置 Catga
builder.Services
    .AddCatga()                      // 添加 Catga 核心服务
    .UseMemoryPack()                 // 使用 MemoryPack 序列化（AOT 兼容）
    .ForDevelopment();               // 开发模式（启用详细日志）

// 2. 添加传输层
builder.Services.AddInMemoryTransport();  // 使用内存传输（开发环境）

// 3. 自动注册 Handler 和服务（Source Generator 魔法！）
builder.Services.AddGeneratedHandlers();   // 自动发现所有 IRequestHandler, IEventHandler
builder.Services.AddGeneratedServices();   // 自动发现所有 [CatgaService]

// 4. 添加 Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// 5. 定义 API 端点
app.MapPost("/users", async (CreateUserCommand cmd, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<CreateUserCommand, UserCreatedResult>(cmd);

    return result.IsSuccess
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { error = result.Error });
})
.WithName("CreateUser")
.WithTags("Users");

app.MapGet("/users/{id}", async (string id, IUserRepository repository) =>
{
    var user = await repository.GetByIdAsync(id);
    return user != null ? Results.Ok(user) : Results.NotFound();
})
.WithName("GetUser")
.WithTags("Users");

app.Run();
```

### 步骤 6: 运行应用

```bash
dotnet run
```

访问 Swagger UI: http://localhost:5000/swagger

---

## 🧪 测试

### 使用 curl

```bash
# 创建用户
curl -X POST http://localhost:5000/users \
  -H "Content-Type: application/json" \
  -d '{"name":"张三","email":"zhangsan@example.com"}'

# 响应
{
  "userId": "a1b2c3d4e5f6...",
  "createdAt": "2024-10-16T12:00:00Z"
}

# 获取用户
curl http://localhost:5000/users/a1b2c3d4e5f6...
```

### 使用 Swagger UI

1. 打开 http://localhost:5000/swagger
2. 展开 `POST /users`
3. 点击 "Try it out"
4. 输入请求体：
   ```json
   {
     "name": "李四",
     "email": "lisi@example.com"
   }
   ```
5. 点击 "Execute"

---

## 🎯 关键概念

### 1. SafeRequestHandler

- **无需 try-catch** - 框架自动处理异常
- **直接抛出 `CatgaException`** - 自动转换为 `CatgaResult.Failure`
- **直接返回结果** - 无需包装为 `CatgaResult.Success`

### 2. 自动注册

- **`AddGeneratedHandlers()`** - 自动注册所有 Handler
- **`AddGeneratedServices()`** - 自动注册所有 `[CatgaService]`
- **零配置** - Source Generator 在编译时生成注册代码

### 3. 事件驱动

- **一个事件，多个 Handler** - 自动并行执行
- **解耦** - 事件发布者无需知道订阅者
- **可扩展** - 随时添加新的事件 Handler

---

## 🚀 下一步

### 添加自定义错误处理

```csharp
public class CreateUserHandler : SafeRequestHandler<CreateUserCommand, UserCreatedResult>
{
    protected override async Task<CatgaResult<UserCreatedResult>> OnBusinessErrorAsync(
        CreateUserCommand request,
        CatgaException exception,
        CancellationToken ct)
    {
        // 自定义错误处理
        Logger.LogWarning("User creation failed for email {Email}: {Error}",
            request.Email, exception.Message);

        var metadata = new ResultMetadata();
        metadata.Add("Email", request.Email);
        metadata.Add("ErrorType", "Validation");

        return new CatgaResult<UserCreatedResult>
        {
            IsSuccess = false,
            Error = $"Failed to create user: {exception.Message}",
            Metadata = metadata
        };
    }
}
```

### 添加调试器

```csharp
// Program.cs
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCatgaDebuggerWithAspNetCore();

    // ... 在 app 配置后
    app.MapCatgaDebugger("/debug");  // http://localhost:5000/debug
}
```

### 升级到分布式

```csharp
// 替换内存传输为 NATS
builder.Services.AddNatsTransport(options =>
{
    options.Url = "nats://localhost:4222";
});

// 添加 Redis 持久化
builder.Services.AddRedisStores(options =>
{
    options.Configuration = "localhost:6379";
});
```

---

## 📚 更多资源

- [完整文档](../docs/INDEX.md)
- [API 参考](./QUICK-REFERENCE.md)
- [OrderSystem 示例](../examples/OrderSystem.Api/)
- [性能基准](./PERFORMANCE-REPORT.md)

---

**恭喜！你已经创建了第一个 Catga 应用！** 🎉
