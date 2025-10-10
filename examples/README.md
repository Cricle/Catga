# Catga 示例项目

## 📚 示例列表

### 1. SimpleWebApi - 基础 CQRS 示例

**位置**: `examples/SimpleWebApi/`

**特点**:
- ✨ 最简单的 Catga 使用示例
- 📝 Command/Query 分离
- 🎯 源生成器自动注册
- 💡 适合快速入门

**代码行数**: ~60 行

**运行**:
```bash
cd examples/SimpleWebApi
dotnet run
```

[查看详细文档](SimpleWebApi/README.md)

---

### 2. RedisExample - Redis 分布式锁和缓存

**位置**: `examples/RedisExample/`

**特点**:
- 🔐 Redis 分布式锁 - 防止并发问题
- 📦 Redis 分布式缓存 - 提升查询性能
- ✨ 源生成器自动注册
- 🚀 生产级示例

**代码行数**: ~120 行

**前置条件**:
```bash
docker run -d -p 6379:6379 redis:latest
```

**运行**:
```bash
cd examples/RedisExample
dotnet run
```

[查看详细文档](RedisExample/README.md)

---

### 3. DistributedCluster - NATS 分布式集群

**位置**: `examples/DistributedCluster/`

**特点**:
- 🚀 NATS 高性能消息传输
- 📡 跨节点负载均衡
- 📢 事件广播（所有节点接收）
- ✨ 源生成器自动注册

**代码行数**: ~80 行

**前置条件**:
```bash
docker run -d -p 4222:4222 nats:latest
```

**运行多个节点**:
```bash
# 节点 1
cd examples/DistributedCluster
dotnet run --urls "https://localhost:5001"

# 节点 2（新终端）
dotnet run --urls "https://localhost:5002"

# 节点 3（新终端）
dotnet run --urls "https://localhost:5003"
```

[查看详细文档](DistributedCluster/README.md)

---

## 🎯 选择指南

| 场景 | 推荐示例 | 说明 |
|------|---------|------|
| **快速入门** | SimpleWebApi | 最简单，理解核心概念 |
| **单体应用** | SimpleWebApi | 无需外部依赖 |
| **需要分布式锁** | RedisExample | 防止并发问题 |
| **需要缓存** | RedisExample | 提升查询性能 |
| **微服务集群** | DistributedCluster | 跨节点通信 |
| **高可用部署** | DistributedCluster | 负载均衡 + 事件广播 |

---

## 🚀 快速开始

### 1. 安装依赖

```bash
# 核心库
dotnet add package Catga
dotnet add package Catga.InMemory

# 源生成器
dotnet add package Catga.SourceGenerator

# Redis（可选）
dotnet add package Catga.Persistence.Redis

# NATS（可选）
dotnet add package Catga.Transport.Nats
```

### 2. 最小代码示例

```csharp
using Catga;
using Catga.DependencyInjection;
using Catga.Handlers;
using Catga.Messages;
using Catga.Results;

var builder = WebApplication.CreateBuilder(args);

// ✨ Catga - 只需 2 行
builder.Services.AddCatga();
builder.Services.AddGeneratedHandlers();

var app = builder.Build();

// API
app.MapPost("/hello", async (ICatgaMediator mediator, HelloCommand cmd) =>
    await mediator.SendAsync<HelloCommand, string>(cmd) is var result && result.IsSuccess
        ? Results.Ok(result.Value)
        : Results.BadRequest(result.Error));

app.Run();

// 消息
public record HelloCommand(string Name) : MessageBase, IRequest<string>;

// Handler（自动注册）
public class HelloHandler : IRequestHandler<HelloCommand, string>
{
    public Task<CatgaResult<string>> HandleAsync(HelloCommand cmd, CancellationToken ct = default)
    {
        return Task.FromResult(CatgaResult<string>.Success($"Hello, {cmd.Name}!"));
    }
}
```

**就这么简单！** 🎉

---

## 📊 示例对比

| 特性 | SimpleWebApi | RedisExample | DistributedCluster |
|------|-------------|--------------|-------------------|
| 代码行数 | ~60 | ~120 | ~80 |
| 外部依赖 | 无 | Redis | NATS |
| 分布式锁 | ❌ | ✅ | ❌ |
| 分布式缓存 | ❌ | ✅ | ❌ |
| 跨节点通信 | ❌ | ❌ | ✅ |
| 负载均衡 | ❌ | ❌ | ✅ |
| 事件广播 | ❌ | ❌ | ✅ |
| 适合场景 | 入门学习 | 单体应用 | 微服务集群 |

---

## 🎓 学习路径

1. **第一步**: 运行 `SimpleWebApi`，理解 CQRS 基础概念
2. **第二步**: 运行 `RedisExample`，学习分布式锁和缓存
3. **第三步**: 运行 `DistributedCluster`，体验微服务集群

---

## 📚 相关文档

- [Catga 快速开始](../QUICK_START.md)
- [架构说明](../ARCHITECTURE.md)
- [源生成器文档](../src/Catga.SourceGenerator/README.md)
- [性能基准测试](../benchmarks/README.md)

---

**Catga - 让 CQRS 变得简单！** ✨
