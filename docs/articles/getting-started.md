# 🚀 Getting Started with Catga

欢迎使用 Catga！这个 5 分钟的快速指南将带你从零开始构建第一个高性能 CQRS 应用。

<div align="center">

**纳秒级延迟 · 百万QPS · 零反射 · 源生成 · 生产就绪**

</div>

---

## 📋 前置要求

- ✅ [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) 或更高版本
- ✅ IDE: Visual Studio 2022+ / VS Code / Rider
- ✅ 基础 C# 和 ASP.NET Core 知识

---

## 🎯 第一步: 创建项目

### 1.1 创建 Web API 项目

```bash
# 创建新项目
dotnet new webapi -n MyFirstCatgaApp
cd MyFirstCatgaApp

# 删除默认的 WeatherForecast (不需要)
rm WeatherForecast.cs Controllers/WeatherForecastController.cs
```

### 1.2 安装 Catga 包

```bash
# 核心包 (必需)
dotnet add package Catga

# 传输层 (选择一个)
dotnet add package Catga.Transport.InMemory  # 推荐: 开发和单体应用

# 可选: ASP.NET Core 集成
dotnet add package Catga.AspNetCore
```

---

## 📦 第二步: 配置 Catga

打开 `Program.cs`，配置 Catga：

```csharp
using Catga;
using Catga.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ⭐ 添加 Catga 服务 (一行代码，自动注册所有 Handler)
builder.Services.AddCatga();

// 可选: 添加内存传输 (开发环境)
builder.Services.AddInMemoryTransport();

// 可选: 添加 ASP.NET Core 端点
builder.Services.AddCatgaEndpoints();

// 添加 Controllers (用于 REST API)
builder.Services.AddControllers();

// 添加 Swagger (可选，推荐)
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

// 可选: 映射 Catga 诊断端点
app.MapCatgaDiagnostics(); // 访问 /catga/health, /catga/metrics

app.Run();
```

**就这么简单！** Catga 会自动发现和注册所有的 Handler。

---

## 💬 第三步: 定义消息

创建 `Messages/` 文件夹，定义你的消息：

### Commands (命令)

```csharp
// Messages/CreateUserCommand.cs
using Catga.Abstractions;

namespace MyFirstCatgaApp.Messages;

/// <summary>
/// 创建用户命令
/// MessageId 会自动生成 (由源生成器)
/// </summary>
public record CreateUserCommand(string Name, string Email) : IRequest<User>;

/// <summary>
/// 用户数据
/// </summary>
public record User(int Id, string Name, string Email);
```

### Events (事件)

```csharp
// Messages/UserCreatedEvent.cs
using Catga.Abstractions;

namespace MyFirstCatgaApp.Messages;

/// <summary>
/// 用户创建事件
/// 可以有多个 Handler 订阅
/// </summary>
public record UserCreatedEvent(int UserId, string Name, string Email) : IEvent;
```

### Queries (查询)

```csharp
// Messages/GetUserQuery.cs
using Catga.Abstractions;

namespace MyFirstCatgaApp.Messages;

/// <summary>
/// 获取用户查询
/// </summary>
public record GetUserQuery(int UserId) : IRequest<User?>;
```

---

## 🎯 第四步: 实现 Handler

创建 `Handlers/` 文件夹，实现业务逻辑：

### Command Handler

```csharp
// Handlers/CreateUserHandler.cs
using Catga.Abstractions;
using Catga.Core;
using MyFirstCatgaApp.Messages;

namespace MyFirstCatgaApp.Handlers;

/// <summary>
/// 创建用户 Handler
/// 会被自动注册 (源生成器)
/// </summary>
public class CreateUserHandler : IRequestHandler<CreateUserCommand, User>
{
    // 模拟数据库
    private static readonly List<User> _users = new();
    private static int _nextId = 1;

    public async Task<CatgaResult<User>> HandleAsync(
        CreateUserCommand request,
        CancellationToken cancellationToken = default)
    {
        // 1️⃣ 验证
        if (string.IsNullOrWhiteSpace(request.Name))
            return CatgaResult<User>.Failure("Name cannot be empty");

        if (string.IsNullOrWhiteSpace(request.Email))
            return CatgaResult<User>.Failure("Email cannot be empty");

        if (_users.Any(u => u.Email == request.Email))
            return CatgaResult<User>.Failure("Email already exists");

        // 2️⃣ 创建用户
        var user = new User(_nextId++, request.Name, request.Email);
        _users.Add(user);

        // 3️⃣ 返回成功结果
        return CatgaResult<User>.Success(user);

        // ✅ 自动追踪、自动指标、自动错误处理！
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
/// 获取用户 Handler
/// </summary>
public class GetUserHandler : IRequestHandler<GetUserQuery, User?>
{
    // 使用与 CreateUserHandler 相同的数据源
    private static readonly List<User> _users = CreateUserHandler._users;

    public async Task<CatgaResult<User?>> HandleAsync(
        GetUserQuery request,
        CancellationToken cancellationToken = default)
    {
        var user = _users.FirstOrDefault(u => u.Id == request.UserId);
        return CatgaResult<User?>.Success(user);
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
/// 用户创建事件 Handler
/// 可以有多个 Event Handler 订阅同一个事件
/// </summary>
public class UserCreatedEventHandler : IEventHandler<UserCreatedEvent>
{
    private readonly ILogger<UserCreatedEventHandler> _logger;

    public UserCreatedEventHandler(ILogger<UserCreatedEventHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(
        UserCreatedEvent @event,
        CancellationToken cancellationToken = default)
    {
        // 发送欢迎邮件、记录审计日志、更新统计等
        _logger.LogInformation(
            "User created: {UserId} - {Name} ({Email})",
            @event.UserId, @event.Name, @event.Email
        );

        // 这里可以做任何事情:
        // - 发送邮件
        // - 更新缓存
        // - 发送到消息队列
        // - 调用其他服务
        await Task.CompletedTask;
    }
}
```

---

## 🌐 第五步: 创建 API 控制器

创建 `Controllers/` 文件夹：

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
    /// 创建用户
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
            // 可选: 发布事件
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
    /// 获取用户
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
/// 创建用户请求 DTO
/// </summary>
public record CreateUserRequest(string Name, string Email);
```

---

## 🎉 第六步: 运行和测试

### 6.1 启动应用

```bash
dotnet run
```

输出:
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:7001
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
```

### 6.2 打开 Swagger

浏览器访问: `https://localhost:7001/swagger`

### 6.3 测试 API

#### 创建用户

```bash
curl -X POST https://localhost:7001/api/users \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Alice",
    "email": "alice@example.com"
  }'
```

响应:
```json
{
  "id": 1,
  "name": "Alice",
  "email": "alice@example.com"
}
```

#### 获取用户

```bash
curl https://localhost:7001/api/users/1
```

响应:
```json
{
  "id": 1,
  "name": "Alice",
  "email": "alice@example.com"
}
```

#### 查看日志

检查控制台，你会看到事件处理日志:
```
info: MyFirstCatgaApp.Handlers.UserCreatedEventHandler[0]
      User created: 1 - Alice (alice@example.com)
```

---

## 📊 性能验证

让我们验证一下 Catga 的性能！

### 安装 BenchmarkDotNet

```bash
dotnet add package BenchmarkDotNet
```

### 创建 Benchmark

```csharp
// Benchmarks/CatgaBenchmark.cs
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Catga.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using MyFirstCatgaApp.Messages;

namespace MyFirstCatgaApp.Benchmarks;

[MemoryDiagnoser]
public class CatgaBenchmark
{
    private ICatgaMediator _mediator = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddCatga();
        var provider = services.BuildServiceProvider();
        _mediator = provider.GetRequiredService<ICatgaMediator>();
    }

    [Benchmark]
    public async Task<CatgaResult<User>> CreateUserCommand()
    {
        return await _mediator.SendAsync(new CreateUserCommand("Test", "test@example.com"));
    }

    [Benchmark]
    public async Task<CatgaResult<User?>> GetUserQuery()
    {
        return await _mediator.SendAsync(new GetUserQuery(1));
    }
}

// Program.cs 添加
// BenchmarkRunner.Run<CatgaBenchmark>();
```

### 运行 Benchmark

```bash
dotnet run -c Release --project YourProject.csproj
```

预期结果:
```
| Method           | Mean     | Allocated |
|----------------- |---------:|----------:|
| CreateUserCommand| 462 ns   | 432 B     |
| GetUserQuery     | 446 ns   | 368 B     |
```

**🎉 恭喜！你已经达到纳秒级性能！**

---

## 🚀 下一步

### 扩展功能

1. **添加持久化**
   ```bash
   dotnet add package Catga.Persistence.Redis
   ```
   ```csharp
   builder.Services.AddRedisPersistence("localhost:6379");
   ```

2. **添加分布式消息**
   ```bash
   dotnet add package Catga.Transport.Nats
   ```
   ```csharp
   builder.Services.AddNatsTransport("nats://localhost:4222");
   ```

3. **添加序列化**
   ```bash
   dotnet add package Catga.Serialization.MemoryPack
   ```
   ```csharp
   builder.Services.AddMemoryPackSerializer();
   ```

4. **添加测试**
   ```bash
   dotnet add package Catga.Testing
   dotnet add package xunit
   dotnet add package FluentAssertions
   ```

### 学习资源

| 资源 | 说明 | 预计时间 |
|------|------|---------|
| [配置指南](./configuration.md) | 详细配置选项 | 30 min |
| [架构概览](../architecture/overview.md) | 理解框架设计 | 30 min |
| [错误处理](../guides/error-handling.md) | 异常处理和回滚 | 20 min |
| [性能优化](../guides/memory-optimization-guide.md) | 零分配技巧 | 1 hour |
| [分布式部署](../deployment/kubernetes.md) | K8s 部署 | 2 hours |
| [OrderSystem 示例](../../examples/OrderSystem.Api/README.md) | 完整电商系统 | 2 hours |

---

## 💡 常见问题

<details>
<summary>Q: Handler 为什么会自动注册？</summary>

A: Catga 使用源生成器在编译时扫描所有实现了 `IRequestHandler` 或 `IEventHandler` 的类，并自动生成注册代码。无需手动注册！

</details>

<details>
<summary>Q: MessageId 是如何生成的？</summary>

A: 源生成器会为所有实现 `IMessage` 的消息自动生成 `MessageId` 属性，使用 Snowflake 算法保证唯一性和有序性。

</details>

<details>
<summary>Q: 如何在测试中使用 Catga？</summary>

A: 使用 `Catga.Testing` 包：

```csharp
var fixture = new CatgaTestFixture();
fixture.RegisterRequestHandler<CreateUserCommand, User, CreateUserHandler>();

var result = await fixture.Mediator.SendAsync(new CreateUserCommand("Test", "test@example.com"));
result.Should().BeSuccessful();
```

详见 [测试文档](../../src/Catga.Testing/README.md)

</details>

<details>
<summary>Q: 如何处理业务异常？</summary>

A: 使用 `CatgaResult<T>`:

```csharp
// 成功
return CatgaResult<User>.Success(user);

// 失败
return CatgaResult<User>.Failure("User not found");

// 异常会被自动捕获和记录
```

详见 [错误处理指南](../guides/error-handling.md)

</details>

---

## 🎯 完整示例

查看完整的生产级别示例:

- **OrderSystem**: [examples/OrderSystem.Api](../../examples/OrderSystem.Api/README.md)
  - 完整的电商订单系统
  - 分布式部署 (3 节点集群)
  - 监控和追踪
  - 性能测试

---

## 📞 获取帮助

- 💬 [GitHub 讨论区](https://github.com/Cricle/Catga/discussions)
- 🐛 [问题追踪](https://github.com/Cricle/Catga/issues)
- 📚 [完整文档](../README.md)
- ⭐ [GitHub](https://github.com/Cricle/Catga)

---

<div align="center">

**恭喜！你已经掌握了 Catga 的基础！** 🎉

现在开始构建你的高性能 CQRS 应用吧！

[查看完整文档](../README.md) · [查看示例](../examples/basic-usage.md) · [性能基准](../BENCHMARK-RESULTS.md)

</div>
