# Catga

**高性能、AOT 兼容的 CQRS 和分布式事务框架**

Catga 是一个现代化的分布式应用框架，提供 CQRS、事件驱动架构和分布式事务（Saga）支持。

## ✨ 核心特性

- ✅ **100% AOT 兼容** - 零反射，完全 NativeAOT 支持
- ✅ **无锁设计** - 原子操作 + ConcurrentDictionary
- ✅ **非阻塞异步** - 全异步，零阻塞
- ✅ **极简 API** - 最少配置，合理默认值
- ✅ **高性能** - 分片存储、并发控制、限流
- ✅ **可观测性** - 分布式追踪、日志、指标
- ✅ **弹性设计** - 熔断器、重试、死信队列
- ✅ **多传输支持** - 内存 / NATS / Redis

## 📦 项目结构

```
Catga/
├── src/
│   ├── Catga/              # 核心库 (CQRS + CatGa事务)
│   ├── Catga.Nats/         # NATS 传输扩展
│   └── Catga.Redis/        # Redis 持久化扩展
└── benchmarks/
    └── Catga.Benchmarks/   # 性能基准测试
```

## 🚀 快速开始

### 1. 安装

```bash
# 核心包
dotnet add package Catga

# NATS 支持
dotnet add package Catga.Nats

# Redis 持久化
dotnet add package Catga.Redis
```

### 2. 基础使用

```csharp
// 定义消息
public record GetUserQuery(long UserId) : IQuery<User>;
public record CreateUserCommand(string Name) : ICommand<long>;
public record UserCreatedEvent(long UserId) : IEvent;

// 定义处理器
public class GetUserHandler : IRequestHandler<GetUserQuery, User>
{
    public async Task<CatgaResult<User>> HandleAsync(
        GetUserQuery request,
        CancellationToken ct)
    {
        var user = await _db.GetUserAsync(request.UserId);
        return CatgaResult<User>.Success(user);
    }
}

// 注册服务
services.AddCatga();
services.AddRequestHandler<GetUserQuery, User, GetUserHandler>();

// 使用
public class UserService(ICatgaMediator mediator)
{
    public async Task<User> GetUserAsync(long id)
    {
        var result = await mediator.SendAsync<GetUserQuery, User>(
            new GetUserQuery(id));
        return result.Value;
    }
}
```

## 📚 文档

详细文档请查看各子项目的 README：

- [Catga 核心库](src/Catga/README.md)
- [Catga.Nats](src/Catga.Nats/README.md)
- [Catga.Redis](src/Catga.Redis/README.md)
- [性能基准测试](benchmarks/Catga.Benchmarks/README.md)

## 🎯 配置预设

```csharp
// 开发环境（所有日志，无限流）
services.AddCatga(opt => opt.ForDevelopment());

// 高性能（5000 并发，64 分片）
services.AddCatga(opt => opt.WithHighPerformance());

// 完整弹性（熔断器 + 限流）
services.AddCatga(opt => opt.WithResilience());

// 最小化（零开销，最快）
services.AddCatga(opt => opt.Minimal());
```

## 📈 性能指标

| 传输 | 延迟 | 吞吐量 | 并发 |
|------|------|--------|------|
| Memory | < 1ms | 100K+ msg/s | 5000+ |
| NATS | < 5ms | 50K+ msg/s | 5000+ |

## 🛠️ 开发

### 构建项目

```bash
dotnet build
```

### 运行测试

```bash
dotnet test
```

### 运行性能测试

```bash
cd benchmarks/Catga.Benchmarks
dotnet run -c Release
```

## 🏗️ 技术栈

- .NET 9.0
- C# 12
- NATS 2.5+
- Redis (StackExchange.Redis)
- Polly 8.5+

## 🤝 贡献

欢迎贡献代码、报告问题或提出建议！

## 📄 许可证

MIT License

---

**Catga** - 让分布式应用开发更简单 🚀

