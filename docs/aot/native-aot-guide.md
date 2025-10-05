# Catga NativeAOT 完全指南

## 📖 目录

1. [什么是 NativeAOT](#什么是-nativeaot)
2. [为什么使用 NativeAOT](#为什么使用-nativeaot)
3. [Catga 的 AOT 支持](#catga-的-aot-支持)
4. [快速开始](#快速开始)
5. [项目配置](#项目配置)
6. [消息类型定义](#消息类型定义)
7. [高级配置](#高级配置)
8. [性能优化](#性能优化)
9. [常见问题](#常见问题)
10. [最佳实践](#最佳实践)

---

## 什么是 NativeAOT

**NativeAOT (Native Ahead-of-Time)** 是 .NET 的一种编译模式，它将 .NET 应用程序提前编译为原生机器码，而不是在运行时通过 JIT (Just-In-Time) 编译。

### 对比

| 特性 | JIT | NativeAOT |
|------|-----|-----------|
| **启动时间** | 慢 (~200ms) | 极快 (~5ms) |
| **内存占用** | 高 (~40MB+) | 低 (~15MB) |
| **二进制大小** | 小 (需要 .NET Runtime) | 中 (自包含) |
| **部署** | 需要安装 .NET | 单文件，无依赖 |
| **反射** | 完全支持 | 受限 |
| **性能** | 很好 | 更好 |

---

## 为什么使用 NativeAOT

### ✅ 适用场景

1. **微服务/无服务器 (Serverless)**
   - 快速冷启动
   - 低内存占用
   - 降低运行成本

2. **容器化应用**
   - 更小的镜像大小
   - 快速扩缩容
   - 更好的资源利用率

3. **边缘计算**
   - 资源受限环境
   - 快速响应时间
   - 离线运行

4. **CLI 工具**
   - 即时启动
   - 单文件分发
   - 无需安装 .NET

### ❌ 不适用场景

1. **大量使用反射的应用**
2. **需要动态代码生成**
3. **依赖运行时编译的框架**
4. **需要插件系统**

---

## Catga 的 AOT 支持

Catga 框架从设计之初就考虑了 AOT 兼容性：

### ✅ 完全支持

| 功能 | AOT 状态 | 说明 |
|------|---------|------|
| **CQRS** | ✅ 完全支持 | 零反射 |
| **Mediator** | ✅ 完全支持 | 编译时注册 |
| **Pipeline** | ✅ 完全支持 | 静态类型 |
| **依赖注入** | ✅ 完全支持 | MS DI 原生支持 |
| **结果类型** | ✅ 完全支持 | 值类型优化 |
| **日志** | ✅ 完全支持 | 标准 ILogger |
| **NATS** | ✅ 完全支持 | JSON 源生成 |
| **Redis** | ✅ 完全支持 | StackExchange.Redis AOT 兼容 |
| **Outbox/Inbox** | ✅ 完全支持 | 无反射序列化 |

### 📊 AOT 警告

- **Catga**: 0 个警告 ✅
- **Catga.Redis**: 0 个警告 ✅
- **Catga.Nats**: 12 个警告 ⚠️
  - 10 个来自 .NET 框架（不可控）
  - 2 个来自 fallback resolver（可选消除）

---

## 快速开始

### 1. 创建项目

```bash
dotnet new console -n MyAotApp
cd MyAotApp
```

### 2. 添加 Catga 引用

```bash
dotnet add package Catga
dotnet add package Catga.Nats  # 如果需要 NATS
dotnet add package Catga.Redis # 如果需要 Redis
```

### 3. 配置项目文件

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>

    <!-- 启用 Native AOT -->
    <PublishAot>true</PublishAot>

    <!-- 可选：优化配置 -->
    <IlcOptimizationPreference>Speed</IlcOptimizationPreference>
    <TrimMode>full</TrimMode>
  </PropertyGroup>
</Project>
```

### 4. 定义消息和 JSON 上下文

```csharp
using System.Text.Json.Serialization;
using Catga.Messages;
using Catga.Results;

// 消息定义
public record CreateOrderCommand : ICommand<int>
{
    public required string ProductId { get; init; }
    public required int Quantity { get; init; }
}

// JSON 源生成上下文 (AOT 必需)
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CreateOrderCommand))]
[JsonSerializable(typeof(CatgaResult<int>))]
[JsonSerializable(typeof(CatgaResult))]
public partial class MyAppJsonContext : JsonSerializerContext { }
```

### 5. 配置和使用

```csharp
using Catga.DependencyInjection;
using Catga.Nats.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// 注册 Catga
builder.Services.AddCatga();
builder.Services.AddRequestHandler<CreateOrderCommand, int, CreateOrderHandler>();

// 配置 JSON 序列化（消除 AOT 警告）
NatsJsonSerializer.SetCustomOptions(new JsonSerializerOptions
{
    TypeInfoResolver = JsonTypeInfoResolver.Combine(
        MyAppJsonContext.Default,
        NatsCatgaJsonContext.Default
    )
});

var app = builder.Build();

// 使用
var mediator = app.Services.GetRequiredService<ICatgaMediator>();
var result = await mediator.SendAsync(new CreateOrderCommand
{
    ProductId = "P001",
    Quantity = 5
});

Console.WriteLine($"Order created: {result.Value}");
```

### 6. 发布 AOT 版本

```bash
# Windows
dotnet publish -c Release -r win-x64

# Linux
dotnet publish -c Release -r linux-x64

# macOS (Intel)
dotnet publish -c Release -r osx-x64

# macOS (ARM/M1/M2)
dotnet publish -c Release -r osx-arm64
```

### 7. 运行

```bash
# Windows
.\bin\Release\net9.0\win-x64\publish\MyAotApp.exe

# Linux/macOS
./bin/Release/net9.0/linux-x64/publish/MyAotApp
```

---

## 项目配置

### 基本配置

```xml
<PropertyGroup>
  <!-- 启用 Native AOT -->
  <PublishAot>true</PublishAot>

  <!-- 裁剪模式 -->
  <TrimMode>full</TrimMode>

  <!-- 启用裁剪分析器 -->
  <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
</PropertyGroup>
```

### 优化配置

```xml
<PropertyGroup>
  <!-- 优化目标：Speed (速度) 或 Size (大小) -->
  <IlcOptimizationPreference>Speed</IlcOptimizationPreference>

  <!-- 禁用堆栈跟踪生成（减小体积） -->
  <IlcGenerateStackTraceData>false</IlcGenerateStackTraceData>

  <!-- 禁用异常消息（进一步减小体积） -->
  <IlcGenerateCompleteTypeMetadata>false</IlcGenerateCompleteTypeMetadata>

  <!-- 全球化设置 -->
  <InvariantGlobalization>true</InvariantGlobalization> <!-- 减小 30MB+ -->
</PropertyGroup>
```

### 调试配置

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Debug'">
  <!-- 在 Debug 模式下禁用 AOT，加快开发迭代 -->
  <PublishAot>false</PublishAot>
</PropertyGroup>
```

---

## 消息类型定义

### 1. 定义消息

```csharp
// Command (有返回值)
public record CreateUserCommand : ICommand<Guid>
{
    public required string Name { get; init; }
    public required string Email { get; init; }
}

// Query
public record GetUserQuery : IQuery<UserDto>
{
    public required Guid UserId { get; init; }
}

// Event
public record UserCreatedEvent : IEvent
{
    public required Guid UserId { get; init; }
    public required string Name { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

// DTO
public record UserDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Email { get; init; }
}
```

### 2. 定义 JSON 上下文

```csharp
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Default)]

// Commands
[JsonSerializable(typeof(CreateUserCommand))]

// Queries
[JsonSerializable(typeof(GetUserQuery))]

// Events
[JsonSerializable(typeof(UserCreatedEvent))]

// DTOs
[JsonSerializable(typeof(UserDto))]

// Results (重要!)
[JsonSerializable(typeof(CatgaResult<Guid>))]
[JsonSerializable(typeof(CatgaResult<UserDto>))]
[JsonSerializable(typeof(CatgaResult))]

public partial class AppJsonContext : JsonSerializerContext { }
```

### 3. 注册上下文

```csharp
using Catga.Nats.Serialization;

NatsJsonSerializer.SetCustomOptions(new JsonSerializerOptions
{
    TypeInfoResolver = JsonTypeInfoResolver.Combine(
        AppJsonContext.Default,           // 你的类型
        NatsCatgaJsonContext.Default      // Catga 框架类型
    )
});
```

---

## 高级配置

### 多项目 AOT 支持

对于多项目解决方案：

```
Solution/
├── MyApp.Core/          (库项目)
│   ├── IsAotCompatible = true
│   └── IsTrimmable = true
├── MyApp.Application/   (库项目)
│   ├── IsAotCompatible = true
│   └── IsTrimmable = true
└── MyApp/               (可执行项目)
    └── PublishAot = true
```

```xml
<!-- MyApp.Core.csproj -->
<PropertyGroup>
  <IsAotCompatible>true</IsAotCompatible>
  <IsTrimmable>true</IsTrimmable>
  <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
</PropertyGroup>

<!-- MyApp.csproj -->
<PropertyGroup>
  <PublishAot>true</PublishAot>
  <TrimMode>full</TrimMode>
</PropertyGroup>
```

### 动态根 (Dynamic Roots)

如果需要保留某些类型不被裁剪：

```xml
<ItemGroup>
  <TrimmerRootAssembly Include="MyAssembly" />
  <TrimmerRootDescriptor Include="TrimmerRoots.xml" />
</ItemGroup>
```

`TrimmerRoots.xml`:
```xml
<linker>
  <assembly fullname="MyApp">
    <type fullname="MyApp.MyType" preserve="all" />
  </assembly>
</linker>
```

### 条件编译

```csharp
#if NET9_0_OR_GREATER && TRIMMING
    // AOT/Trimming 特定代码
#else
    // 传统 JIT 代码
#endif
```

---

## 性能优化

### 1. 启动时间优化

```xml
<IlcOptimizationPreference>Speed</IlcOptimizationPreference>
```

### 2. 大小优化

```xml
<!-- 减小二进制大小约 30-50% -->
<IlcOptimizationPreference>Size</IlcOptimizationPreference>
<IlcGenerateStackTraceData>false</IlcGenerateStackTraceData>
<InvariantGlobalization>true</InvariantGlobalization>
```

### 3. 内存优化

```csharp
// 使用 struct 而非 class
public readonly record struct UserId(Guid Value);

// 使用 Span<T>
public void ProcessData(ReadOnlySpan<byte> data) { }

// 使用对象池
private static readonly ObjectPool<StringBuilder> StringBuilderPool = ...;
```

### 4. 分层编译

```xml
<!-- 对非热路径使用更小的代码 -->
<IlcInstructionSet>native</IlcInstructionSet>
```

---

## 常见问题

### Q: 编译失败，提示 IL2XXX 警告
**A**: 确保所有序列化类型都在 `JsonSerializerContext` 中注册。

### Q: 运行时抛出 NotSupportedException
**A**: 检查是否使用了反射 API，改用源生成器或编译时已知类型。

### Q: 二进制文件太大 (>50MB)
**A**:
```xml
<InvariantGlobalization>true</InvariantGlobalization>
<IlcOptimizationPreference>Size</IlcOptimizationPreference>
<IlcGenerateStackTraceData>false</IlcGenerateStackTraceData>
```

### Q: 启动时间没有明显提升
**A**:
- 检查是否真的发布为 AOT (`PublishAot>true</PublishAot>`)
- 确认使用 Release 配置 (`-c Release`)
- 检查是否有大量的静态初始化代码

### Q: 如何调试 AOT 应用
**A**:
```bash
# 使用 Debug 配置（禁用 AOT）
dotnet run

# 或启用符号
dotnet publish -c Release -r win-x64 /p:DebugType=embedded /p:DebugSymbols=true
```

---

## 最佳实践

### ✅ DO

1. **使用 `record` 定义消息**
   ```csharp
   public record MyCommand : ICommand<int> { }
   ```

2. **定义完整的 JSON 上下文**
   ```csharp
   [JsonSerializable(typeof(MyCommand))]
   [JsonSerializable(typeof(CatgaResult<int>))]
   public partial class AppJsonContext : JsonSerializerContext { }
   ```

3. **使用构造函数注入**
   ```csharp
   public class MyHandler
   {
       private readonly ILogger _logger;
       public MyHandler(ILogger<MyHandler> logger) => _logger = logger;
   }
   ```

4. **尽早验证 AOT 兼容性**
   ```bash
   dotnet publish -c Release -r win-x64
   ```

5. **使用 Span<T> 和 Memory<T>**
   ```csharp
   public void Process(ReadOnlySpan<byte> data) { }
   ```

### ❌ DON'T

1. **不要使用反射**
   ```csharp
   // ❌ 错误
   Type.GetType("MyType").GetMethod("MyMethod").Invoke(...)

   // ✅ 正确：使用静态类型
   var handler = serviceProvider.GetRequiredService<IMyHandler>();
   ```

2. **不要使用动态类型**
   ```csharp
   // ❌ 错误
   dynamic obj = GetObject();
   obj.DoSomething();

   // ✅ 正确：使用接口或基类
   IMyInterface obj = GetObject();
   obj.DoSomething();
   ```

3. **不要忘记注册所有序列化类型**
   ```csharp
   // ❌ 错误：缺少 Result 类型
   [JsonSerializable(typeof(MyCommand))]

   // ✅ 正确：包含所有类型
   [JsonSerializable(typeof(MyCommand))]
   [JsonSerializable(typeof(CatgaResult<int>))]
   ```

4. **不要在热路径使用 LINQ**
   ```csharp
   // ❌ 较慢
   var result = items.Where(x => x.IsValid).Select(x => x.Value).ToList();

   // ✅ 更快
   var result = new List<int>(items.Count);
   foreach (var item in items)
   {
       if (item.IsValid) result.Add(item.Value);
   }
   ```

---

## 📚 参考资源

- [.NET Native AOT 部署](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
- [JSON 源生成器](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/source-generation)
- [Trimming 选项](https://learn.microsoft.com/dotnet/core/deploying/trimming/trimming-options)
- [AOT 兼容性要求](https://learn.microsoft.com/dotnet/core/deploying/native-aot/compatibility)

---

## 🎯 总结

Catga 框架提供了一流的 NativeAOT 支持：

| 特性 | 状态 |
|------|------|
| ✅ 零反射设计 | 完全支持 |
| ✅ JSON 源生成 | 完全支持 |
| ✅ 编译时注册 | 完全支持 |
| ✅ 裁剪友好 | 完全支持 |
| ✅ 文档完善 | 完全支持 |

**使用 Catga + NativeAOT，构建快速、高效、现代化的云原生应用！** 🚀

