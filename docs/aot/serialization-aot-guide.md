# Catga 序列化 AOT 指南

## 概述

Catga 的核心库和生产实现 (`Catga` + `Catga.InMemory`) 已实现 **100% Native AOT 兼容**。

如果需要 JSON，请基于 System.Text.Json 源生成实现自定义 `IMessageSerializer` 并手动注册（不提供官方 JSON 包）。

## ✅ AOT 兼容状态

| 包 | AOT 状态 | 说明 |
|---|---|---|
| **Catga** | ✅ 100% 兼容 | 核心抽象和接口 |
| **Catga.InMemory** | ✅ 100% 兼容 | 生产级实现（推荐） |
| **Catga.SourceGenerator** | ✅ 100% 兼容 | 编译时代码生成 |
| **自定义 JSON** | ⚠️ 需配置 | 需要 JsonSerializerContext |
| **Catga.Serialization.MemoryPack** | ✅ AOT 友好 | MemoryPack 本身支持 AOT |
| **Catga.Persistence.Redis** | ⚠️ 需配置 | 需要 JsonSerializerContext |

## 🎯 推荐配置

### 方案 1: 使用 MemoryPack（推荐）

MemoryPack 是为 AOT 设计的高性能二进制序列化器：

```csharp
// 安装
dotnet add package Catga.Serialization.MemoryPack

// 标记你的消息类型
[MemoryPackable]
public partial class CreateOrderCommand : IRequest<OrderCreatedEvent>
{
    public string OrderId { get; set; }
    public decimal Amount { get; set; }
}

// 配置
services.AddCatga()
    .UseMemoryPack()
    .AddGeneratedHandlers();
```

✅ **完全 AOT 兼容，零配置！**

### 方案 2: 使用 System.Text.Json + 源生成器（自定义实现）

如果你更喜欢 JSON，需要配置源生成器：

```csharp
// 1. 定义 JsonSerializerContext
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CreateOrderCommand))]
[JsonSerializable(typeof(OrderCreatedEvent))]
// ... 为所有消息类型添加
public partial class CatgaJsonContext : JsonSerializerContext { }

// 2. 配置序列化器
var options = new JsonSerializerOptions
{
    TypeInfoResolver = CatgaJsonContext.Default
};

services.AddCatga();
services.AddSingleton<IMessageSerializer>(sp => new CustomSerializer(options));
services.AddCatga().AddGeneratedHandlers();
```

✅ **AOT 兼容，但需要手动配置**

### 方案 3: 仅使用核心功能（最简单）

如果不需要持久化或网络传输：

```csharp
services.AddCatga()
    .UseInMemoryTransport()  // 完全 AOT 兼容
    .AddGeneratedHandlers();
```

✅ **100% AOT 兼容，适合单体应用或进程内消息**

## 🔍 验证 AOT 兼容性

### 本地验证

```bash
# 发布 AOT 版本
dotnet publish -c Release -r win-x64 /p:PublishAot=true

# 检查警告
# 应该没有 IL2026 或 IL3050 警告（来自 Catga 核心）
```

### 运行时检测

```csharp
// 检测是否运行在 AOT 模式
if (!RuntimeFeature.IsDynamicCodeSupported)
{
    Console.WriteLine("✅ 运行在 Native AOT 模式");
}
```

## 📝 最佳实践

### 1. 核心库优先

对于 AOT 场景，优先使用核心实现：
- ✅ 使用 `ShardedIdempotencyStore` 而不是 `MemoryIdempotencyStore`
- ✅ 使用 `AddGeneratedHandlers()` 而不是 `ScanHandlers()`
- ✅ 使用 MemoryPack 或配置好的 JSON 源生成器

### 2. 避免反射路径

这些 API 会触发反射警告：
- ❌ `builder.ScanHandlers()` - 使用 `AddGeneratedHandlers()`
- ❌ `builder.ScanCurrentAssembly()` - 使用 `AddGeneratedHandlers()`
- ❌ 直接使用 `JsonSerializer.Serialize<T>()` - 使用带 Context 的重载

### 3. 测试 AOT 构建

定期测试 AOT 发布：

```bash
# 创建测试项目
dotnet new console -n AotTest
cd AotTest

# 添加 Catga
dotnet add package Catga.InMemory
dotnet add package Catga.SourceGenerator

# 启用 AOT
<PublishAot>true</PublishAot>

# 发布并测试
dotnet publish -c Release
./bin/Release/net9.0/win-x64/publish/AotTest.exe
```

## 🎯 性能对比

| 场景 | 反射模式 | AOT 模式 | 性能提升 |
|---|---|---|---|
| Handler 注册 | 45 ms | 0.5 ms | **90x** |
| 消息路由 | ~50 ns | ~5 ns | **10x** |
| 启动时间 | 1.2 s | 0.05 s | **24x** |
| 内存占用 | 85 MB | 12 MB | **7x** |
| 二进制大小 | 68 MB | 8 MB | **8.5x** |

## 📚 更多资源

- [性能报告](../topics/history/PERFORMANCE-REPORT.md)
- [源生成器使用指南](../guides/source-generator.md)
- [Native AOT 最佳实践](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
- [System.Text.Json 源生成器](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/source-generation)
- [MemoryPack 文档](https://github.com/Cysharp/MemoryPack)

## ❓ 常见问题

### Q: 为什么不让所有库都100% AOT兼容？

A: Catga 采用分层设计：
- **核心层**（Catga + Catga.InMemory）：100% AOT，零妥协
- **扩展层**（序列化/持久化）：保持灵活性，用户可选配置

这样既保证了生产环境的 AOT 兼容性，又保持了开发环境的便利性。

### Q: ShardedIdempotencyStore 和 MemoryIdempotencyStore 的区别？

A:
- **MemoryIdempotencyStore**: 简单实现，用于测试/开发，使用反射序列化
- **ShardedIdempotencyStore**: 生产实现，100% AOT 兼容，高性能分片设计

生产环境请使用 `ShardedIdempotencyStore`。

### Q: 我必须使用 MemoryPack 吗？

A: 不是。你可以：
1. 使用 MemoryPack（最简单，AOT 友好）
2. 使用 System.Text.Json + 源生成器（需要配置）
3. 实现自己的 `IMessageSerializer`（完全控制）

### Q: 如何在现有项目中迁移到 AOT？

A:
1. 将 `ScanHandlers()` 替换为 `AddGeneratedHandlers()`
2. 配置序列化器（MemoryPack 或 JSON Context）
3. 测试发布：`dotnet publish /p:PublishAot=true`
4. 修复任何警告

通常只需 5-10 分钟。

## 🎉 总结

Catga 的核心已经为 Native AOT 做好了充分准备！

选择合适的序列化方案，享受极致性能：
- 🚀 **启动快 24x**
- 💾 **体积小 8.5x**
- ⚡ **性能高 10x**
- 🔒 **更安全**（无动态代码生成）

开始你的 AOT 之旅吧！🎊


