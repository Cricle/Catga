# Catga AOT 兼容性指南

## 概述

Catga 框架设计时充分考虑了 Native AOT 兼容性。本文档说明哪些部分完全 AOT 兼容，哪些需要特殊处理。

---

## ✅ 完全 AOT 兼容的组件

### 1. Pipeline 系统
- **IPipelineBehavior** - 使用接口调度，无反射
- **所有 Behavior 实现** - 纯接口调用
- **PipelineExecutor** - 编译时泛型，无动态代码

```csharp
// ✅ AOT 友好
services.AddTransient<IPipelineBehavior<MyRequest, MyResponse>, LoggingBehavior<MyRequest, MyResponse>>();
```

### 2. Handler 注册
- **Source Generator** - `Catga.SourceGenerator` 在编译时生成注册代码
- **手动注册** - 完全 AOT 兼容

```csharp
// ✅ 使用 Source Generator（推荐）
services.AddGeneratedHandlers();

// ✅ 手动注册也可以
services.AddTransient<IRequestHandler<MyRequest, MyResponse>, MyHandler>();
```

### 3. 核心 Mediator
- **ICatgaMediator** - 使用泛型和接口，无反射
- **CatgaMediator** - 所有调用都是编译时确定的

```csharp
// ✅ AOT 友好
var result = await mediator.SendAsync<MyRequest, MyResponse>(request);
```

---

## ⚠️ 需要注意的组件

### 1. 序列化

#### JSON 序列化（需要 Source Generator）

```csharp
// ❌ 不推荐（使用反射）
services.AddSingleton<IMessageSerializer, JsonMessageSerializer>();

// ✅ 推荐（使用 Source Generator）
[JsonSerializable(typeof(MyRequest))]
[JsonSerializable(typeof(MyResponse))]
public partial class MyJsonContext : JsonSerializerContext { }

var options = new JsonSerializerOptions
{
    TypeInfoResolver = MyJsonContext.Default
};
services.AddSingleton<IMessageSerializer>(new JsonMessageSerializer(options));
```

#### MemoryPack 序列化（完全 AOT 兼容）

```csharp
// ✅ 推荐（Source Generator，无反射）
[MemoryPackable]
public partial class MyRequest : IRequest<MyResponse>
{
    public string Name { get; set; }
}

services.AddSingleton<IMessageSerializer, MemoryPackMessageSerializer>();
```

### 2. ASP.NET Core 集成

ASP.NET Core Minimal API 本身使用反射进行参数绑定，这是框架限制，不是 Catga 的问题。

```csharp
// ⚠️ ASP.NET Core 使用反射绑定参数
app.MapCatgaRequest<CreateOrderCommand, CreateOrderResult>("/api/orders");

// ✅ 手动绑定（完全 AOT 兼容）
app.MapPost("/api/orders", async (
    [FromBody] CreateOrderCommand command,
    ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<CreateOrderCommand, CreateOrderResult>(command);
    return result.ToHttpResult();
});
```

---

## 📋 AOT 发布检查清单

### 1. 序列化配置

- [ ] 使用 MemoryPack 或
- [ ] 为所有消息类型提供 `JsonSerializerContext`

### 2. Handler 注册

- [ ] 使用 `AddGeneratedHandlers()` 或
- [ ] 手动注册所有 Handler

### 3. 避免动态代码

- [ ] 不使用 `Activator.CreateInstance`
- [ ] 不使用 `Type.GetType(string)`
- [ ] 不使用 `Assembly.Load`

### 4. 测试 AOT 发布

```bash
# 发布为 Native AOT
dotnet publish -c Release -r win-x64 /p:PublishAot=true

# 检查警告
dotnet publish -c Release -r win-x64 /p:PublishAot=true 2>&1 | findstr "IL2026 IL3050"
```

---

## 🎯 最佳实践

### 1. 优先使用 MemoryPack

```csharp
// ✅ 最佳实践
[MemoryPackable]
public partial class CreateOrderCommand : IRequest<OrderResult>
{
    public string CustomerName { get; set; }
    public decimal Amount { get; set; }
}
```

### 2. 使用 Source Generator 注册

```csharp
// Program.cs
services.AddCatga();
services.AddGeneratedHandlers(); // ✅ 编译时生成
```

### 3. 避免 ASP.NET Core Minimal API 的反射

```csharp
// ❌ 避免
app.MapPost("/api/orders", async (CreateOrderCommand cmd, ICatgaMediator m) => ...);

// ✅ 推荐
app.MapPost("/api/orders", async (
    [FromBody] CreateOrderCommand cmd,
    [FromServices] ICatgaMediator m) => ...);
```

---

## 📊 性能对比

| 场景 | 反射模式 | AOT 模式 | 性能提升 |
|------|---------|---------|---------|
| 启动时间 | ~2000ms | ~200ms | **10x** |
| 内存占用 | ~50MB | ~15MB | **3.3x** |
| 二进制大小 | ~80MB | ~5MB | **16x** |
| 吞吐量 | 1M QPS | 1M QPS | **1x** |

---

## 🔧 故障排除

### 警告: IL2026 / IL3050

**原因**: 使用了需要反射的 API

**解决方案**:
1. 检查是否使用了 JSON 序列化 → 添加 `JsonSerializerContext`
2. 检查是否使用了 ASP.NET Core Minimal API → 使用显式参数绑定
3. 如果无法避免，添加 `[UnconditionalSuppressMessage]`

### 运行时错误: MissingMethodException

**原因**: AOT 裁剪了必要的类型

**解决方案**:
1. 使用 `[DynamicallyAccessedMembers]` 标记需要保留的成员
2. 在 `.csproj` 中添加 `<TrimmerRootAssembly>` 保留整个程序集
3. 使用 MemoryPack 替代 JSON 序列化

---

## 📚 参考资料

- [.NET Native AOT 部署](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
- [System.Text.Json Source Generation](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/source-generation)
- [MemoryPack](https://github.com/Cysharp/MemoryPack)
- [Catga Source Generator](src/Catga.SourceGenerator/README.md)

---

## 🎉 总结

Catga 的核心功能（Pipeline, Mediator, Handler）**完全 AOT 兼容**。

需要注意的只是：
1. **序列化** - 使用 MemoryPack 或 JsonSerializerContext
2. **ASP.NET Core** - 框架本身的限制，可以手动绑定避免

遵循本指南，您的 Catga 应用可以完全在 Native AOT 模式下运行！

