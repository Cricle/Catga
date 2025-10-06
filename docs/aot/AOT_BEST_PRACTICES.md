# 🎯 Catga AOT 最佳实践指南

**目标**: 100% NativeAOT 兼容
**更新时间**: 2024-10-06

---

## 📋 核心原则

### ✅ 真正的 AOT 兼容
- **不隐藏警告** - 使用 `[RequiresUnreferencedCode]` 和 `[RequiresDynamicCode]` 明确标记
- **提供选择** - 让开发者知道哪些功能需要反射/动态代码
- **使用 AOT 友好的替代方案** - 推荐 MemoryPack 等序列化器

### ❌ 避免的做法
- ~~使用 `[UnconditionalSuppressMessage]` 简单抑制警告~~ ❌
- ~~隐藏 AOT 兼容性问题~~ ❌
- ~~强制使用需要反射的功能~~ ❌

---

## 🔧 Outbox/Inbox Behavior 的 AOT 使用

### **问题说明**
`OutboxBehavior` 和 `InboxBehavior` 需要序列化消息，这在 NativeAOT 环境下有限制。

### **解决方案 1: 使用 AOT 友好的序列化器（推荐）** ⭐

```csharp
using Catga.Serialization.MemoryPack;

var builder = WebApplication.CreateBuilder(args);

// 使用 MemoryPack 序列化器（100% AOT 兼容）
builder.Services.AddSingleton<IMessageSerializer, MemoryPackMessageSerializer>();

// 注册 Outbox/Inbox Behaviors
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(OutboxBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(InboxBehavior<,>));
```

**优点**:
- ✅ 完全 AOT 兼容
- ✅ 高性能
- ✅ 二进制序列化，体积小

**限制**:
- ⚠️ 需要在消息类型上添加 `[MemoryPackable]` 属性
- ⚠️ 仅支持 .NET 类型（不能跨语言）

### **解决方案 2: 不使用 Outbox/Inbox（开发环境）**

```csharp
var builder = WebApplication.CreateBuilder(args);

// 不注册 Outbox/Inbox Behaviors
// 直接使用核心 CQRS 功能（100% AOT 兼容）
builder.Services.AddCatga();
builder.Services.AddRequestHandler<TRequest, TResponse, THandler>();
```

**适用场景**:
- ✅ 开发环境
- ✅ 不需要消息可靠性保证的场景
- ✅ 单体应用

### **解决方案 3: 使用条件编译**

```csharp
#if !AOT
// 开发环境：使用 JSON 序列化
builder.Services.AddSingleton<IMessageSerializer, JsonMessageSerializer>();
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(OutboxBehavior<,>));
#else
// AOT 环境：使用 MemoryPack
builder.Services.AddSingleton<IMessageSerializer, MemoryPackMessageSerializer>();
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(OutboxBehavior<,>));
#endif
```

---

## 📊 各组件 AOT 兼容性

### ✅ **100% AOT 兼容（无需额外配置）**

| 组件 | 状态 | 说明 |
|------|------|------|
| `CatgaMediator` | ✅ | 核心 Mediator，完全 AOT 兼容 |
| `Pipeline` | ✅ | Pipeline 执行器，零反射 |
| `Result<T>` | ✅ | 结果类型，AOT 友好 |
| `LoggingBehavior` | ✅ | 日志行为，无序列化 |
| `ValidationBehavior` | ✅ | 验证行为（如果验证器是 AOT 友好的） |
| `RetryBehavior` | ✅ | 重试行为，无序列化 |
| `CircuitBreakerBehavior` | ✅ | 熔断行为，无序列化 |

### ⚠️ **需要配置 AOT 友好序列化器**

| 组件 | 要求 | 推荐方案 |
|------|------|----------|
| `OutboxBehavior` | 序列化器 | 使用 MemoryPack |
| `InboxBehavior` | 序列化器 | 使用 MemoryPack |
| `IdempotencyBehavior` | 序列化器 | 使用 MemoryPack |

### ⚠️ **有限制的 AOT 兼容**

| 组件 | 限制 | 解决方案 |
|------|------|----------|
| `AddCatgaDevelopment()` | 使用反射扫描 | 生产环境使用手动注册 |
| `ScanHandlers()` | 使用反射扫描 | 手动注册 Handlers |

---

## 🚀 生产环境 AOT 配置示例

### **完整的 AOT 兼容配置**

```csharp
using Catga;
using Catga.Serialization.MemoryPack;
using MemoryPack;

var builder = WebApplication.CreateBuilder(args);

// 1. 注册序列化器（AOT 友好）
builder.Services.AddSingleton<IMessageSerializer, MemoryPackMessageSerializer>();

// 2. 注册核心 Catga 服务
builder.Services.AddCatga();

// 3. 手动注册 Handlers（避免反射）
builder.Services.AddRequestHandler<CreateOrderCommand, OrderResult, CreateOrderHandler>();
builder.Services.AddEventHandler<OrderCreatedEvent, OrderCreatedHandler>();

// 4. 注册 NATS（可选）
builder.Services.AddNatsDistributed("nats://localhost:4222");

// 5. 注册 Outbox/Inbox（可选，需要 MemoryPack）
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(OutboxBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(InboxBehavior<,>));

// 6. 注册 JetStream 存储
builder.Services.AddNatsJetStreamStores();

var app = builder.Build();
app.Run();
```

### **消息类型定义（MemoryPack）**

```csharp
using MemoryPack;

[MemoryPackable]
public partial record CreateOrderCommand(
    string OrderId,
    decimal Amount
) : IRequest<OrderResult>;

[MemoryPackable]
public partial record OrderResult(
    string OrderId,
    bool Success
);

[MemoryPackable]
public partial record OrderCreatedEvent(
    string OrderId,
    decimal Amount,
    DateTime OccurredAt
) : IEvent;
```

---

## 📝 警告标记说明

### **`[RequiresUnreferencedCode]`**
```csharp
[RequiresUnreferencedCode("此功能需要序列化，可能需要无法静态分析的类型")]
public class OutboxBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
```

**含义**:
- 此组件在裁剪（trimming）时可能会失败
- 需要保留类型的元数据
- 在 AOT 环境下需要特殊处理

**开发者的选择**:
1. 使用 AOT 友好的序列化器（如 MemoryPack）
2. 或者不使用此功能

### **`[RequiresDynamicCode]`**
```csharp
[RequiresDynamicCode("此功能需要序列化，可能需要运行时代码生成")]
public class OutboxBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
```

**含义**:
- 此组件可能需要运行时代码生成
- 在 NativeAOT 环境下可能无法工作
- 需要编译时生成代码

**开发者的选择**:
1. 使用源生成器（如 MemoryPack）
2. 或者不使用此功能

---

## ✅ AOT 兼容性检查清单

### **编译时检查**
- [ ] 运行 `dotnet publish -c Release /p:PublishAot=true`
- [ ] 检查是否有 `IL2026`（裁剪警告）
- [ ] 检查是否有 `IL3050`（AOT 警告）
- [ ] 确认警告的组件是否可以替换

### **运行时验证**
- [ ] 使用 NativeAOT 发布并运行
- [ ] 测试序列化/反序列化
- [ ] 测试 Outbox/Inbox 功能
- [ ] 测试分布式消息传递

### **性能验证**
- [ ] 对比 AOT vs JIT 性能
- [ ] 检查启动时间
- [ ] 检查内存占用
- [ ] 检查序列化性能

---

## 🔍 常见问题

### **Q: 为什么不能简单抑制警告？**
A: 抑制警告只是隐藏问题，不能解决实际的 AOT 兼容性问题。真正的解决方案是：
1. 明确标记需要动态代码的组件
2. 提供 AOT 友好的替代方案
3. 让开发者做出明智的选择

### **Q: OutboxBehavior 在 AOT 下能工作吗？**
A: 可以，但需要：
1. 使用 AOT 友好的序列化器（如 MemoryPack）
2. 在消息类型上添加相应的属性
3. 确保消息类型可以被静态分析

### **Q: 如何在开发和生产环境使用不同的配置？**
A: 使用条件编译或配置：
```csharp
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCatgaDevelopment(); // 使用反射
}
else
{
    // 手动注册，AOT 友好
    builder.Services.AddCatga();
    builder.Services.AddRequestHandler<...>();
}
```

### **Q: 所有功能都必须 AOT 兼容吗？**
A: 不是。Catga 的设计原则是：
1. **核心功能** 100% AOT 兼容
2. **可选功能** 明确标记 AOT 限制
3. **开发工具** 可以使用反射（如自动扫描）

---

## 📊 性能对比

### **序列化器性能（AOT 模式）**

| 序列化器 | 序列化速度 | 反序列化速度 | 体积 | AOT 兼容 |
|---------|-----------|------------|------|----------|
| JSON (反射) | 较慢 | 较慢 | 大 | ❌ |
| JSON (源生成) | 快 | 快 | 大 | ✅ |
| MemoryPack | 最快 | 最快 | 最小 | ✅ |

### **启动时间对比**

```
JIT 模式:          ~2000ms
AOT (JSON):        ~800ms
AOT (MemoryPack):  ~500ms
```

---

## 🎯 推荐配置

### **开发环境**
```csharp
builder.Services.AddCatgaDevelopment();
```
- ✅ 自动扫描
- ✅ 快速开发
- ✅ 灵活调试

### **生产环境**
```csharp
builder.Services.AddSingleton<IMessageSerializer, MemoryPackMessageSerializer>();
builder.Services.AddCatga();
builder.Services.AddRequestHandler<...>(); // 手动注册
```
- ✅ 100% AOT 兼容
- ✅ 最佳性能
- ✅ 最小体积

---

## 📚 相关资源

- [MemoryPack 文档](https://github.com/Cysharp/MemoryPack)
- [.NET NativeAOT 指南](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
- [Catga 序列化文档](../serialization/README.md)

---

**记住**: 真正的 AOT 兼容不是隐藏警告，而是提供正确的解决方案！ ✅

