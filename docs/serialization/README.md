# 🔧 Catga 序列化器抽象

Catga 提供了灵活的序列化器抽象，支持多种序列化格式，并且完全兼容 AOT。

---

## 📦 可用的序列化器

### 1️⃣ JSON 序列化器（推荐）
- **包名**: `Catga.Serialization.Json`
- **基于**: `System.Text.Json`
- **优点**: .NET 原生支持，AOT 友好，无额外依赖
- **适用场景**: 通用场景、跨语言互操作

```bash
dotnet add package Catga.Serialization.Json
```

### 2️⃣ MemoryPack 序列化器（高性能）
- **包名**: `Catga.Serialization.MemoryPack`
- **基于**: `MemoryPack`
- **优点**: 极高性能，零分配，二进制格式
- **适用场景**: 高性能场景、.NET 内部通信

```bash
dotnet add package Catga.Serialization.MemoryPack
```

---

## 🚀 使用方式

### JSON 序列化器

```csharp
using Catga.Serialization;
using Catga.Serialization.Json;

// 注册序列化器
builder.Services.AddSingleton<IMessageSerializer, JsonMessageSerializer>();

// 使用 NATS + JSON
builder.Services.AddNatsDistributed("nats://localhost:4222", opt =>
{
    opt.EnableOutbox = true;
    opt.EnableInbox = true;
    opt.EnableIdempotency = true;
});
```

### MemoryPack 序列化器

```csharp
using Catga.Serialization;
using Catga.Serialization.MemoryPack;

// 注册序列化器
builder.Services.AddSingleton<IMessageSerializer, MemoryPackMessageSerializer>();

// 使用 NATS + MemoryPack
builder.Services.AddNatsDistributed("nats://localhost:4222", opt =>
{
    opt.EnableOutbox = true;
    opt.EnableInbox = true;
    opt.EnableIdempotency = true;
});
```

---

## 🔄 NATS vs Redis 存储对比

| 特性 | NATS (内存) | Redis (持久化) |
|------|------------|---------------|
| **持久化** | ❌ 内存存储 | ✅ 持久化存储 |
| **性能** | ⚡ 极高 | ⚡ 高 |
| **分布式锁** | ❌ 不支持 | ✅ 支持 |
| **集群** | ✅ 原生支持 | ✅ 支持 |
| **适用场景** | 临时数据、高吞吐 | 生产环境、持久化需求 |

---

## 📋 完整配置示例

### NATS + JSON（开发环境）

```csharp
using Catga.Serialization;
using Catga.Serialization.Json;

var builder = WebApplication.CreateBuilder(args);

// 注册 JSON 序列化器
builder.Services.AddSingleton<IMessageSerializer, JsonMessageSerializer>();

// 配置 NATS 分布式支持
builder.Services.AddNatsDistributed("nats://localhost:4222", opt =>
{
    opt.EnableOutbox = true;
    opt.EnableInbox = true;
    opt.EnableIdempotency = true;
    opt.EnableLogging = true;
    opt.EnableTracing = true;
});

var app = builder.Build();
app.Run();
```

### Redis + MemoryPack（生产环境）

```csharp
using Catga.Serialization;
using Catga.Serialization.MemoryPack;

var builder = WebApplication.CreateBuilder(args);

// 注册 MemoryPack 序列化器（高性能）
builder.Services.AddSingleton<IMessageSerializer, MemoryPackMessageSerializer>();

// 配置 Redis 分布式支持
builder.Services.AddRedisDistributed("localhost:6379", opt =>
{
    opt.EnableOutbox = true;
    opt.EnableInbox = true;
    opt.EnableIdempotency = true;
    opt.EnableDistributedLock = true; // Redis 支持分布式锁
});

var app = builder.Build();
app.Run();
```

---

## 🔑 自定义序列化器

你可以实现自己的序列化器：

```csharp
using Catga.Serialization;

public class MyCustomSerializer : IMessageSerializer
{
    public string Name => "MyCustom";

    public byte[] Serialize<T>(T value)
    {
        // 你的序列化逻辑
        throw new NotImplementedException();
    }

    public T? Deserialize<T>(byte[] data)
    {
        // 你的反序列化逻辑
        throw new NotImplementedException();
    }
}

// 注册
builder.Services.AddSingleton<IMessageSerializer, MyCustomSerializer>();
```

---

## 📊 性能对比

| 序列化器 | 序列化速度 | 反序列化速度 | 数据大小 | AOT 兼容 |
|---------|----------|------------|---------|---------|
| **JSON** | ⭐⭐⭐ | ⭐⭐⭐ | 较大 | ✅ 完全 |
| **MemoryPack** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | 最小 | ✅ 完全 |

---

## ⚠️ 注意事项

1. **序列化器一致性**: 集群中所有节点必须使用相同的序列化器
2. **NATS 存储限制**: NATS 存储为内存实现，重启后数据丢失
3. **生产环境推荐**: Redis + MemoryPack 组合性能最佳
4. **开发环境推荐**: NATS + JSON 组合配置最简单

---

## 🔗 相关链接

- [NATS 分布式配置](../distributed/nats-setup.md)
- [Redis 分布式配置](../distributed/redis-setup.md)
- [性能优化指南](../performance/README.md)

