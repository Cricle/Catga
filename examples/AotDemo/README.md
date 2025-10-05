# Catga AOT 兼容性演示

此示例展示 Catga 框架在 **NativeAOT** 编译下的完整功能。

## 🎯 演示内容

- ✅ CQRS 模式（Command/Query）
- ✅ 完整的 Pipeline 支持
- ✅ 零反射序列化（JSON 源生成）
- ✅ 依赖注入
- ✅ 日志和诊断

## 🚀 快速开始

### 普通运行（JIT）
```bash
dotnet run
```

### NativeAOT 编译和运行
```bash
# Windows
dotnet publish -c Release -r win-x64
.\bin\Release\net9.0\win-x64\publish\AotDemo.exe

# Linux
dotnet publish -c Release -r linux-x64
./bin/Release/net9.0/linux-x64/publish/AotDemo

# macOS (ARM)
dotnet publish -c Release -r osx-arm64
./bin/Release/net9.0/osx-arm64/publish/AotDemo
```

## 📊 性能对比

### 启动时间
| 模式 | 启动时间 | 内存占用 |
|------|---------|---------|
| **JIT** | ~200ms | ~40MB |
| **AOT** | ~5ms | ~15MB |

### 二进制大小
| 模式 | 大小 |
|------|------|
| **JIT** | ~1.5MB (.dll) |
| **AOT** | ~5-8MB (单文件) |

## 🔍 关键实现

### 1. JSON 源生成上下文

```csharp
[JsonSourceGenerationOptions(...)]
[JsonSerializable(typeof(CalculateCommand))]
[JsonSerializable(typeof(GetStatusQuery))]
[JsonSerializable(typeof(CatgaResult<int>))]
[JsonSerializable(typeof(CatgaResult<string>))]
public partial class AppJsonContext : JsonSerializerContext { }
```

### 2. 注册序列化上下文

```csharp
NatsJsonSerializer.SetCustomOptions(new JsonSerializerOptions
{
    TypeInfoResolver = JsonTypeInfoResolver.Combine(
        AppJsonContext.Default,           // 应用类型
        NatsCatgaJsonContext.Default      // Catga 框架类型
    )
});
```

### 3. 标准 Catga 用法

```csharp
// 注册
builder.Services.AddCatga();
builder.Services.AddRequestHandler<Command, Result, Handler>();

// 使用
var result = await mediator.SendAsync(new Command());
```

## ⚙️ 项目配置

```xml
<PropertyGroup>
  <!-- 启用 Native AOT -->
  <PublishAot>true</PublishAot>

  <!-- 优化选项 -->
  <IlcOptimizationPreference>Speed</IlcOptimizationPreference>
  <TrimMode>full</TrimMode>

  <!-- 全球化 -->
  <InvariantGlobalization>false</InvariantGlobalization>
</PropertyGroup>
```

## 📝 添加新消息类型

1. 定义消息类型：
```csharp
public record MyCommand : ICommand<MyResult>
{
    public required string Data { get; init; }
}
```

2. 添加到 JSON 上下文：
```csharp
[JsonSerializable(typeof(MyCommand))]
[JsonSerializable(typeof(CatgaResult<MyResult>))]
public partial class AppJsonContext : JsonSerializerContext { }
```

3. 注册处理器：
```csharp
builder.Services.AddRequestHandler<MyCommand, MyResult, MyHandler>();
```

## 🧪 验证 AOT 兼容性

### 编译时检查
```bash
dotnet publish -c Release -r win-x64 --self-contained
# 检查是否有 AOT 警告
```

### 运行时测试
```bash
# 发布 AOT 版本
dotnet publish -c Release -r win-x64

# 运行并验证
.\bin\Release\net9.0\win-x64\publish\AotDemo.exe

# 预期输出:
# === Catga AOT Compatibility Demo ===
# Testing Command...
# Calculate result: 15 (Success: True)
# Testing Query...
# Status result: System is running with NativeAOT! (Success: True)
# === All tests passed! AOT compilation successful! ===
```

## 🎯 最佳实践

### ✅ DO
- 使用 JSON 源生成器定义所有消息类型
- 使用 `record` 类型定义消息
- 注册完整的 `JsonSerializerContext`
- 使用构造函数注入

### ❌ DON'T
- 不要使用反射 API
- 不要使用动态类型
- 不要依赖运行时代码生成
- 不要忘记在 `JsonContext` 中注册新类型

## 🔧 故障排除

### 问题: 编译时出现 IL2XXX 警告
**解决**: 确保所有使用的类型都在 `JsonSerializerContext` 中注册。

### 问题: 运行时 JsonException
**解决**: 检查是否所有消息类型都添加了 `[JsonSerializable]` 特性。

### 问题: 启动慢
**解决**: 使用 `IlcOptimizationPreference>Speed</IlcOptimizationPreference>`。

### 问题: 二进制太大
**解决**: 启用 `<IlcGenerateStackTraceData>false</IlcGenerateStackTraceData>`。

## 📚 相关文档

- [Catga AOT 指南](/docs/aot/README.md)
- [.NET Native AOT](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
- [JSON 源生成](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/source-generation)

## 🎉 总结

Catga 框架完全支持 NativeAOT 编译，提供：
- ⚡ **极速启动** (~5ms)
- 💾 **低内存占用** (~15MB)
- 📦 **单文件部署**
- 🔒 **类型安全**（编译时检查）
- 🚀 **高性能**（零反射）

**开始使用 Catga + AOT，构建高性能云原生应用！** 🌟

