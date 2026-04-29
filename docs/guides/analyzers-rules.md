# Catga 分析器规则参考

> 这一页专门放规则说明、触发示例和误用修复。
> 如果你在找仓库配置、CI 接入、故障排查，请看 [analyzers-configuration.md](./analyzers-configuration.md)。

---

## 先看什么

如果你只关心当前最容易踩坑的规则，优先看：

1. `CATGA002`：缺少序列化器注册
2. `CAT2004`：singleton 依赖 scoped Catga 服务

---

## 🎯 为什么需要分析器？

**传统方式的问题**:
```csharp
// ❌ 运行时才发现错误
services.AddCatga();  // 忘记注册序列化器
var result = await mediator.SendAsync<CreateOrder, OrderResult>(cmd);
// 💥 运行时异常: IMessageSerializer not registered
```

**使用分析器**:
```csharp
// 编译时就发现错误
services.AddCatga();  // ← 编译警告: CATGA002
//              ^^^^^
// 调用 .UseMemoryPack() 或手动注册 IMessageSerializer

// 修复后
services.AddCatga().UseMemoryPack();  // 编译通过
```

**收益**:
- ✅ **编译时发现** - 90% 的配置错误在编译时捕获
- ✅ **自动修复** - 一键应用建议的修复
- ✅ **持续集成** - CI/CD 中自动检查
- ✅ **团队协作** - 统一的代码质量标准

---

## 📦 安装

### 自动包含（推荐）

如果使用 `Catga.SourceGenerator`，分析器已自动包含：

```bash
dotnet add package Catga.SourceGenerator
```

**验证**:
```bash
dotnet build
# 分析器会自动运行
```

### 项目引用方式

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\Catga.SourceGenerator\Catga.SourceGenerator.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

---

## 🆕 新增分析器 (v2.0)

### CATGA001: 缺少 [MemoryPackable] 属性

**严重性**: Info
**类别**: AOT 兼容性
**首次引入**: v2.0

#### 描述

检测实现 `IRequest` 或 `IEvent` 的消息类型，但未标注 `[MemoryPackable]` 属性。

#### 为什么需要？

MemoryPack 是推荐的 AOT 序列化器，所有消息类型都应标注 `[MemoryPackable]` 以获得：
- ✅ 100% AOT 兼容
- ✅ 5x 性能提升
- ✅ 40% 更小的 payload

#### 示例

**触发警告**:
```csharp
// ❌ CATGA001: 缺少 [MemoryPackable]
public record CreateOrder(string OrderId, decimal Amount)
    : IRequest<OrderResult>;
//              ^^^^^^^^^^^
// 💡 添加 [MemoryPackable] 以获得最佳 AOT 性能
```

**修复方式**:
```csharp
// ✅ 正确
[MemoryPackable]
public partial record CreateOrder(string OrderId, decimal Amount)
    : IRequest<OrderResult>;
```

#### 自动修复

IDE 会提供自动修复选项：
1. 添加 `[MemoryPackable]` 属性
2. 添加 `partial` 关键字
3. 添加 `using MemoryPack;`

**快捷键**:
- Visual Studio: `Ctrl + .` 或 `Alt + Enter`
- VS Code: `Ctrl + .`
- Rider: `Alt + Enter`

#### 配置

如果不想看到此警告（例如使用 JSON），可以抑制：

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);CATGA001</NoWarn>
</PropertyGroup>
```

或使用 `.editorconfig`:
```ini
[*.cs]
dotnet_diagnostic.CATGA001.severity = none
```

---

### CATGA002: 缺少序列化器注册

**严重性**: Warning
**类别**: 配置
**首次引入**: v2.0

#### 描述

检测调用 `AddCatga()` 但未链式调用 `.UseMemoryPack()` 或未手动注册 `IMessageSerializer`。

#### 为什么需要？

Catga 需要 `IMessageSerializer` 才能工作，忘记注册会导致运行时异常。

#### 示例

**触发警告**:
```csharp
// ❌ CATGA002: 缺少序列化器注册
services.AddCatga();
//              ^^^^^
// 💡 调用 .UseMemoryPack() 或手动注册 IMessageSerializer
```

**修复方式**:
```csharp
// ✅ 方式 1: MemoryPack (推荐)
services.AddCatga().UseMemoryPack();

// ✅ 方式 2: 手动注册自定义序列化器（例如 System.Text.Json 实现）
services.AddCatga();
services.AddSingleton<IMessageSerializer, CustomSerializer>();
```

#### 自动修复

IDE 会提供自动修复选项：
1. 添加 `.UseMemoryPack()` (推荐)
2. 生成 `IMessageSerializer` 手动注册模板

#### 检测范围

分析器会在以下情况检查：
- ✅ 同一方法内
- ✅ 链式调用
- ❌ 跨方法调用（限制）

```csharp
// ✅ 同一方法 - 检测到
public void ConfigureServices(IServiceCollection services)
{
    services.AddCatga();  // ← 警告
}

// ✅ 链式调用 - 检测到
services.AddCatga()
    .UseMemoryPack();  // ← 无警告

// ⚠️ 跨方法 - 可能检测不到
public void ConfigureServices(IServiceCollection services)
{
    services.AddCatga();  // ← 可能警告
    RegisterSerializer(services);  // 跨方法
}

void RegisterSerializer(IServiceCollection services)
{
    services.AddSingleton<IMessageSerializer, ...>();
}
```

---

### CAT2004: Singleton Catga 注册依赖 Scoped 服务

**严重性**: Error
**类别**: 依赖注入 / 生命周期
**首次引入**: 2026-04-29

#### 描述

检测以下高风险误用：
- `services.AddSingleton<IRequestHandler<...>, ...>()`
- `services.AddSingleton<IEventHandler<...>, ...>()`
- `services.AddSingleton(typeof(IPipelineBehavior<,>), ...)`
- `services.AddSingleton<IFlowExecutor, ...>()`
- `[CatgaLifetime(ServiceLifetime.Singleton)]`

当这些 singleton 注册的实现类型构造函数**直接依赖**以下 scoped Catga 服务时，会在编译期报错：
- `ICatgaMediator`
- `IFlowExecutor`
- `IFlow<T>`

#### 为什么需要？

这类问题以前通常要到应用启动时才会炸：
- 宿主启动失败
- DI 校验报 `singleton -> scoped`
- 示例项目或生产环境才暴露

现在会尽量前移到编译期。

#### 示例

**触发错误**:
```csharp
services.AddSingleton<IRequestHandler<CreateOrder, string>, BadHandler>();

public sealed class BadHandler(ICatgaMediator mediator)
    : IRequestHandler<CreateOrder, string>
{
    public ValueTask<CatgaResult<string>> HandleAsync(CreateOrder request, CancellationToken ct = default)
        => new(CatgaResult<string>.Success("ok"));
}
```

**修复方式**:
```csharp
services.AddScoped<IRequestHandler<CreateOrder, string>, GoodHandler>();
```

或：
```csharp
[CatgaLifetime(ServiceLifetime.Scoped)]
public sealed class GoodHandler : IRequestHandler<CreateOrder, string> { ... }
```

#### 说明

- 这个规则当前只在 analyzer **能证明** 生命周期冲突时才报错。
- 运行时 `ValidateCatgaLifetimes()` 仍然保留，负责兜底更复杂的注册图。

#### 常见误用与修复

**误用 1: Singleton Handler 直接依赖 `ICatgaMediator`**
```csharp
services.AddSingleton<IRequestHandler<CreateOrder, string>, BadHandler>();

public sealed class BadHandler(ICatgaMediator mediator)
    : IRequestHandler<CreateOrder, string>
{
    public ValueTask<CatgaResult<string>> HandleAsync(CreateOrder request, CancellationToken ct = default)
        => new(CatgaResult<string>.Success("ok"));
}
```

```csharp
services.AddScoped<IRequestHandler<CreateOrder, string>, GoodHandler>();
```

**误用 2: Singleton Flow 执行器依赖 scoped Flow 服务**
```csharp
services.AddSingleton<IFlowExecutor, CustomFlowExecutor>();
```

```csharp
services.AddScoped<IFlowExecutor, CustomFlowExecutor>();
```

**误用 3: 自动注册类型被强制标成 Singleton**
```csharp
[CatgaLifetime(ServiceLifetime.Singleton)]
public sealed class BadHandler(ICatgaMediator mediator)
    : IRequestHandler<CreateOrder, string>
{
}
```

```csharp
[CatgaLifetime(ServiceLifetime.Scoped)]
public sealed class GoodHandler(ICatgaMediator mediator)
    : IRequestHandler<CreateOrder, string>
{
}
```

**误用 4: 为了“复用实例”把 `IPipelineBehavior<,>` 改成 Singleton**
```csharp
services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
```

```csharp
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
```

**判断原则**:
- 只要构造函数里直接拿了 `ICatgaMediator`、`IFlowExecutor`、`IFlow<T>`，就不要注册成 `Singleton`
- 对 Handler / Behavior / Flow 执行器，默认优先 `Scoped`
- 如果你认为某个类型必须是 `Singleton`，那它就不应该再直接依赖 scoped Catga 入口服务

---

## 📋 完整规则列表

| ID | 规则名称 | 严重性 | 自动修复 | 版本 |
|----|----------|--------|---------|------|
| **新增** |
| CATGA001 | 缺少 [MemoryPackable] | Info | ✅ | v2.0 |
| CATGA002 | 缺少序列化器注册 | Warning | ✅ | v2.0 |
| CAT2004 | Singleton Catga 注册依赖 Scoped 服务 | Error | ❌ | 2026-04-29 |
| **已有** |
| CAT1001 | Handler 未实现接口 | Error | ❌ | v1.0 |
| CAT1002 | 多个 Handler 处理同一消息 | Warning | ❌ | v1.0 |
| CAT1003 | Handler 未注册 | Info | ✅ | v1.0 |
| CAT2002 | Request 必须有返回类型 | Error | ❌ | v1.0 |
| CAT2003 | Event 不应有返回类型 | Warning | ❌ | v1.0 |
| CAT3002 | Behavior 未注册 | Info | ✅ | v1.0 |
| CAT3003 | Behavior 顺序错误 | Warning | ❌ | v1.0 |
| CAT4001 | 性能：避免在热路径使用反射 | Warning | ⚠️ | v1.0 |

**图例**:
- ✅ 有自动修复
- ⚠️ 部分场景有修复
- ❌ 无自动修复
