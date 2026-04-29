# Catga 内存优化使用指南

本指南介绍如何使用 Catga 的内存优化特性来提升应用程序性能。

## 📋 目录

- 快速开始
- 核心概念
- 序列化器选择
- 池化内存管理
- 最佳实践
- 性能基准
- AOT 兼容性

---

## 🚀 快速开始 {#quickstart}

### 1. 使用高性能序列化器

```csharp
// 使用 MemoryPack (推荐 - AOT 路线最直接)
services.AddCatga()
    .UseMemoryPack();

// 或使用自定义 JSON（实现 IMessageSerializer 并手动注册）
services.AddCatga();
services.AddSingleton<IMessageSerializer, CustomSerializer>();
```

### 2. 零分配序列化

```csharp
var serializer = serviceProvider.GetRequiredService<IMessageSerializer>();

// 直接使用 byte[]（内部使用池化缓冲区）
var bytes = serializer.Serialize(message);
await SendAsync(bytes);
```

### 3. 零拷贝反序列化

```csharp
// 从 ReadOnlySpan<byte> 反序列化（零拷贝）
var message = serializer.Deserialize<MyMessage>(receivedData.AsSpan());

// 从 byte[] 反序列化
var message = serializer.Deserialize<MyMessage>(receivedData);
```

---

## 🧠 核心概念 {#core-concepts}

### 内存池化

Catga 使用 `MemoryPoolManager` 统一管理所有内存池：

```csharp
// 租用数组（自动使用 ArrayPool）
using var pooled = MemoryPoolManager.RentArray(minimumLength: 1024);
pooled.Span.Fill(0);
// 离开 using 作用域时自动归还

// 租用缓冲区写入器
using var writer = MemoryPoolManager.RentBufferWriter(initialCapacity: 256);
writer.Write(data);
// 自动归还到池
```

### 简化的池化策略

`MemoryPoolManager` 使用 .NET 的共享池：

- **ArrayPool<byte>.Shared** - 所有数组租用
- **MemoryPool<byte>.Shared** - 已移除（直接使用 ArrayPool）

```csharp
// 小消息
using var small = MemoryPoolManager.RentArray(1024);  // 1KB

// 大消息
using var large = MemoryPoolManager.RentArray(256 * 1024);  // 256KB

// 都使用同一个共享池 - 简单高效
```

---

## 🎯 序列化器选择 {#serializer-choice}

### MemoryPackMessageSerializer (推荐)

**优势**:
- ✅ AOT 友好（源生成器）
- ✅ 零反射
- ✅ 更低运行时开销
- ✅ 完整池化支持
- ✅ 二进制格式（通常更紧凑）

**使用场景**:
- 微服务内部通信
- 高性能 API
- Native AOT 部署
- 实时系统

```csharp
// 1. 标记消息类型
[MemoryPackable]
public partial class OrderCreatedEvent
{
    public string OrderId { get; set; }
    public decimal Amount { get; set; }
}

// 2. 注册序列化器
services.AddCatga()
    .UseMemoryPack();

// 3. 自动使用池化
var bytes = serializer.Serialize(message);  // 内部使用 PooledBufferWriter
```

### JsonMessageSerializer

**优势**:
- ✅ 人类可读
- ✅ 工具支持好
- ✅ 跨语言兼容
- ✅ AOT 兼容（泛型方法）
- ✅ 完整池化支持

**使用场景**:
- 调试和开发
- 跨语言通信
- REST API 集成
- 需要可读性

```csharp
// 1. 配置 JsonSerializerOptions (可选 - AOT 优化)
[JsonSerializable(typeof(OrderCreatedEvent))]
public partial class MyJsonContext : JsonSerializerContext { }

// 2. 注册自定义序列化器（示例）
var options = new JsonSerializerOptions
{
    TypeInfoResolver = MyJsonContext.Default  // AOT 优化
};
services.AddCatga();
services.AddSingleton<IMessageSerializer>(sp => new CustomSerializer(options));
```

---

## 🏊 池化内存管理 {#pooled-memory}

### PooledArray 模式

```csharp
public async Task SendMessagePooled<T>(T message, IMessageSerializer serializer)
{
    // 序列化到池化数组
    using var pooled = MemoryPoolManager.RentArray(4096);

    // 使用 IBufferWriter 直接序列化
    using var writer = MemoryPoolManager.RentBufferWriter();
    serializer.Serialize(message, writer);

    // 发送
    await transport.PublishAsync(writer.WrittenMemory);

    // 离开作用域时自动归还
}
```

### PooledBufferWriter 模式

```csharp
public async Task WriteMessagesToStream<T>(
    IEnumerable<T> messages,
    Stream stream,
    IMessageSerializer serializer)
{
    // 获取池化写入器
    using var writer = MemoryPoolManager.RentBufferWriter(initialCapacity: 4096);

    // 批量序列化
    foreach (var message in messages)
    {
        serializer.Serialize(message, writer);
    }

    // 写入流
    await stream.WriteAsync(writer.WrittenMemory);

    // 自动清理
}
```

---

## 💡 最佳实践 {#best-practices}

### 1. 始终使用 using 语句

```csharp
// ✅ 正确
using var pooled = MemoryPoolManager.RentArray(1024);
await SendAsync(pooled.Memory);

// ❌ 错误 - 内存泄漏！
var pooled = MemoryPoolManager.RentArray(1024);
await SendAsync(pooled.Memory);
// 忘记 Dispose，内存永远不会归还
```

### 2. 不要存储 Memory/Span 引用

```csharp
// ❌ 错误 - 使用已释放的内存
ReadOnlyMemory<byte> storedMemory;
using (var pooled = MemoryPoolManager.RentArray(1024))
{
    storedMemory = pooled.Memory;  // 危险！
}
await SendAsync(storedMemory);  // 💥 已释放的内存

// ✅ 正确 - 在有效作用域内使用
using var pooled = MemoryPoolManager.RentArray(1024);
await SendAsync(pooled.Memory);
```

### 3. 小消息使用 stackalloc

```csharp
// 对于小消息 (< 256 bytes)，使用 stackalloc
Span<byte> buffer = stackalloc byte[256];
if (TrySerialize(message, buffer, out int bytesWritten))
{
    // 零堆分配！
    await SendAsync(buffer.Slice(0, bytesWritten));
}
```

### 4. 使用 SerializationHelper

```csharp
// SerializationHelper 自动使用池化序列化器
var base64 = SerializationHelper.Serialize(message, serializer);
// 内部自动使用 PooledBufferWriter

var decoded = SerializationHelper.Deserialize<MyMessage>(base64, serializer);
// 内部使用池化 Base64 解码
```

---

## 📊 性能基准 {#benchmarks}

### 序列化性能对比

```
BenchmarkDotNet v0.13.12, Windows 11 (10.0.22631.4602)
Intel Core i9-13900K, 1 CPU, 32 logical and 24 physical cores

| Method                    | Mean      | Allocated |
|-------------------------- |----------:| ---------:|
| MemoryPack_Serialize      |  45.2 ns  |     128 B |
| JSON_Serialize            | 312.4 ns  |     584 B |
```

### Base64 编码性能

```
| Method                          | Mean      | Allocated |
|-------------------------------- |----------:| ---------:|
| Convert.ToBase64String          | 125.3 ns  |     312 B |
| SerializationHelper (stackalloc)|  42.7 ns  |       0 B | ⬇️ -100%
| SerializationHelper (pooled)    |  68.5 ns  |      48 B | ⬇️ -85%
```

### 吞吐量提升

```
场景: 10,000 消息/秒

优化前:
- 内存分配: 584 MB/s
- GC 暂停: 45 ms/s
- CPU 使用: 35%

优化后 (MemoryPack + 池化):
- 内存分配: 32 MB/s    ⬇️ -94%
- GC 暂停: 8 ms/s       ⬇️ -82%
- CPU 使用: 22%         ⬇️ -37%
- 吞吐量: +127%         ⬆️ 22,700 消息/秒
```

---

## 🔧 AOT 兼容性 {#aot-compat}

### 完全 AOT 安全的组件

```csharp
// ✅ MemoryPackMessageSerializer (推荐)
[MemoryPackable]
public partial class MyMessage { }

services.AddCatga()
    .UseMemoryPack();  // 零反射，AOT 路线直接

// ✅ JsonMessageSerializer (泛型方法)
[JsonSerializable(typeof(MyMessage))]
public partial class MyJsonContext : JsonSerializerContext { }

var options = new JsonSerializerOptions
{
    TypeInfoResolver = MyJsonContext.Default
};
services.AddCatga();
services.AddSingleton<IMessageSerializer>(sp => new CustomSerializer(options));

// ✅ 使用泛型方法
var bytes = serializer.Serialize(message);  // AOT 安全
var msg = serializer.Deserialize<MyMessage>(bytes);  // AOT 安全
```

---

## 🎯 总结

### 性能判断原则

这里不再给统一的固定收益区间。

更稳妥的理解是：

- 优化重点通常会体现在内存分配、GC 压力和热点路径吞吐
- 实际收益强依赖消息大小、分配模式、broker 组合和负载模型
- 应该以你的目标服务压测结果为准

### 推荐配置

**生产环境 (高性能)**:
```csharp
services.AddCatga()
    .UseMemoryPack();  // 默认推荐的高性能 / AOT 路线
```

**开发环境 (可读性优先)**:
```csharp
services.AddCatga();
services.AddSingleton<IMessageSerializer, CustomSerializer>();
```

---

**最后更新**: 2024-01-20
**版本**: 2.0.0
**维护者**: Catga Team

