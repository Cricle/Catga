# Catga Native AOT 发布指南

## 概述

本指南将帮助你将 Catga 应用发布为 Native AOT 二进制文件，获得：
- 🚀 更快的启动
- 💾 更小的部署产物
- ⚡ 更低的运行时开销
- 🔒 **更高的安全性**（无JIT，代码完全预编译）

## 前置要求

### 开发环境

- **.NET 10.0 SDK** 或更高版本
- **C++ 编译工具链**：
  - Windows: Visual Studio 2022 (含 C++ 桌面开发工作负载)
  - Linux: GCC 或 Clang
  - macOS: Xcode Command Line Tools

### 验证环境

```bash
# 验证 .NET SDK
dotnet --version  # 应该是 10.0.0 或更高

# Windows: 验证 Visual Studio C++ 工具
where cl.exe

# Linux: 验证 GCC
gcc --version

# macOS: 验证 Clang
clang --version
```

## 快速开始

### 1. 配置项目文件

在你的 `.csproj` 文件中添加：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>

    <!-- Enable Native AOT -->
    <PublishAot>true</PublishAot>

    <!-- Optional: Trim unused code -->
    <PublishTrimmed>true</PublishTrimmed>
    <TrimMode>full</TrimMode>

    <!-- Optional: Optimize for size -->
    <IlcOptimizationPreference>Size</IlcOptimizationPreference>
    <!-- Or optimize for speed -->
    <!-- <IlcOptimizationPreference>Speed</IlcOptimizationPreference> -->

    <!-- Optional: Include symbols for debugging -->
    <DebugType>embedded</DebugType>
    <DebugSymbols>true</DebugSymbols>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Catga" Version="x.y.z" />
    <PackageReference Include="Catga.Serialization.MemoryPack" Version="x.y.z" />
    <PackageReference Include="Catga.Transport.InMemory" Version="x.y.z" />
  </ItemGroup>
</Project>
```

### 2. 确保代码 AOT 兼容

#### ✅ 使用当前推荐的最小 AOT 路线

```csharp
var catga = services.AddCatga()
    .UseMemoryPack()
    .UseInMemory()
    .AddHostedServices();

services.AddInMemoryTransport();
```

#### ✅ 使用 MemoryPack 序列化

```csharp
// 标记你的消息类型
[MemoryPackable]
public partial class CreateOrderCommand : IRequest<OrderResult>
{
    public string OrderId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

// 配置
services.AddCatga()
    .UseMemoryPack();  // AOT 友好的序列化器
```

#### ✅ 使用生产级实现

```csharp
services.AddCatga()
    .UseMemoryPack()
    .UseRedis("localhost:6379")
    .ForProduction()
    .UseInbox()
    .UseOutbox()
    .UseDeadLetterQueue()
    .AddHostedServices();

services.AddRedisTransport("localhost:6379");
```

### 3. 发布为 Native AOT

```bash
# Windows (x64)
dotnet publish -c Release -r win-x64

# Linux (x64)
dotnet publish -c Release -r linux-x64

# macOS (ARM64, Apple Silicon)
dotnet publish -c Release -r osx-arm64

# 输出位置
# bin/Release/net10.0/{runtime}/publish/
```

发布后的文件结构：
```
publish/
├── YourApp.exe (或 YourApp)  ← 单个可执行文件
└── YourApp.pdb (可选，调试符号)
```

### 4. 运行和测试

```bash
# Windows
.\bin\Release\net10.0\win-x64\publish\YourApp.exe

# Linux / macOS
./bin/Release/net10.0/linux-x64/publish/YourApp

# 查看文件大小
ls -lh bin/Release/net10.0/*/publish/
```

## 高级配置

### 优化文件大小

```xml
<PropertyGroup>
  <!-- 启用所有优化 -->
  <PublishAot>true</PublishAot>
  <PublishTrimmed>true</PublishTrimmed>
  <TrimMode>full</TrimMode>

  <!-- 优化设置 -->
  <IlcOptimizationPreference>Size</IlcOptimizationPreference>
  <IlcGenerateStackTraceData>false</IlcGenerateStackTraceData>

  <!-- 不变globalization (减小10-20MB) -->
  <InvariantGlobalization>true</InvariantGlobalization>

  <!-- 移除不需要的功能 -->
  <EventSourceSupport>false</EventSourceSupport>
  <HttpActivityPropagationSupport>false</HttpActivityPropagationSupport>
  <EnableUnsafeBinaryFormatterSerialization>false</EnableUnsafeBinaryFormatterSerialization>
</PropertyGroup>
```

### 优化启动性能

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
  <PublishTrimmed>true</PublishTrimmed>

  <!-- 优化速度 -->
  <IlcOptimizationPreference>Speed</IlcOptimizationPreference>

  <!-- 启用 PGO (Profile-Guided Optimization) -->
  <IlcPgoOptimize>true</IlcPgoOptimize>

  <!-- 保留堆栈跟踪 -->
  <IlcGenerateStackTraceData>true</IlcGenerateStackTraceData>
</PropertyGroup>
```

### 跨平台发布

```bash
# 一次性发布到多个平台
dotnet publish -c Release -r win-x64 -o ./dist/win-x64
dotnet publish -c Release -r linux-x64 -o ./dist/linux-x64
dotnet publish -c Release -r osx-arm64 -o ./dist/osx-arm64
```

## 常见问题排查

### 问题 1: IL2026/IL3050 警告

**症状**：
```
warning IL2026: Using member 'X' which has 'RequiresUnreferencedCodeAttribute'
```

**原因**: 使用了反射或动态代码生成

**解决方案**:
1. 优先使用 `UseMemoryPack()`
2. 避免依赖运行时反射扫描
3. 先用 `UseInMemory() + AddInMemoryTransport()` 跑通 AOT，再切生产 broker

### 问题 2: 编译失败 "native toolchain not found"

**症状**：
```
error : Native toolchain cannot be found
```

**解决方案**:
- **Windows**: 安装 Visual Studio 2022 + C++ 桌面开发工作负载
- **Linux**: `sudo apt-get install clang zlib1g-dev`
- **macOS**: `xcode-select --install`

### 问题 3: 文件体积过大

**症状**: 发布的文件 > 50MB

**解决方案**:
1. 启用 `InvariantGlobalization` (如果不需要国际化)
2. 使用 `IlcOptimizationPreference=Size`
3. 禁用不需要的功能 (见"优化文件大小"部分)
4. 检查是否包含了不必要的依赖

### 问题 4: 运行时崩溃或异常

**症状**: 发布版本崩溃，但 Debug 模式正常

**排查步骤**:
1. 启用调试符号：`<DebugType>embedded</DebugType>`
2. 保留堆栈跟踪：`<IlcGenerateStackTraceData>true</IlcGenerateStackTraceData>`
3. 检查是否有反射使用
4. 使用 `dotnet publish` 的 `-v:detailed` 选项查看详细输出

## 性能说明

Native AOT 的实际收益高度依赖：

- 你的依赖树
- 是否是 ASP.NET Core 宿主
- trimming 配置
- serializer 与 transport/persistence 组合

因此这里不再给固定倍数承诺。实际收益请在你的目标应用上实测。

相关文档：

- [Benchmark Results](../BENCHMARK-RESULTS.md)
- [AOT 部署说明](../articles/aot-deployment.md)
- [序列化 AOT 指南](../aot/serialization-aot-guide.md)

## 最佳实践

### 1. 开发与生产分离

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCatgaConfiguration(this IServiceCollection services)
    {
#if AOT_BUILD
        // 生产 AOT 配置
        return services.AddCatga()
            .UseMemoryPack();
#else
        // 开发配置 (更灵活)：示例使用自定义 JSON 序列化器手动注册
        services.AddCatga();
        services.AddSingleton<IMessageSerializer, CustomSerializer>();
        return services;
#endif
    }
}
```

### 2. 条件编译

在 `.csproj` 中定义：
```xml
<PropertyGroup Condition="'$(Configuration)' == 'Release' and '$(PublishAot)' == 'true'">
  <DefineConstants>$(DefineConstants);AOT_BUILD</DefineConstants>
</PropertyGroup>
```

### 3. CI/CD 集成

**GitHub Actions**:
```yaml
- name: Publish Native AOT
  run: |
    dotnet publish -c Release -r linux-x64 \
      /p:PublishAot=true \
      /p:PublishTrimmed=true \
      /p:IlcOptimizationPreference=Speed

- name: Upload artifact
  uses: actions/upload-artifact@v3
  with:
    name: app-native-aot
    path: bin/Release/net10.0/linux-x64/publish/
```

### 4. Docker 容器

**Dockerfile**:
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -r linux-x64 /p:PublishAot=true -o /app

FROM mcr.microsoft.com/dotnet/runtime-deps:9.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["./YourApp"]
```

**优势**:
- 容器镜像通常更精简
- 启动通常更快
- 无需 .NET runtime

## 验证 AOT 编译

### 检查是否真的是 Native AOT

```csharp
using System.Runtime.CompilerServices;

if (!RuntimeFeature.IsDynamicCodeSupported)
{
    Console.WriteLine("✅ Running as Native AOT");
}
else
{
    Console.WriteLine("❌ Running as traditional .NET");
}
```

### 性能测试

```bash
# 启动时间
time ./YourApp --version

# 内存占用
dotnet-trace collect --process-id $(pidof YourApp)

# 文件大小
ls -lh YourApp
```

## 资源

- [Catga AOT 序列化指南](../aot/serialization-aot-guide.md)
- [源生成器使用指南](../guides/source-generator.md)
- [.NET Native AOT 官方文档](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
- [性能基准测试](../BENCHMARK-RESULTS.md)

## 总结

Catga 为 Native AOT 提供了清晰的落地路径：

✅ **优先推荐 `UseMemoryPack()`**
✅ **可先用 `UseInMemory()` 验证最小 AOT 路线**
✅ **再切换到 Redis / NATS 等生产组合**
✅ **文档已覆盖发布、序列化和排障路径**

下一步应该是：在你的目标宿主、发布参数和 broker 组合上做一次真实发布与实测。
