# Catga 序列化 AOT 指南

## 概述

Catga 当前最稳的 AOT 路线是：

- `AddCatga()`
- `UseMemoryPack()`
- 再按场景接 `UseInMemory() / UseRedis(...) / UseNats(...)`

如果需要 JSON，请基于 System.Text.Json 源生成实现自定义 `IMessageSerializer` 并手动注册（不提供官方 JSON 包）。

## ✅ AOT 兼容状态

| 包 | AOT 状态 | 说明 |
|---|---|---|
| **Catga** | ✅ 100% 兼容 | 核心抽象和接口 |
| **Catga.Transport.InMemory** | ✅ AOT 友好 | 开发/测试起步路径 |
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
    .UseMemoryPack();
```

✅ **完全 AOT 兼容，配置路径最简单**

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
```

✅ **AOT 兼容，但需要手动配置**

### 方案 3: 仅使用核心功能（最简单）

如果不需要持久化或网络传输：

```csharp
services.AddCatga()
    .UseMemoryPack()
    .UseInMemory();

services.AddInMemoryTransport();
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

对于 AOT 场景，优先使用当前推荐实现：
- ✅ 使用 `UseMemoryPack()`
- ✅ 使用 `UseInMemory()` 先跑通最小 AOT 路线
- ✅ 再切到 `UseRedis(...)` / `UseNats(...)`

### 2. 避免反射路径

这些 API 会触发反射警告：
- ❌ 运行时反射扫描
- ❌ 直接使用 `JsonSerializer.Serialize<T>()` - 使用带 Context 的重载

### 3. 测试 AOT 构建

定期测试 AOT 发布：

```bash
# 创建测试项目
dotnet new console -n AotTest
cd AotTest

# 添加 Catga
dotnet add package Catga
dotnet add package Catga.Serialization.MemoryPack
dotnet add package Catga.Transport.InMemory

# 启用 AOT
<PublishAot>true</PublishAot>

# 发布并测试
dotnet publish -c Release
./bin/Release/net10.0/win-x64/publish/AotTest.exe
```

## 🎯 性能说明

这里不再给固定倍数结论。

更稳妥的读法是：

- AOT 通常有助于启动、部署体积和运行时可预测性
- 真正收益要以你的目标应用和发布参数实测为准
- 运行时数字请看 [Benchmark Results](../BENCHMARK-RESULTS.md)

## 📚 更多资源

- [性能报告](../topics/history/PERFORMANCE-REPORT.md)
- [源生成器使用指南](../guides/source-generator.md)
- [Native AOT 最佳实践](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
- [System.Text.Json 源生成器](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/source-generation)
- [MemoryPack 文档](https://github.com/Cysharp/MemoryPack)

## ❓ 常见问题

### Q: 为什么不让所有库都100% AOT兼容？

A: Catga 采用分层设计：
- **核心层**（Catga + MemoryPack 路线）：优先保证 AOT 友好
- **扩展层**（序列化/持久化）：保持灵活性，用户可选配置

这样既保证了生产环境的 AOT 兼容性，又保持了开发环境的便利性。

### Q: 我必须使用 InMemory 才能做 AOT 吗？

A:
- 不是。
- `InMemory` 更适合先验证最小可用 AOT 路线。
- 真正生产时，更常见的是 `UseRedis(...) + AddRedisTransport(...)` 或 `UseNats(...) + AddNatsTransport(...)`。

### Q: 我必须使用 MemoryPack 吗？

A: 不是。你可以：
1. 使用 MemoryPack（最简单，AOT 友好）
2. 使用 System.Text.Json + 源生成器（需要配置）
3. 实现自己的 `IMessageSerializer`（完全控制）

### Q: 如何在现有项目中迁移到 AOT？

A:
1. 先改成 `UseMemoryPack()`
2. 配置序列化器（MemoryPack 或 JSON Context）
3. 测试发布：`dotnet publish /p:PublishAot=true`
4. 修复任何警告

## 🎉 总结

Catga 的核心已经为 Native AOT 做好了充分准备！

选择合适的序列化方案，享受极致性能：
- 🚀 更稳的 AOT 发布路径
- 💾 更小的部署产物潜力
- ⚡ 更低的运行时开销潜力
- 🔒 **更安全**（无动态代码生成）

开始你的 AOT 之旅吧！🎊
