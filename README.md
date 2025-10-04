# Catga

[![CI](https://github.com/YOUR_USERNAME/Catga/workflows/CI/badge.svg)](https://github.com/YOUR_USERNAME/Catga/actions)
[![Code Coverage](https://github.com/YOUR_USERNAME/Catga/workflows/Code%20Coverage/badge.svg)](https://github.com/YOUR_USERNAME/Catga/actions)
[![NuGet](https://img.shields.io/nuget/v/Catga.svg)](https://www.nuget.org/packages/Catga/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

**Catga** 是一个高性能、100% AOT 兼容的 .NET 分布式框架，基于 CQRS (Command Query Responsibility Segregation) 和 Saga 模式。

## ✨ 特性

- 🚀 **高性能**: 零分配的消息处理管道
- 📦 **100% AOT 兼容**: 完全支持 NativeAOT 编译
- 🔄 **CQRS 模式**: 清晰的命令/查询分离
- 🔀 **分布式 Saga**: 基于 CatGa 的分布式事务协调
- 🛡️ **弹性机制**: 内置重试、熔断器、限流
- 🔁 **幂等性**: 自动消息去重处理
- 📨 **多种传输**: 支持 NATS、Redis 等
- 🎯 **类型安全**: 完全的编译时类型检查
- 📝 **中央包管理**: 统一的依赖版本管理

## 📦 安装

### 核心包

```bash
dotnet add package Catga
```

### NATS 扩展

```bash
dotnet add package Catga.Nats
```

### Redis 扩展

```bash
dotnet add package Catga.Redis
```

## 🚀 快速开始

### 1. 定义消息

```csharp
using Catga.Messages;

// 命令
public record CreateOrderCommand : MessageBase, ICommand<OrderResult>
{
    public string ProductId { get; init; } = string.Empty;
    public int Quantity { get; init; }
}

// 查询
public record GetOrderQuery : MessageBase, IQuery<OrderDto>
{
    public string OrderId { get; init; } = string.Empty;
}

// 事件
public record OrderCreatedEvent : EventBase
{
    public string OrderId { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
}
```

### 2. 实现处理器

```csharp
using Catga.Handlers;
using Catga.Results;

public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderResult>
{
    public async Task<CatgaResult<OrderResult>> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        // 业务逻辑
        var order = new Order
        {
            ProductId = request.ProductId,
            Quantity = request.Quantity
        };

        await SaveOrderAsync(order, cancellationToken);

        return CatgaResult<OrderResult>.Success(
            new OrderResult { OrderId = order.Id }
        );
    }
}
```

### 3. 配置服务

```csharp
using Catga.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// 注册 Catga
builder.Services.AddTransit();

// 注册处理器
builder.Services.AddScoped<IRequestHandler<CreateOrderCommand, OrderResult>, CreateOrderHandler>();

var app = builder.Build();
```

### 4. 发送消息

```csharp
using Catga;

public class OrderController : ControllerBase
{
    private readonly ICatgaMediator _mediator;

    public OrderController(ICatgaMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("orders")]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderCommand command)
    {
        var result = await _mediator.SendAsync<CreateOrderCommand, OrderResult>(command);

        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return BadRequest(result.Error);
    }
}
```

## 🔧 高级特性

### Pipeline Behaviors

```csharp
// 自动日志记录
builder.Services.AddTransit(options =>
{
    options.AddLogging();
    options.AddTracing();
    options.AddIdempotency();
    options.AddValidation();
    options.AddRetry(maxAttempts: 3);
});
```

### 分布式 Saga (CatGa)

```csharp
using Catga.CatGa.Core;

public class OrderSaga : ICatGaTransaction
{
    public async Task ExecuteAsync(CatGaContext context)
    {
        // 创建订单
        var order = await CreateOrderAsync(context);
        context.SetCompensation(() => DeleteOrderAsync(order.Id));

        // 扣减库存
        await ReduceInventoryAsync(order.ProductId, order.Quantity);
        context.SetCompensation(() => RestoreInventoryAsync(order.ProductId, order.Quantity));

        // 支付
        await ProcessPaymentAsync(order.TotalAmount);
        context.SetCompensation(() => RefundPaymentAsync(order.PaymentId));
    }
}
```

### NATS 集成

```csharp
using Catga.Nats.DependencyInjection;

builder.Services.AddNatsTransit(options =>
{
    options.Url = "nats://localhost:4222";
    options.MaxReconnect = 10;
});
```

### Redis 集成

```csharp
using Catga.Redis.DependencyInjection;

builder.Services.AddRedisTransit(options =>
{
    options.Configuration = "localhost:6379";
    options.IdempotencyExpiration = TimeSpan.FromHours(24);
});
```

## 📊 性能

基准测试（在 AMD Ryzen 9 5900X 上运行）:

| 操作 | 平均时间 | 吞吐量 | 分配 |
|------|----------|--------|------|
| 本地命令 | ~50 ns | 20M ops/s | 0 B |
| 本地查询 | ~55 ns | 18M ops/s | 0 B |
| NATS 远程调用 | ~1.2 ms | 800 ops/s | 384 B |
| Saga 事务 | ~2.5 ms | 400 ops/s | 1.2 KB |

运行基准测试:

```bash
dotnet run -c Release --project benchmarks/Catga.Benchmarks
```

## 🏗️ 项目结构

```
Catga/
├── src/
│   ├── Catga/              # 核心库
│   ├── Catga.Nats/         # NATS 传输
│   └── Catga.Redis/        # Redis 扩展
├── tests/
│   └── Catga.Tests/        # 单元测试
├── benchmarks/
│   └── Catga.Benchmarks/   # 性能测试
├── docs/                   # 文档
└── README.md
```

## 📚 文档

- [快速开始](docs/guides/quick-start.md)
- [架构概览](docs/architecture/overview.md)
- [CQRS 模式](docs/architecture/cqrs.md)
- [CatGa Saga](docs/architecture/saga.md)
- [API 参考](docs/api/)

## 🧪 测试

运行单元测试:

```bash
dotnet test
```

查看测试覆盖率:

```bash
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
```

## 🤝 贡献

欢迎贡献！请查看 [贡献指南](CONTRIBUTING.md)。

1. Fork 项目
2. 创建特性分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 打开 Pull Request

## 🔄 CI/CD

项目使用 GitHub Actions 进行持续集成:

- ✅ 自动构建和测试
- 📊 代码覆盖率报告
- 🚀 自动发布到 NuGet
- 🔍 代码质量检查

## 📝 更新日志

查看 [CHANGELOG.md](CHANGELOG.md) 了解详细的版本历史。

### 最新版本 (v1.0.0)

- ✨ 初始版本发布
- 🚀 100% AOT 兼容
- 📦 CQRS 核心功能
- 🔄 分布式 Saga (CatGa)
- 📨 NATS 和 Redis 集成

## 📄 许可证

本项目采用 MIT 许可证 - 查看 [LICENSE](LICENSE) 文件了解详情。

## 🙏 致谢

- [MediatR](https://github.com/jbogard/MediatR) - CQRS 设计灵感
- [NATS](https://nats.io/) - 高性能消息传输
- [StackExchange.Redis](https://github.com/StackExchange/StackExchange.Redis) - Redis 客户端

## 📧 联系方式

- 问题反馈: [GitHub Issues](https://github.com/YOUR_USERNAME/Catga/issues)
- 讨论交流: [GitHub Discussions](https://github.com/YOUR_USERNAME/Catga/discussions)

---

⭐ 如果这个项目对你有帮助，请给一个 Star!
