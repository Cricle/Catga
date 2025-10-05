# 示例项目

Catga 提供了多个示例项目来展示不同的使用场景和最佳实践。

## 📁 项目列表

### 1. OrderApi - 基础 Web API 示例

**路径**: `examples/OrderApi/`

一个简单的订单管理 API，展示：
- ✅ CQRS 模式基础用法
- ✅ 依赖注入配置
- ✅ 错误处理
- ✅ Swagger 文档

**快速开始**:
```bash
cd examples/OrderApi
dotnet run
# 访问 https://localhost:7xxx/swagger
```

**主要功能**:
- 创建订单 (`POST /api/orders`)
- 查询订单 (`GET /api/orders/{id}`)
- 预置产品数据
- 内存存储（演示用）

**学习要点**:
- 如何定义命令和查询
- 如何实现处理器
- 如何配置 Catga
- 如何处理结果

### 2. 更多示例 (计划中)

#### EventDrivenApi - 事件驱动示例
- 事件发布和订阅
- 多个事件处理器
- 事件溯源模式

#### DistributedSaga - 分布式事务示例
- CatGa Saga 使用
- 补偿机制
- 分布式状态管理

#### NatsIntegration - NATS 集成示例
- 跨服务通信
- 发布/订阅模式
- 负载均衡

#### RedisCache - Redis 集成示例
- 幂等性存储
- Saga 状态持久化
- 缓存模式

## 🚀 运行示例

### 前置条件

- .NET 9.0 SDK
- (可选) Docker - 用于运行 NATS、Redis 等中间件

### 运行所有示例

```bash
# 构建所有示例
dotnet build

# 运行特定示例
dotnet run --project examples/OrderApi
```

### Docker Compose 支持 (计划中)

```bash
# 启动所有服务和中间件
docker-compose up

# 运行示例
dotnet run --project examples/OrderApi
dotnet run --project examples/NatsIntegration
```

## 📖 学习路径

### 初学者

1. **开始**: [OrderApi](OrderApi/) - 学习基础概念
2. **进阶**: EventDrivenApi - 理解事件驱动
3. **实践**: 修改示例，添加新功能

### 中级用户

1. **分布式**: DistributedSaga - 学习分布式事务
2. **集成**: NatsIntegration - 学习跨服务通信
3. **优化**: RedisCache - 学习性能优化

### 高级用户

1. **扩展**: 创建自定义 Pipeline Behavior
2. **集成**: 集成其他消息中间件
3. **监控**: 添加监控和可观测性

## 🛠️ 自定义示例

### 创建新示例

```bash
# 创建新的示例项目
dotnet new webapi -n MyExample -o examples/MyExample
cd examples/MyExample

# 添加 Catga 引用
dotnet add reference ../../src/Catga/Catga.csproj

# 将项目添加到解决方案
dotnet sln ../../Catga.sln add MyExample.csproj
```

### 示例模板

```csharp
// Program.cs
using Catga.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddCatga();

// 注册你的处理器
builder.Services.AddScoped<IRequestHandler<MyCommand, MyResult>, MyHandler>();

var app = builder.Build();

app.MapControllers();
app.Run();

// Commands/MyCommand.cs
public record MyCommand : MessageBase, ICommand<MyResult>
{
    public string Data { get; init; } = string.Empty;
}

public record MyResult
{
    public string ProcessedData { get; init; } = string.Empty;
}

// Handlers/MyHandler.cs
public class MyHandler : IRequestHandler<MyCommand, MyResult>
{
    public async Task<CatgaResult<MyResult>> HandleAsync(
        MyCommand request,
        CancellationToken cancellationToken = default)
    {
        // 你的业务逻辑
        return CatgaResult<MyResult>.Success(new MyResult
        {
            ProcessedData = $"Processed: {request.Data}"
        });
    }
}
```

## 📋 示例对比

| 示例 | 难度 | 特性 | 适用场景 |
|------|------|------|----------|
| OrderApi | 初级 | CQRS 基础 | 学习入门 |
| EventDrivenApi | 中级 | 事件驱动 | 事件系统 |
| DistributedSaga | 高级 | 分布式事务 | 复杂业务流程 |
| NatsIntegration | 中级 | 消息传递 | 微服务通信 |
| RedisCache | 中级 | 状态管理 | 性能优化 |

## 🔧 开发工具

### 推荐 IDE

- **Visual Studio 2022** - 完整的 .NET 开发环境
- **JetBrains Rider** - 跨平台 .NET IDE
- **VS Code** - 轻量级编辑器 + C# 扩展

### 有用的扩展

- **REST Client** - 测试 API
- **Docker** - 容器管理
- **GitLens** - Git 增强

### 调试技巧

```csharp
// 使用条件断点
if (request.MessageId == "specific-id")
{
    System.Diagnostics.Debugger.Break();
}

// 使用结构化日志
_logger.LogDebug("Processing {RequestType} with data: {@RequestData}",
    nameof(MyCommand), request);
```

## 📚 相关资源

- [Catga 文档](../README.md)
- [API 参考](../api/README.md)
- [架构文档](../architecture/overview.md)
- [贡献指南](../../CONTRIBUTING.md)

## 💡 提示和技巧

### 性能优化

1. **使用 AOT 编译**:
   ```bash
   dotnet publish -c Release -r win-x64 --self-contained
   ```

2. **启用 JSON 源生成**:
   ```csharp
   [JsonSerializable(typeof(MyCommand))]
   partial class MyJsonContext : JsonSerializerContext { }
   ```

3. **配置日志级别**:
   ```json
   {
     "Logging": {
       "LogLevel": {
         "Catga": "Information"
       }
     }
   }
   ```

### 测试建议

```csharp
[Fact]
public async Task CreateOrder_ShouldReturnSuccess()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddCatga();
    services.AddScoped<IRequestHandler<CreateOrderCommand, OrderResult>, CreateOrderHandler>();

    var provider = services.BuildServiceProvider();
    var mediator = provider.GetRequiredService<ICatgaMediator>();

    var command = new CreateOrderCommand
    {
        CustomerId = "TEST-001",
        ProductId = "PROD-001",
        Quantity = 1
    };

    // Act
    var result = await mediator.SendAsync<CreateOrderCommand, OrderResult>(command);

    // Assert
    result.IsSuccess.Should().BeTrue();
    result.Value.OrderId.Should().NotBeEmpty();
}
```

## 🐛 常见问题

### Q: 示例运行失败？
A: 检查 .NET 版本、依赖包是否正确安装

### Q: 找不到处理器？
A: 确保处理器已注册到 DI 容器

### Q: JSON 序列化失败？
A: 检查是否使用了 JSON 源生成器或配置了正确的选项

### Q: 性能不佳？
A: 考虑启用 AOT、使用对象池、优化序列化

---

通过这些示例，你可以快速学习 Catga 的各种特性，并在实际项目中应用这些模式和最佳实践。
