# 🎯 Catga - 最佳实践指南

生产级Catga应用的最佳实践集合。

---

## 📐 设计原则

### 1. CQRS分离

**命令 (Command)**: 改变状态

```csharp
// ✅ 命令: 修改数据
public record CreateUserCommand : IRequest<CreateUserResponse>
{
    public string UserName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
}
```

**查询 (Query)**: 只读数据

```csharp
// ✅ 查询: 只读
public record GetUserQuery : IRequest<UserDto>
{
    public string UserId { get; init; } = string.Empty;
}
```

**规则**:
- ❌ Command中不要查询数据返回
- ❌ Query中不要修改状态
- ✅ 职责清晰分离

---

### 2. 使用Record类型

```csharp
// ✅ 推荐 - Record (不可变)
public record CreateUserCommand : IRequest<CreateUserResponse>
{
    public string UserName { get; init; } = string.Empty;
}

// ❌ 不推荐 - Class (可变)
public class CreateUserCommand : IRequest<CreateUserResponse>
{
    public string UserName { get; set; } = string.Empty; // 可变!
}
```

**优势**:
- 线程安全 (不可变)
- 值语义 (==比较内容)
- 简洁语法

---

### 3. 事件驱动

**发布事件**:

```csharp
public class CreateUserCommandHandler
    : IRequestHandler<CreateUserCommand, CreateUserResponse>
{
    private readonly ICatgaMediator _mediator;

    public async Task<CatgaResult<CreateUserResponse>> HandleAsync(
        CreateUserCommand request,
        CancellationToken cancellationToken)
    {
        // 1. 业务逻辑
        var userId = await _userService.CreateAsync(request.UserName);

        // 2. 发布事件
        await _mediator.PublishAsync(new UserCreatedEvent
        {
            UserId = userId,
            UserName = request.UserName
        }, cancellationToken);

        return CatgaResult<CreateUserResponse>.Success(...);
    }
}
```

**处理事件**:

```csharp
// 可以有多个事件处理器!
public class SendWelcomeEmailHandler : IEventHandler<UserCreatedEvent>
{
    public async Task HandleAsync(
        UserCreatedEvent @event,
        CancellationToken cancellationToken)
    {
        await _emailService.SendWelcomeEmailAsync(@event.Email);
    }
}

public class AuditLogHandler : IEventHandler<UserCreatedEvent>
{
    public async Task HandleAsync(
        UserCreatedEvent @event,
        CancellationToken cancellationToken)
    {
        await _auditService.LogAsync($"User created: {@event.UserId}");
    }
}
```

---

## 🏗️ 项目结构

### 推荐结构

```
MyApp/
├── Commands/                    # 命令
│   ├── CreateUserCommand.cs
│   └── UpdateUserCommand.cs
├── Queries/                     # 查询
│   ├── GetUserQuery.cs
│   └── ListUsersQuery.cs
├── Events/                      # 事件
│   ├── UserCreatedEvent.cs
│   └── UserUpdatedEvent.cs
├── Handlers/                    # 处理器
│   ├── Commands/
│   │   ├── CreateUserCommandHandler.cs
│   │   └── UpdateUserCommandHandler.cs
│   ├── Queries/
│   │   ├── GetUserQueryHandler.cs
│   │   └── ListUsersQueryHandler.cs
│   └── Events/
│       ├── UserCreatedEventHandler.cs
│       └── SendWelcomeEmailHandler.cs
├── Models/                      # 领域模型
│   ├── User.cs
│   └── UserDto.cs
├── Behaviors/                   # 自定义行为
│   └── CustomLoggingBehavior.cs
└── Program.cs
```

---

## ✅ Handler最佳实践

### 1. 单一职责

```csharp
// ✅ 好 - 职责单一
public class CreateUserCommandHandler : IRequestHandler<...>
{
    public async Task<CatgaResult<CreateUserResponse>> HandleAsync(...)
    {
        // 只负责创建用户
        var user = await _userService.CreateAsync(...);
        return CatgaResult<CreateUserResponse>.Success(...);
    }
}

// ❌ 差 - 职责过多
public class CreateUserCommandHandler : IRequestHandler<...>
{
    public async Task<CatgaResult<CreateUserResponse>> HandleAsync(...)
    {
        var user = await _userService.CreateAsync(...);
        await _emailService.SendEmailAsync(...); // 不应该在这里!
        await _auditService.LogAsync(...); // 不应该在这里!
        return ...;
    }
}
```

**正确做法**: 发布事件，让事件处理器处理

---

### 2. 依赖注入

```csharp
// ✅ 好 - 构造函数注入
public class CreateUserCommandHandler : IRequestHandler<...>
{
    private readonly IUserService _userService;
    private readonly ILogger<CreateUserCommandHandler> _logger;

    public CreateUserCommandHandler(
        IUserService userService,
        ILogger<CreateUserCommandHandler> logger)
    {
        _userService = userService;
        _logger = logger;
    }
}

// ❌ 差 - 直接new
public class CreateUserCommandHandler : IRequestHandler<...>
{
    public async Task<CatgaResult<...>> HandleAsync(...)
    {
        var service = new UserService(); // 不可测试!
    }
}
```

---

### 3. 异常处理

```csharp
// ✅ 好 - 返回CatgaResult
public async Task<CatgaResult<CreateUserResponse>> HandleAsync(...)
{
    try
    {
        var user = await _userService.CreateAsync(...);
        return CatgaResult<CreateUserResponse>.Success(user);
    }
    catch (UserAlreadyExistsException ex)
    {
        return CatgaResult<CreateUserResponse>.Failure(
            "User already exists", ex);
    }
}

// ❌ 差 - 直接抛异常
public async Task<CatgaResult<CreateUserResponse>> HandleAsync(...)
{
    var user = await _userService.CreateAsync(...);
    if (user == null)
        throw new Exception("Failed"); // 不优雅!
    return ...;
}
```

---

### 4. 使用CancellationToken

```csharp
// ✅ 好 - 传递CancellationToken
public async Task<CatgaResult<UserDto>> HandleAsync(
    GetUserQuery request,
    CancellationToken cancellationToken)
{
    var user = await _repository.GetByIdAsync(
        request.UserId, cancellationToken); // ← 传递
    return CatgaResult<UserDto>.Success(user);
}

// ❌ 差 - 忽略CancellationToken
public async Task<CatgaResult<UserDto>> HandleAsync(
    GetUserQuery request,
    CancellationToken cancellationToken)
{
    var user = await _repository.GetByIdAsync(request.UserId); // 未传递
    return ...;
}
```

---

## 🔧 Behavior最佳实践

### 1. 自定义Logging

```csharp
public class CustomLoggingBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger _logger;

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        _logger.LogInformation("Handling {Request}", requestName);

        var sw = Stopwatch.StartNew();
        var result = await next();
        sw.Stop();

        _logger.LogInformation("Handled {Request} in {Elapsed}ms",
            requestName, sw.ElapsedMilliseconds);

        return result;
    }
}
```

### 2. 性能监控

```csharp
public class PerformanceMonitoringBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var result = await next();
        sw.Stop();

        if (sw.ElapsedMilliseconds > 1000) // >1s
        {
            _logger.LogWarning("Slow request: {Request} took {Elapsed}ms",
                typeof(TRequest).Name, sw.ElapsedMilliseconds);
        }

        return result;
    }
}
```

---

## 🌐 分布式最佳实践

### 1. Outbox模式

**使用场景**: 确保消息至少发送一次

```csharp
// 配置
builder.Services
    .AddRedisPersistence(...)
    .AddOutbox(options =>
    {
        options.PollingInterval = TimeSpan.FromSeconds(5);
        options.BatchSize = 100;
    });

// Handler中使用
public class CreateOrderCommandHandler : IRequestHandler<...>
{
    private readonly IOutboxStore _outboxStore;

    public async Task<CatgaResult<...>> HandleAsync(...)
    {
        // 1. 业务逻辑 + 保存到Outbox (同一事务)
        using var transaction = await _dbContext.BeginTransactionAsync();

        var order = await _dbContext.Orders.AddAsync(...);
        await _outboxStore.AddAsync(new OrderCreatedEvent { ... });

        await transaction.CommitAsync();

        // 2. 后台服务会自动发送
        return ...;
    }
}
```

### 2. Inbox模式

**使用场景**: 确保消息恰好处理一次

```csharp
// 配置
builder.Services
    .AddRedisPersistence(...)
    .AddInbox(options =>
    {
        options.RetentionPeriod = TimeSpan.FromDays(7);
    });

// 自动去重
```

### 3. 幂等性

```csharp
// 使用MessageId确保幂等
public async Task<CatgaResult<...>> HandleAsync(
    CreateOrderCommand request,
    CancellationToken cancellationToken)
{
    // 检查是否已处理
    var existing = await _repository.GetByMessageIdAsync(request.MessageId);
    if (existing != null)
        return CatgaResult<...>.Success(existing); // 返回已有结果

    // 处理新请求
    var order = await _repository.CreateAsync(...);
    return CatgaResult<...>.Success(order);
}
```

---

## 🧪 测试最佳实践

### 1. Handler单元测试

```csharp
public class CreateUserCommandHandlerTests
{
    [Fact]
    public async Task CreateUser_ValidRequest_ReturnsSuccess()
    {
        // Arrange
        var mockService = new Mock<IUserService>();
        mockService.Setup(x => x.CreateAsync(It.IsAny<string>()))
            .ReturnsAsync("user123");

        var handler = new CreateUserCommandHandler(
            mockService.Object,
            Mock.Of<ILogger<CreateUserCommandHandler>>());

        var command = new CreateUserCommand { UserName = "test" };

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal("user123", result.Data.UserId);
    }
}
```

### 2. 集成测试

```csharp
public class UserFlowIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public UserFlowIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateUser_EndToEnd_Success()
    {
        // Arrange
        var client = _factory.CreateClient();
        var command = new CreateUserCommand { UserName = "test" };

        // Act
        var response = await client.PostAsJsonAsync("/users", command);

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CreateUserResponse>();
        Assert.NotNull(result);
    }
}
```

---

## 🔒 安全最佳实践

### 1. 验证输入

```csharp
public class CreateUserValidator : IValidator<CreateUserCommand>
{
    public Task<IEnumerable<string>> ValidateAsync(
        CreateUserCommand request,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        // 必填校验
        if (string.IsNullOrWhiteSpace(request.UserName))
            errors.Add("UserName is required");

        // 格式校验
        if (!IsValidEmail(request.Email))
            errors.Add("Invalid email format");

        // 长度校验
        if (request.UserName.Length > 50)
            errors.Add("UserName too long");

        return Task.FromResult<IEnumerable<string>>(errors);
    }
}
```

### 2. 授权检查

```csharp
public class AuthorizationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ICurrentUserService _currentUser;

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // 检查权限
        if (!await _currentUser.HasPermissionAsync(typeof(TRequest).Name))
        {
            return CatgaResult<TResponse>.Failure("Unauthorized");
        }

        return await next();
    }
}
```

---

## 📊 监控最佳实践

### 1. 结构化日志

```csharp
_logger.LogInformation(
    "User created: {UserId}, UserName: {UserName}, Email: {Email}",
    userId, userName, email);

// 输出:
// {"UserId": "123", "UserName": "john", "Email": "john@example.com"}
```

### 2. 关键指标

```csharp
// 记录业务指标
CatgaMetrics.RecordRequestStart("CreateUserCommand");
CatgaMetrics.RecordRequestSuccess("CreateUserCommand", durationMs);
CatgaMetrics.RecordRequestFailure("CreateUserCommand", errorType, durationMs);
```

---

## ⚡ 性能最佳实践

### 1. 批量操作

```csharp
// ✅ 好 - 批量处理
var commands = users.Select(u => new CreateUserCommand { ... }).ToList();
await _batchTransport.SendBatchAsync(commands, batchSize: 100);

// ❌ 差 - 逐个处理
foreach (var user in users)
{
    await _mediator.SendAsync(new CreateUserCommand { ... });
}
```

### 2. 避免N+1查询

```csharp
// ✅ 好 - 批量加载
var userIds = orders.Select(o => o.UserId).Distinct().ToList();
var users = await _repository.GetByIdsAsync(userIds);
var userDict = users.ToDictionary(u => u.Id);

foreach (var order in orders)
{
    order.User = userDict[order.UserId];
}

// ❌ 差 - N+1查询
foreach (var order in orders)
{
    order.User = await _repository.GetByIdAsync(order.UserId);
}
```

---

## 📚 文档最佳实践

### 1. XML文档注释

```csharp
/// <summary>
/// Creates a new user with the specified username and email.
/// </summary>
/// <param name="request">The create user command containing username and email.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>
/// A CatgaResult containing the created user response if successful,
/// or an error message if the user already exists.
/// </returns>
public async Task<CatgaResult<CreateUserResponse>> HandleAsync(
    CreateUserCommand request,
    CancellationToken cancellationToken)
{
    // ...
}
```

### 2. README示例

每个Command/Query应有README示例:

```markdown
# CreateUserCommand

Creates a new user in the system.

## Usage

```csharp
var command = new CreateUserCommand
{
    UserName = "john",
    Email = "john@example.com"
};

var result = await _mediator.SendAsync(command);
```

## Validation

- UserName: Required, 1-50 characters
- Email: Required, valid email format
```

---

## ✅ 检查清单

### 开发阶段

- [ ] 使用Record类型
- [ ] CQRS职责分离
- [ ] Handler单一职责
- [ ] 正确使用CancellationToken
- [ ] 添加验证逻辑
- [ ] 发布领域事件
- [ ] 编写单元测试

### 部署前

- [ ] 集成测试通过
- [ ] 性能测试达标
- [ ] 安全审计完成
- [ ] 日志完整
- [ ] 监控配置
- [ ] 文档齐全

---

**Catga - 最佳实践成就卓越！** 🎯

