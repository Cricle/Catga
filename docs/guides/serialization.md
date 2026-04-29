# Catga 序列化指南

> **一站式序列化配置指南** - MemoryPack vs JSON 完整对比
> 最后更新: 2025-10-14

[返回主文档](../README.md) · [架构设计](../architecture/ARCHITECTURE.md)

---

## 🎯 快速决策

### 决策树

```mermaid
graph TD
    A[需要 Native AOT?] -->|是| B[MemoryPack]
    A -->|否| C[需要人类可读?]
    C -->|是| D[JSON]
    C -->|否| B

    B --> E[所有消息标注 [MemoryPackable]]
    D --> F[配置 JsonSerializerContext]

    E --> G[✅ AOT 友好]
    F --> H[⚠️ 需额外配置]

    style B fill:#90EE90
    style D fill:#FFD700
    style G fill:#90EE90
    style H fill:#FFD700
```

### 推荐方案

| 场景 | 推荐 | 理由 |
|------|------|------|
| **生产环境** | ✅ MemoryPack | 性能最优，AOT 友好 |
| **Native AOT** | ✅ MemoryPack | AOT 路线最直接 |
| **开发调试** | ⚠️ JSON | 人类可读，便于调试 |
| **跨语言** | ⚠️ JSON | 通用格式 |
| **高性能** | ✅ MemoryPack | 二进制格式、运行时开销更低 |

---

## 🔥 MemoryPack (推荐)

### 为什么选择 MemoryPack？

**核心优势**:
- ✅ **AOT 友好** - 避免运行时反射和动态代码生成
- ✅ **运行时开销更低** - 通常比 JSON 更适合高频消息
- ✅ **Payload 更紧凑** - 二进制格式通常更省带宽
- ✅ **零拷贝** - 反序列化零内存分配
- ✅ **类型安全** - 编译时检查
- ✅ **易于使用** - 一个属性搞定

### 安装

```bash
# 1. 安装 Catga MemoryPack 扩展（推荐）
dotnet add package Catga.Serialization.MemoryPack

# 2. 安装 MemoryPack 核心库
dotnet add package MemoryPack
dotnet add package MemoryPack.Generator
```

### 基础使用

#### 1. 标注消息类型

```csharp
using MemoryPack;
using Catga.Messages;

// Command
[MemoryPackable]
public partial record CreateOrder(string OrderId, decimal Amount)
    : IRequest<OrderResult>;

// Query
[MemoryPackable]
public partial record GetOrder(string OrderId)
    : IRequest<Order?>;

// Event
[MemoryPackable]
public partial record OrderCreated(string OrderId, DateTime CreatedAt)
    : IEvent;

// Result
[MemoryPackable]
public partial record OrderResult(string OrderId, bool Success);

[MemoryPackable]
public partial record Order(string Id, string UserId, decimal Amount);
```

**关键点**:
- ✅ 必须添加 `[MemoryPackable]` 属性
- ✅ 必须使用 `partial` 关键字
- ✅ 推荐使用 `record`（不可变）
- ✅ 支持 `class` 和 `struct`

#### 2. 配置序列化器

```csharp
using Catga.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// 最小推荐配置
builder.Services.AddCatga()
    .UseMemoryPack()
    .ForProduction();

var app = builder.Build();
app.Run();
```

#### 3. 使用（无需额外代码）

```csharp
public class OrderService
{
    private readonly ICatgaMediator _mediator;

    public OrderService(ICatgaMediator mediator) => _mediator = mediator;

    public async Task<OrderResult> CreateOrderAsync(string orderId, decimal amount)
    {
        // 自动使用 MemoryPack 序列化
        var result = await _mediator.SendAsync<CreateOrder, OrderResult>(
            new CreateOrder(orderId, amount));

        return result.Value!;
    }
}
```

### 高级特性

#### 支持的类型

```csharp
// ✅ 基本类型
[MemoryPackable]
public partial record BasicTypes(
    int IntValue,
    long LongValue,
    float FloatValue,
    double DoubleValue,
    decimal DecimalValue,
    bool BoolValue,
    string StringValue,
    DateTime DateTimeValue,
    Guid GuidValue
);

// ✅ 集合类型
[MemoryPackable]
public partial record Collections(
    List<string> StringList,
    Dictionary<string, int> StringIntDict,
    int[] IntArray,
    HashSet<string> StringSet
);

// ✅ 嵌套类型
[MemoryPackable]
public partial record OrderItem(string ProductId, int Quantity, decimal Price);

[MemoryPackable]
public partial record Order(
    string OrderId,
    List<OrderItem> Items,  // 嵌套
    OrderStatus Status      // 枚举
);

public enum OrderStatus
{
    Pending,
    Confirmed,
    Shipped,
    Delivered
}

// ✅ 可空类型
[MemoryPackable]
public partial record NullableTypes(
    string? NullableString,
    int? NullableInt,
    Order? NullableOrder
);
```

#### 版本兼容

```csharp
// 使用 MemoryPackOrder 控制序列化顺序
[MemoryPackable]
public partial record OrderV1(
    [property: MemoryPackOrder(0)] string OrderId,
    [property: MemoryPackOrder(1)] decimal Amount
);

// 添加新字段时保持兼容
[MemoryPackable]
public partial record OrderV2(
    [property: MemoryPackOrder(0)] string OrderId,
    [property: MemoryPackOrder(1)] decimal Amount,
    [property: MemoryPackOrder(2)] string? UserId = null  // 新字段，默认值
);
```

#### 忽略字段

```csharp
[MemoryPackable]
public partial record User(
    string Id,
    string Name,
    [property: MemoryPackIgnore] string Password  // 不序列化
);
```

### 性能说明

这里不再给固定倍数承诺。

更稳妥的理解是：

- MemoryPack 通常更适合高频、低开销的 .NET 内部消息
- JSON 更适合人类可读、跨语言和外部系统集成
- 真实差异取决于消息结构、发布参数和是否使用 AOT

实际 benchmark 请以仓库里的最新性能文档和你的业务实测为准。

### AOT 验证

```bash
# 发布 AOT 应用
dotnet publish -c Release -r linux-x64 --property:PublishAot=true

# 验证应用可启动
time ./bin/Release/net10.0/linux-x64/publish/YourApp

# 查看发布产物
ls -lh ./bin/Release/net10.0/linux-x64/publish/YourApp
```

### 常见问题

#### Q: 忘记添加 [MemoryPackable] 怎么办？

**A**: Catga 分析器会在编译时警告：

```csharp
// ❌ 编译时警告: CATGA001
public record CreateOrder(string OrderId) : IRequest<bool>;
//              ^^^^^^^^^^^
// 💡 添加 [MemoryPackable] 以获得最佳 AOT 性能

// ✅ 正确
[MemoryPackable]
public partial record CreateOrder(string OrderId) : IRequest<bool>;
```

#### Q: 可以序列化接口吗？

**A**: 不能直接序列化接口，需要使用具体类型：

```csharp
// ❌ 不支持
public interface IMessage { }

// ✅ 使用具体类型
[MemoryPackable]
public partial record ConcreteMessage(...) : IMessage;
```

#### Q: 如何处理继承？

**A**: 使用 `MemoryPackUnion`：

```csharp
[MemoryPackUnion(0, typeof(CreateOrderCommand))]
[MemoryPackUnion(1, typeof(UpdateOrderCommand))]
[MemoryPackable]
public abstract partial record OrderCommand;

[MemoryPackable]
public partial record CreateOrderCommand(string OrderId) : OrderCommand;

[MemoryPackable]
public partial record UpdateOrderCommand(string OrderId, string Status) : OrderCommand;
```

---

## 📝 JSON（自定义实现，作为参考）

### 何时使用 JSON？

**适用场景**:
- ⚠️ 需要人类可读的格式（调试）
- ⚠️ 跨语言互操作
- ⚠️ 已有 JSON 基础设施
- ⚠️ 不追求极致性能

**不推荐场景**:
- ❌ Native AOT 生产环境（需额外配置）
- ❌ 高性能场景
- ❌ 大量消息传输

### 安装

不提供官方 JSON 包。建议基于 System.Text.Json（源生成）实现 `IMessageSerializer` 并手动注册。

### 基础使用（不推荐 AOT）

```csharp
using Catga.DependencyInjection;

// ⚠️ 不推荐：直接反射 JSON（AOT 不支持，示例仅用于说明）
builder.Services.AddCatga();
builder.Services.AddSingleton<IMessageSerializer, ReflectionJsonSerializer>();
builder.Services.AddCatga().ForProduction();
```

### AOT 使用（推荐）

#### 1. 定义 JsonSerializerContext

```csharp
using System.Text.Json.Serialization;

// 为所有消息类型创建 Context
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CreateOrder))]
[JsonSerializable(typeof(UpdateOrder))]
[JsonSerializable(typeof(GetOrder))]
[JsonSerializable(typeof(OrderResult))]
[JsonSerializable(typeof(Order))]
[JsonSerializable(typeof(OrderCreated))]
[JsonSerializable(typeof(OrderUpdated))]
public partial class AppJsonContext : JsonSerializerContext
{
}
```

#### 2. 配置序列化器

```csharp
using System.Text.Json;

builder.Services.AddCatga()
    .UseJson(new JsonSerializerOptions
    {
        TypeInfoResolver = AppJsonContext.Default  // 使用 Source Generator
    })
    .ForProduction();
```

#### 3. 定义消息（无需特殊属性）

```csharp
// 普通 record，无需 [MemoryPackable]
public record CreateOrder(string OrderId, decimal Amount)
    : IRequest<OrderResult>;

public record OrderResult(string OrderId, bool Success);
```

### JSON 配置选项（自定义实现示例）

```csharp
var options = new JsonSerializerOptions
{
    TypeInfoResolver = AppJsonContext.Default,  // AOT 必需
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip
};
builder.Services.AddCatga();
builder.Services.AddSingleton<IMessageSerializer>(sp => new CustomSerializer(options));
```

### 方案对比

| 方案 | AOT 兼容 | 人类可读 | 配置复杂度 | 适用场景 |
|------|---------|---------|-----------|---------|
| JSON（反射） | ❌ | ✅ | 低 | 仅限非 AOT、本地调试示例 |
| JSON（源生成） | ✅ | ✅ | 中 | 跨语言、外部接口、文本排查 |
| MemoryPack | ✅ | ❌ | 低 | 默认推荐、.NET 内部消息 |

### 常见问题

#### Q: 为什么 JSON 需要 JsonSerializerContext？

**A**: Native AOT 不支持反射，必须使用 Source Generator：

```csharp
// ❌ AOT 不支持
JsonSerializer.Serialize(order);  // 运行时反射

// ✅ AOT 支持
JsonSerializer.Serialize(order, AppJsonContext.Default.Order);  // 编译时生成
```

#### Q: 忘记添加类型到 JsonSerializerContext 怎么办？

**A**: 运行时会抛出异常：

```csharp
// 如果 NewMessage 未在 Context 中声明
var result = await mediator.SendAsync<NewMessage, Result>(new NewMessage());
// 💥 NotSupportedException: Serialization of 'NewMessage' is not supported
```

**解决方案**: 添加到 Context：

```csharp
[JsonSerializable(typeof(NewMessage))]  // ← 添加这行
public partial class AppJsonContext : JsonSerializerContext { }
```

---

## 📊 完整对比

### 功能对比

| 特性 | MemoryPack | JSON |
|------|-----------|------|
| **AOT 兼容性** | ✅ 友好 | ⚠️ 需配置 |
| **性能** | 🔥 通常更优 | ⚡ 取决于实现 |
| **Payload 大小** | 📦 通常更紧凑 | 📦 通常更大 |
| **人类可读** | ❌ 二进制 | ✅ 文本 |
| **跨语言** | ❌ .NET Only | ✅ 通用 |
| **配置复杂度** | ✅ 简单 | ⚠️ 中等 |
| **类型安全** | ✅ 编译时 | ⚠️ 运行时 |
| **版本兼容** | ✅ 支持 | ✅ 支持 |
| **调试友好** | ❌ | ✅ |

### 性能判断原则

如果你需要：

- 最小运行时开销：优先 `MemoryPack`
- 可读文本和跨语言：优先 JSON
- Native AOT：优先 `MemoryPack`，或使用带 `JsonSerializerContext` 的自定义 JSON

不要把文档里的历史示例数字直接当成你的系统收益。

### 使用建议

| 场景 | 推荐 | 配置 |
|------|------|------|
| **生产环境** | MemoryPack | `.UseMemoryPack()` |
| **Native AOT** | MemoryPack | `.UseMemoryPack()` |
| **高性能** | MemoryPack | `.UseMemoryPack()` |
| **开发调试** | JSON（自定义） | `AddCatga()+AddSingleton<IMessageSerializer>` |
| **跨语言** | JSON（自定义） | `AddCatga()+AddSingleton<IMessageSerializer>` |
| **微服务** | MemoryPack | `.UseMemoryPack()` |

---

## 🔄 迁移指南

### 从 JSON 迁移到 MemoryPack

#### 步骤 1: 安装 MemoryPack

```bash
dotnet add package Catga.Serialization.MemoryPack
dotnet add package MemoryPack
dotnet add package MemoryPack.Generator
```

#### 步骤 2: 添加属性

```csharp
// Before
public record CreateOrder(string OrderId, decimal Amount)
    : IRequest<OrderResult>;

// After
[MemoryPackable]
public partial record CreateOrder(string OrderId, decimal Amount)
    : IRequest<OrderResult>;
```

#### 步骤 3: 更新配置

```csharp
// Before（自定义 JSON 注册）
services.AddCatga();
services.AddSingleton<IMessageSerializer, CustomSerializer>();

// After
services.AddCatga().UseMemoryPack();
```

#### 步骤 4: 验证

```bash
# 编译检查
dotnet build

# 运行测试
dotnet test

# AOT 发布
dotnet publish -c Release -r linux-x64 --property:PublishAot=true
```

### 从 MemoryPack 迁移到 JSON

#### 步骤 1: 使用 System.Text.Json（源生成）实现自定义序列化器

#### 步骤 2: 创建 JsonSerializerContext

```csharp
[JsonSerializable(typeof(CreateOrder))]
[JsonSerializable(typeof(OrderResult))]
// ... 所有消息类型
public partial class AppJsonContext : JsonSerializerContext { }
```

#### 步骤 3: 更新配置

```csharp
// Before
services.AddCatga().UseMemoryPack();

// After（自定义注册）
var options = new JsonSerializerOptions { TypeInfoResolver = AppJsonContext.Default };
services.AddCatga();
services.AddSingleton<IMessageSerializer>(sp => new CustomSerializer(options));
```

#### 步骤 4: 移除 MemoryPack 属性（可选）

```csharp
// Before
[MemoryPackable]
public partial record CreateOrder(...) : IRequest<OrderResult>;

// After
public record CreateOrder(...) : IRequest<OrderResult>;
```

---

## 🛠️ 自定义序列化器

### 实现 IMessageSerializer

```csharp
using Catga.Serialization;

public class CustomSerializer : IMessageSerializer
{
    public byte[] Serialize<T>(T message)
    {
        // 自定义序列化逻辑
        // 例如: Protobuf, MessagePack, BSON 等
        throw new NotImplementedException();
    }

    public T? Deserialize<T>(byte[] data)
    {
        // 自定义反序列化逻辑
        throw new NotImplementedException();
    }
}
```

### 注册自定义序列化器

```csharp
// 方式 1: 直接注册
services.AddCatga();
services.AddSingleton<IMessageSerializer, CustomSerializer>();

// 方式 2: 扩展方法
public static class CustomSerializerExtensions
{
    public static CatgaServiceBuilder UseCustomSerializer(
        this CatgaServiceBuilder builder)
    {
        builder.Services.AddSingleton<IMessageSerializer, CustomSerializer>();
        return builder;
    }
}

// 使用
services.AddCatga().UseCustomSerializer();
```

---

## 📚 相关资源

- **[MemoryPack 官方文档](https://github.com/Cysharp/MemoryPack)** - 完整的 MemoryPack 指南
- **[System.Text.Json 源生成](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/source-generation)** - JSON 源生成器
- **[Native AOT 部署](../deployment/native-aot-publishing.md)** - AOT 发布指南
- **[性能优化报告](../topics/history/PERFORMANCE-REPORT.md)** - 历史性能总结

---

## 🎯 最佳实践

### ✅ 推荐做法

1. **生产环境使用 MemoryPack**
   ```csharp
   services.AddCatga().UseMemoryPack().ForProduction();
   ```

2. **所有消息标注 [MemoryPackable]**
   ```csharp
   [MemoryPackable]
   public partial record MyMessage(...) : IRequest<MyResult>;
   ```

3. **使用 record 类型**
   ```csharp
   // ✅ 推荐: record (不可变)
   [MemoryPackable]
   public partial record CreateOrder(...);

   // ⚠️ 可以但不推荐: class (可变)
   [MemoryPackable]
   public partial class CreateOrder { ... }
   ```

4. **启用分析器**
   ```xml
   <PropertyGroup>
     <EnableNETAnalyzers>true</EnableNETAnalyzers>
   </PropertyGroup>
   ```

### ❌ 避免做法

1. **不要混用序列化器**
   ```csharp
   // ❌ 错误: 不同服务使用不同序列化器
   ServiceA: UseMemoryPack()
   ServiceB: 自定义 JSON
   // 无法互相通信！
   ```

2. **不要忘记 partial 关键字**
   ```csharp
   // ❌ 编译错误
   [MemoryPackable]
   public record CreateOrder(...);  // 缺少 partial

   // ✅ 正确
   [MemoryPackable]
   public partial record CreateOrder(...);
   ```

3. **不要在 AOT 中使用反射 JSON**
   ```csharp
   // ❌ AOT 不支持（反射路径）
   builder.Services.AddCatga();
   builder.Services.AddSingleton<IMessageSerializer, ReflectionJsonSerializer>();

   // ✅ AOT 支持（源生成 + 手动注册）
   var options = new JsonSerializerOptions { TypeInfoResolver = AppJsonContext.Default };
   builder.Services.AddCatga();
   builder.Services.AddSingleton<IMessageSerializer>(sp => new CustomSerializer(options));
   ```

---

<div align="center">

**选择和你的部署目标一致的序列化器**

[返回主文档](../README.md) · [架构设计](../architecture/ARCHITECTURE.md)

**推荐**: 生产默认先用 MemoryPack；确实需要文本可读或跨语言时，再切自定义 JSON

</div>

