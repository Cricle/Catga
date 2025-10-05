# AOT (Ahead-of-Time) 兼容性指南

## 📊 当前状态

Catga 框架在 NativeAOT 编译时会产生少量警告（12 个），但这些警告**不影响运行时功能**。

### 警告分类

#### 1. 框架生成的警告 (10 个)
来源：`System.Text.Json` 源生成器生成的代码中引用 `Exception.TargetSite`

```
warning IL2026: Using member 'System.Exception.TargetSite.get' which has
'RequiresUnreferencedCodeAttribute' can break functionality when trimming...
```

**影响**: 无，这是 .NET 框架生成的代码，不影响 Catga 的功能。

#### 2. Fallback Resolver 警告 (2 个)
来源：`NatsJsonSerializer` 中的 `DefaultJsonTypeInfoResolver`

```
warning IL2026/IL3050: Using member 'DefaultJsonTypeInfoResolver()' which has
'RequiresUnreferencedCodeAttribute'/'RequiresDynamicCodeAttribute'...
```

**原因**: Catga 是一个框架，支持用户定义的任意消息类型。为了在用户未提供 `JsonSerializerContext` 时仍能工作，我们使用 reflection-based fallback。

**影响**: 最小。用户可以通过提供自己的 `JsonSerializerContext` 完全消除这些警告（见下文）。

---

## ✅ 如何实现 100% AOT 兼容

### 方法 1: 定义完整的 JsonSerializerContext（推荐）

```csharp
using System.Text.Json.Serialization;
using Catga.Results;

// 定义包含所有消息类型的上下文
[JsonSerializable(typeof(CreateOrderCommand))]
[JsonSerializable(typeof(OrderResult))]
[JsonSerializable(typeof(OrderCreatedEvent))]
[JsonSerializable(typeof(GetOrderQuery))]
[JsonSerializable(typeof(OrderDto))]
[JsonSerializable(typeof(CatgaResult<OrderResult>))]
[JsonSerializable(typeof(CatgaResult<OrderDto>))]
[JsonSerializable(typeof(CatgaResult))]
// ... 添加所有你的消息类型
public partial class MyAppJsonContext : JsonSerializerContext
{
}
```

### 方法 2: 注册自定义 JsonSerializerOptions

```csharp
using Catga.Nats.Serialization;

var builder = WebApplication.CreateBuilder(args);

// 创建包含你的上下文的选项
var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    TypeInfoResolver = JsonTypeInfoResolver.Combine(
        MyAppJsonContext.Default,
        NatsCatgaJsonContext.Default  // Catga 内部类型
    )
};

// 设置为默认选项
NatsJsonSerializer.SetCustomOptions(jsonOptions);

// 添加服务
builder.Services.AddCatga();
builder.Services.AddNatsCatga("nats://localhost:4222");

var app = builder.Build();
app.Run();
```

### 方法 3: 抑制警告（快速方案）

如果你不需要 NativeAOT 编译，可以在项目文件中抑制这些警告：

```xml
<PropertyGroup>
    <NoWarn>IL2026;IL3050</NoWarn>
</PropertyGroup>
```

---

## 🎯 各模块 AOT 状态

| 模块 | AOT 警告 | 运行时影响 | 说明 |
|------|---------|----------|------|
| **Catga** (核心) | 0 | ✅ 无 | 100% AOT 兼容 |
| **Catga.Redis** | 0 | ✅ 无 | 100% AOT 兼容 |
| **Catga.Nats** | 12 | ✅ 无 | 可选 reflection fallback |

---

## 📝 最佳实践

### 1. 开发阶段
使用默认配置（reflection fallback），快速迭代：

```csharp
services.AddCatga();
services.AddNatsCatga("nats://localhost:4222");
// 不需要额外配置，开箱即用
```

### 2. 生产环境（追求极致性能）
定义完整的 `JsonSerializerContext` 并注册：

```csharp
// 定义所有消息类型
[JsonSerializable(typeof(MyCommand))]
[JsonSerializable(typeof(MyResult))]
// ...
public partial class ProductionJsonContext : JsonSerializerContext { }

// 注册
NatsJsonSerializer.SetCustomOptions(new JsonSerializerOptions
{
    TypeInfoResolver = JsonTypeInfoResolver.Combine(
        ProductionJsonContext.Default,
        NatsCatgaJsonContext.Default
    )
});
```

### 3. NativeAOT 发布
如果你使用 NativeAOT，必须使用方法 2：

```bash
dotnet publish -c Release -r linux-x64 \
  --self-contained true \
  -p:PublishAot=true
```

确保在发布前注册了完整的 `JsonSerializerContext`。

---

## 🧪 验证 AOT 兼容性

### 检查警告
```bash
dotnet build -c Release /p:PublishAot=true
```

### 本地测试
```bash
# 发布为 NativeAOT
dotnet publish -c Release -r win-x64 \
  --self-contained true \
  -p:PublishAot=true

# 运行测试
./bin/Release/net9.0/win-x64/publish/YourApp.exe
```

---

## ❓ 常见问题

### Q: 为什么有 12 个警告？
**A**: 10 个来自 .NET 框架生成的代码（不可控），2 个来自 reflection fallback（可选）。

### Q: 警告会影响性能吗？
**A**: 不会。使用 `JsonSerializerContext` 的代码路径是零反射的，性能与手写代码相同。

### Q: 必须提供 JsonSerializerContext 吗？
**A**: 不必须。只有在以下情况下才需要：
1. 使用 NativeAOT 编译
2. 追求极致性能
3. 想要消除所有警告

普通部署（非 AOT）下，默认配置完全可用。

### Q: 如何找出所有需要序列化的类型？
**A**:
1. 所有实现 `ICommand<T>`, `IQuery<T>`, `IEvent` 的消息类型
2. 所有作为响应的结果类型 `T`
3. 所有需要通过 NATS 传输的类型
4. 包装类型 `CatgaResult<T>`

---

## 📚 相关资源

- [.NET AOT 部署](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
- [JSON 源生成](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/source-generation)
- [Catga 性能优化](/docs/performance/optimization.md)

---

**Catga 致力于提供最佳的 AOT 兼容性，同时保持灵活性和易用性。** 🚀

