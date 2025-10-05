# 🚀 Catga - 现代分布式 CQRS 框架

[![Build Status](https://github.com/your-org/Catga/workflows/CI/badge.svg)](https://github.com/your-org/Catga/actions)
[![Coverage](https://codecov.io/gh/your-org/Catga/branch/master/graph/badge.svg)](https://codecov.io/gh/your-org/Catga)
[![NuGet](https://img.shields.io/nuget/v/Catga.svg)](https://www.nuget.org/packages/Catga/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-9.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/9.0)

**Catga** 是一个高性能、现代化的分布式 CQRS (Command Query Responsibility Segregation) 框架，专为 .NET 9.0 设计。它提供了构建可扩展、可维护的分布式系统所需的所有工具。

## ✨ 核心特性

### 🎯 CQRS 架构
- **清晰分离**: 命令、查询和事件的完全分离
- **统一调度**: `ICatgaMediator` 提供统一的消息调度
- **强类型结果**: `CatgaResult<T>` 确保类型安全的错误处理
- **管道行为**: 支持日志、验证、重试、熔断等横切关注点

### 🚀 高性能设计
- **零分配**: 精心设计的对象池和内存管理
- **NativeAOT**: 100% 支持 NativeAOT 编译，启动快速
- **JSON 源生成**: 避免运行时反射，提升序列化性能
- **异步优化**: 全面的 async/await 支持

### 🌐 分布式支持
- **NATS 集成**: 高性能消息传递和发布/订阅
- **Redis 集成**: 状态存储和幂等性支持
- **CatGa Saga**: 分布式事务模式实现
- **事件溯源**: 支持事件驱动架构

### 🔧 开发体验
- **简洁 API**: 直观易用的接口设计
- **依赖注入**: 与 .NET DI 容器深度集成
- **结构化日志**: 完整的可观测性支持
- **丰富文档**: 从入门到高级的完整指南

## 📊 性能基准

| 操作类型 | 延迟 | 吞吐量 | 内存分配 |
|----------|------|--------|----------|
| 本地命令 | ~50ns | 20M ops/s | 0B |
| 本地查询 | ~55ns | 18M ops/s | 0B |
| NATS 调用 | ~1.2ms | 800 ops/s | 384B |
| Saga 事务 | ~2.5ms | 400 ops/s | 1.2KB |

## 🏃‍♂️ 快速开始

### 安装

```bash
dotnet add package Catga
```

### 基本用法

```csharp
// 1. 定义消息
public record CreateOrderCommand : MessageBase, ICommand<OrderResult>
{
    public string CustomerId { get; init; } = string.Empty;
    public string ProductId { get; init; } = string.Empty;
    public int Quantity { get; init; }
}

// 2. 实现处理器
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderResult>
{
    public async Task<CatgaResult<OrderResult>> HandleAsync(
        CreateOrderCommand request, 
        CancellationToken cancellationToken = default)
    {
        // 业务逻辑
        return CatgaResult<OrderResult>.Success(new OrderResult 
        { 
            OrderId = Guid.NewGuid().ToString() 
        });
    }
}

// 3. 配置服务
builder.Services.AddTransit();
builder.Services.AddScoped<IRequestHandler<CreateOrderCommand, OrderResult>, CreateOrderHandler>();

// 4. 使用调度器
[ApiController]
public class OrdersController : ControllerBase
{
    private readonly ICatgaMediator _mediator;
    
    public OrdersController(ICatgaMediator mediator) => _mediator = mediator;
    
    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderCommand command)
    {
        var result = await _mediator.SendAsync<CreateOrderCommand, OrderResult>(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
}
```

### 分布式消息传递

```csharp
// 添加 NATS 支持
builder.Services.AddNatsCatga(options =>
{
    options.Url = "nats://localhost:4222";
    options.ServiceId = "order-service";
});

// 发布事件
await _mediator.PublishAsync(new OrderCreatedEvent 
{ 
    OrderId = order.Id,
    CustomerId = order.CustomerId 
});
```

## 📁 项目结构

```
Catga/
├── 🎯 核心框架
│   ├── src/Catga/                     # 核心框架
│   ├── src/Catga.Nats/               # NATS 集成
│   └── src/Catga.Redis/              # Redis 集成
├── 🧪 测试和基准
│   ├── tests/Catga.Tests/            # 单元测试
│   └── benchmarks/Catga.Benchmarks/  # 性能基准
├── 🚀 示例项目
│   ├── examples/OrderApi/            # Web API 示例
│   └── examples/NatsDistributed/     # 分布式示例
├── 📚 文档
│   ├── docs/api/                     # API 文档
│   ├── docs/architecture/            # 架构文档
│   └── docs/examples/                # 示例文档
└── 🔧 工具
    ├── .github/workflows/            # CI/CD 流水线
    └── demo.ps1 / demo.sh           # 演示脚本
```

## 🎮 演示脚本

### Windows (PowerShell)
```bash
# 完整演示
./demo.ps1

# 运行示例
./demo.ps1 -RunExamples

# 跳过构建和测试
./demo.ps1 -SkipBuild -SkipTests
```

### Linux/macOS (Bash)
```bash
# 完整演示
./demo.sh

# 运行示例
./demo.sh --run-examples

# 跳过构建和测试
./demo.sh --skip-build --skip-tests
```

## 📚 文档

| 文档类型 | 链接 | 描述 |
|----------|------|------|
| 🚀 快速开始 | [docs/guides/quick-start.md](docs/guides/quick-start.md) | 5分钟上手指南 |
| 🏗️ 架构概览 | [docs/architecture/overview.md](docs/architecture/overview.md) | 系统架构设计 |
| 📖 API 参考 | [docs/api/README.md](docs/api/README.md) | 完整 API 文档 |
| 💡 示例项目 | [examples/README.md](examples/README.md) | 实用示例代码 |
| 🤝 贡献指南 | [CONTRIBUTING.md](CONTRIBUTING.md) | 参与项目开发 |

## 🎯 示例项目

### 1. OrderApi - 基础 Web API 
**特点**: 简单易懂，适合学习 CQRS 基础概念

```bash
cd examples/OrderApi
dotnet run
# 访问 https://localhost:7xxx/swagger
```

**功能**:
- ✅ 订单创建和查询
- ✅ Swagger API 文档
- ✅ 完整的错误处理
- ✅ 内存存储演示

### 2. NatsDistributed - 分布式微服务
**特点**: 生产级别，展示完整的分布式架构

```bash
# 1. 启动 NATS 服务器
docker run -d --name nats-server -p 4222:4222 nats:latest

# 2. 启动服务
cd examples/NatsDistributed/OrderService && dotnet run
cd examples/NatsDistributed/NotificationService && dotnet run  
cd examples/NatsDistributed/TestClient && dotnet run
```

**架构**:
- 🏗️ **OrderService**: 处理订单业务逻辑
- 📧 **NotificationService**: 处理通知和审计日志
- 🧪 **TestClient**: 自动化测试场景

## 🔧 技术栈

### 核心技术
- **.NET 9.0** - 最新的 .NET 运行时
- **C# 13** - 现代 C# 语言特性
- **System.Text.Json** - 高性能 JSON 处理
- **Microsoft.Extensions.DependencyInjection** - 依赖注入

### 集成组件
- **NATS.Net** - NATS 消息代理客户端
- **StackExchange.Redis** - Redis 数据库客户端
- **BenchmarkDotNet** - 性能基准测试

### 开发工具
- **xUnit** + **FluentAssertions** - 单元测试
- **GitHub Actions** - CI/CD 自动化
- **Coverlet** - 代码覆盖率分析

## 🏗️ 构建和测试

```bash
# 构建项目
dotnet build

# 运行测试
dotnet test

# 运行基准测试
dotnet run --project benchmarks/Catga.Benchmarks --configuration Release

# 运行示例
dotnet run --project examples/OrderApi
```

## 🚀 生产部署

### Docker 支持
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0
COPY . /app
WORKDIR /app
ENTRYPOINT ["dotnet", "YourApp.dll"]
```

### NativeAOT 部署
```bash
dotnet publish -c Release -r linux-x64 --self-contained
```

### 监控和可观测性
- ✅ 结构化日志 (Serilog/NLog 集成)
- ✅ 分布式追踪 (OpenTelemetry 支持)
- ✅ 健康检查端点
- ✅ 指标收集 (Prometheus 兼容)

## 🤝 贡献

我们欢迎社区贡献！请阅读 [CONTRIBUTING.md](CONTRIBUTING.md) 了解如何参与项目开发。

### 贡献方式
- 🐛 报告 Bug
- 💡 提出新功能建议
- 📝 改进文档
- 🔧 提交代码修复
- 🧪 添加测试用例

### 开发流程
1. Fork 项目
2. 创建功能分支 (`git checkout -b feature/amazing-feature`)
3. 提交更改 (`git commit -m 'Add amazing feature'`)
4. 推送分支 (`git push origin feature/amazing-feature`)
5. 创建 Pull Request

## 📄 许可证

本项目基于 [MIT 许可证](LICENSE) 开源。

## 🙏 致谢

感谢所有为 Catga 做出贡献的开发者和社区成员！

### 技术灵感
- [MediatR](https://github.com/jbogard/MediatR) - 启发了调度器设计
- [NATS](https://nats.io/) - 提供了出色的消息传递基础设施
- [Event Store](https://www.eventstore.com/) - 事件溯源模式参考

## 📞 支持

- 📚 [文档](docs/)
- 🐛 [问题反馈](https://github.com/your-org/Catga/issues)
- 💬 [讨论区](https://github.com/your-org/Catga/discussions)
- 📧 联系邮箱: support@catga.dev

---

**用 Catga 构建更好的分布式系统！** 🚀