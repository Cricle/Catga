# CQRS 模式

Command Query Responsibility Segregation (命令查询职责分离) 是 Catga 的核心设计模式。

## 什么是 CQRS？

CQRS 将应用程序的**读操作**（查询）和**写操作**（命令）分离到不同的模型中。

### 传统方式 vs CQRS

**传统方式**（CRUD）:
```csharp
public interface IUserService
{
    User Get(int id);           // 读
    void Create(User user);     // 写
    void Update(User user);     // 写
    void Delete(int id);        // 写
}
```

**CQRS 方式**:
```csharp
// 读模型（Query）
public record GetUserQuery(int Id) : IQuery<UserDto>;

// 写模型（Command）
public record CreateUserCommand(string Name, string Email) : ICommand<int>;
public record UpdateUserCommand(int Id, string Name) : ICommand<Unit>;
public record DeleteUserCommand(int Id) : ICommand<Unit>;
```

## 为什么使用 CQRS？

### 优势

1. **关注点分离**
   - 读写逻辑独立
   - 更容易理解和维护

2. **独立优化**
   - 查询可以使用只读数据库副本
   - 命令可以使用写优化的数据库

3. **可扩展性**
   - 读写可以独立扩展
   - 根据负载模式调整资源

4. **安全性**
   - 明确的读写权限
   - 更容易实施访问控制

5. **可测试性**
   - 处理器单一职责
   - 独立单元测试

## Catga 中的 CQRS

### 消息类型

#### 1. Query（查询）

**特点**:
- 只读操作
- 不改变系统状态
- 返回数据
- 幂等性

**示例**:
```csharp
// 获取单个实体
public record GetUserQuery(int UserId) : IQuery<UserDto>;

// 获取列表
public record ListUsersQuery(int PageIndex, int PageSize) : IQuery<PagedList<UserDto>>;

// 复杂查询
public record SearchUsersQuery(
    string? Name,
    string? Email,
    bool? IsActive,
    DateTime? CreatedAfter) : IQuery<List<UserDto>>;
```

**处理器**:
```csharp
public class GetUserHandler : IRequestHandler<GetUserQuery, UserDto>
{
    private readonly IUserRepository _repository;
    private readonly IMapper _mapper;

    public async Task<TransitResult<UserDto>> HandleAsync(
        GetUserQuery request,
        CancellationToken cancellationToken = default)
    {
        var user = await _repository.GetByIdAsync(request.UserId);

        if (user == null)
            return TransitResult<UserDto>.Failure("User not found");

        var dto = _mapper.Map<UserDto>(user);
        return TransitResult<UserDto>.Success(dto);
    }
}
```

#### 2. Command（命令）

**特点**:
- 修改系统状态
- 有副作用
- 表达意图
- 可能触发事件

**示例**:
```csharp
// 创建
public record CreateUserCommand(
    string Name,
    string Email) : ICommand<int>; // 返回 UserId

// 更新
public record UpdateUserCommand(
    int UserId,
    string Name) : ICommand<Unit>; // 无返回值

// 删除
public record DeleteUserCommand(int UserId) : ICommand<Unit>;

// 业务操作
public record ActivateUserCommand(int UserId) : ICommand<Unit>;
public record DeactivateUserCommand(int UserId, string Reason) : ICommand<Unit>;
```

**处理器**:
```csharp
public class CreateUserHandler : IRequestHandler<CreateUserCommand, int>
{
    private readonly IUserRepository _repository;
    private readonly ITransitMediator _mediator;
    private readonly ILogger<CreateUserHandler> _logger;

    public async Task<TransitResult<int>> HandleAsync(
        CreateUserCommand request,
        CancellationToken cancellationToken = default)
    {
        // 1. 验证（或使用 ValidationBehavior）
        if (await _repository.EmailExistsAsync(request.Email))
        {
            return TransitResult<int>.Failure("Email already exists");
        }

        // 2. 创建领域实体
        var user = new User
        {
            Name = request.Name,
            Email = request.Email,
            CreatedAt = DateTime.UtcNow
        };

        // 3. 持久化
        var userId = await _repository.CreateAsync(user);

        // 4. 发布领域事件
        await _mediator.PublishAsync(
            new UserCreatedEvent(userId, user.Name, user.Email),
            cancellationToken);

        _logger.LogInformation("User created: {UserId}", userId);

        return TransitResult<int>.Success(userId);
    }
}
```

#### 3. Event（事件）

**特点**:
- 描述已经发生的事情
- 过去时命名
- 不可变
- 可能有多个订阅者

**示例**:
```csharp
// 领域事件
public record UserCreatedEvent(
    int UserId,
    string Name,
    string Email) : IEvent;

public record UserUpdatedEvent(
    int UserId,
    string OldName,
    string NewName) : IEvent;

public record UserDeletedEvent(
    int UserId,
    DateTime DeletedAt) : IEvent;
```

**处理器**:
```csharp
// 发送欢迎邮件
public class SendWelcomeEmailHandler : IEventHandler<UserCreatedEvent>
{
    private readonly IEmailService _emailService;

    public async Task HandleAsync(
        UserCreatedEvent @event,
        CancellationToken cancellationToken = default)
    {
        await _emailService.SendWelcomeEmailAsync(
            @event.Email,
            @event.Name);
    }
}

// 更新统计
public class UpdateUserStatisticsHandler : IEventHandler<UserCreatedEvent>
{
    private readonly IStatisticsService _statistics;

    public async Task HandleAsync(
        UserCreatedEvent @event,
        CancellationToken cancellationToken = default)
    {
        await _statistics.IncrementUserCountAsync();
    }
}

// 通知管理员
public class NotifyAdminHandler : IEventHandler<UserCreatedEvent>
{
    private readonly INotificationService _notifications;

    public async Task HandleAsync(
        UserCreatedEvent @event,
        CancellationToken cancellationToken = default)
    {
        await _notifications.NotifyAdminAsync(
            $"New user registered: {@event.Name}");
    }
}
```

## 最佳实践

### 1. 命名约定

```csharp
// ✅ 好的命名
public record GetUserQuery(...) : IQuery<UserDto>;
public record CreateUserCommand(...) : ICommand<int>;
public record UserCreatedEvent(...) : IEvent;

// ❌ 不好的命名
public record UserQuery(...);     // 不明确
public record User(...);           // 太通用
public record CreateUser(...);    // 不明确类型
```

### 2. 使用 record 类型

```csharp
// ✅ 使用 record（不可变）
public record CreateUserCommand(string Name, string Email) : ICommand<int>;

// ❌ 使用 class（可变）
public class CreateUserCommand : ICommand<int>
{
    public string Name { get; set; }  // 可变
    public string Email { get; set; }
}
```

### 3. 单一职责

```csharp
// ✅ 每个命令一个明确的职责
public record UpdateUserNameCommand(int UserId, string Name) : ICommand<Unit>;
public record UpdateUserEmailCommand(int UserId, string Email) : ICommand<Unit>;

// ❌ 一个命令做太多事情
public record UpdateUserCommand(
    int UserId,
    string? Name,
    string? Email,
    string? Phone,
    Address? Address) : ICommand<Unit>;
```

### 4. 返回值设计

```csharp
// ✅ 返回必要的数据
public record CreateUserCommand(...) : ICommand<int>;  // 返回 UserId
public record GetUserQuery(...) : IQuery<UserDto>;     // 返回 DTO

// ✅ 无需返回值时使用 Unit
public record DeleteUserCommand(int UserId) : ICommand<Unit>;

// ❌ 返回过多数据
public record CreateUserCommand(...) : ICommand<User>;  // 不要返回实体
```

### 5. DTO vs 实体

```csharp
// ✅ Query 返回 DTO
public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    // 只包含需要的字段
}

// ✅ Command 使用领域实体
public class CreateUserHandler
{
    public async Task<TransitResult<int>> HandleAsync(...)
    {
        var user = new User(...);  // 领域实体
        await _repository.SaveAsync(user);
        return TransitResult<int>.Success(user.Id);
    }
}
```

### 6. 验证

```csharp
// ✅ 使用 ValidationBehavior
public class CreateUserValidator : IValidator<CreateUserCommand>
{
    public Task<List<string>> ValidateAsync(
        CreateUserCommand command,
        CancellationToken ct)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(command.Name))
            errors.Add("Name is required");

        if (!IsValidEmail(command.Email))
            errors.Add("Invalid email format");

        return Task.FromResult(errors);
    }
}

// 注册
services.AddValidator<CreateUserCommand, CreateUserValidator>();
```

## 高级模式

### 1. 读写分离数据库

```csharp
public class GetUserHandler : IRequestHandler<GetUserQuery, UserDto>
{
    private readonly IReadOnlyRepository<User> _readRepo;  // 只读副本

    public async Task<TransitResult<UserDto>> HandleAsync(...)
    {
        var user = await _readRepo.GetByIdAsync(query.UserId);
        // ...
    }
}

public class CreateUserHandler : IRequestHandler<CreateUserCommand, int>
{
    private readonly IRepository<User> _writeRepo;  // 写数据库

    public async Task<TransitResult<int>> HandleAsync(...)
    {
        var user = new User(...);
        await _writeRepo.SaveAsync(user);
        // ...
    }
}
```

### 2. 事件溯源

```csharp
// 命令产生事件
public class CreateUserHandler
{
    public async Task<TransitResult<int>> HandleAsync(...)
    {
        // 1. 创建用户
        var user = new User(...);
        await _repository.SaveAsync(user);

        // 2. 发布事件序列
        await _mediator.PublishAsync(new UserCreatedEvent(...));
        await _mediator.PublishAsync(new UserProfileCreatedEvent(...));
        await _mediator.PublishAsync(new WelcomeEmailScheduledEvent(...));

        return TransitResult<int>.Success(user.Id);
    }
}

// 事件处理器重建状态
public class UserProjectionHandler : IEventHandler<UserCreatedEvent>
{
    public async Task HandleAsync(UserCreatedEvent @event, ...)
    {
        await _projection.UpdateAsync(@event.UserId, projection =>
        {
            projection.Name = @event.Name;
            projection.Email = @event.Email;
            projection.CreatedAt = @event.CreatedAt;
        });
    }
}
```

### 3. 最终一致性

```csharp
// 命令立即返回
public class PlaceOrderHandler : IRequestHandler<PlaceOrderCommand, OrderId>
{
    public async Task<TransitResult<OrderId>> HandleAsync(...)
    {
        var order = new Order(...) { Status = OrderStatus.Pending };
        await _repository.SaveAsync(order);

        // 异步处理
        await _mediator.PublishAsync(new OrderPlacedEvent(order.Id));

        // 立即返回
        return TransitResult<OrderId>.Success(order.Id);
    }
}

// 事件处理器异步处理
public class ProcessPaymentHandler : IEventHandler<OrderPlacedEvent>
{
    public async Task HandleAsync(OrderPlacedEvent @event, ...)
    {
        // 处理支付
        var result = await _paymentService.ProcessAsync(@event.OrderId);

        if (result.Success)
            await _mediator.PublishAsync(new PaymentSuccessEvent(...));
        else
            await _mediator.PublishAsync(new PaymentFailedEvent(...));
    }
}
```

## 性能考虑

### 1. 查询优化

```csharp
// ✅ 只查询需要的字段
public record GetUserListQuery() : IQuery<List<UserSummaryDto>>;

public class UserSummaryDto  // 轻量级 DTO
{
    public int Id { get; set; }
    public string Name { get; set; }
    // 不包含关联数据
}

// ✅ 使用分页
public record ListUsersQuery(int Page, int PageSize) : IQuery<PagedResult<UserDto>>;
```

### 2. 命令优化

```csharp
// ✅ 批量操作
public record CreateUsersCommand(List<UserData> Users) : ICommand<List<int>>;

// ✅ 异步处理
public class CreateUserHandler
{
    public async Task<TransitResult<int>> HandleAsync(...)
    {
        // 快速返回，异步处理耗时操作
        await _mediator.PublishAsync(new UserCreationRequestedEvent(...));
        return TransitResult<int>.Success(tempId);
    }
}
```

## 测试

### 单元测试

```csharp
[Fact]
public async Task CreateUser_ShouldReturnUserId()
{
    // Arrange
    var repository = new Mock<IUserRepository>();
    var mediator = new Mock<ITransitMediator>();
    var handler = new CreateUserHandler(repository.Object, mediator.Object);

    var command = new CreateUserCommand("John Doe", "john@example.com");

    repository
        .Setup(r => r.CreateAsync(It.IsAny<User>()))
        .ReturnsAsync(123);

    // Act
    var result = await handler.HandleAsync(command);

    // Assert
    Assert.True(result.IsSuccess);
    Assert.Equal(123, result.Value);

    mediator.Verify(m => m.PublishAsync(
        It.IsAny<UserCreatedEvent>(),
        It.IsAny<CancellationToken>()), Times.Once);
}
```

## 下一步

- 📖 学习 [Pipeline 行为](pipeline-behaviors.md)
- 🔄 探索 [事件驱动架构](../guides/events.md)
- 🌐 了解 [分布式事务](catga-transactions.md)
- 📊 查看 [完整示例](../examples/simple-cqrs.md)

---

**CQRS with Catga** - 简单、清晰、高性能 🚀

