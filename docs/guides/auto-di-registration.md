# Auto DI Registration

这篇文档解释 Catga 当前的自动注册机制，以及它和旧的 `AddGeneratedServices()` / `AddGeneratedHandlers()` 口径有什么区别。

## 当前结论

Catga 源生成器会在你的应用项目里生成一个扩展方法：

```csharp
builder.Services.AddCatgaServices();
```

它负责把编译时发现的 handler 和带 `[CatgaService]` 的服务注册进 DI。当前应该优先使用这个生成方法。

## 最小接法

### 1. 引入源生成器

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\Catga.SourceGenerator\Catga.SourceGenerator.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

### 2. 编写 handler / service

```csharp
using Catga;

public sealed class CreateOrderHandler : IRequestHandler<CreateOrder, OrderResult>
{
    public ValueTask<CatgaResult<OrderResult>> HandleAsync(
        CreateOrder request,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(CatgaResult<OrderResult>.Success(new(request.OrderId, true)));
    }
}

[CatgaService]
public sealed class OrderRepository : IOrderRepository
{
}
```

### 3. 注册 Catga + 生成扩展

```csharp
builder.Services
    .AddCatga()
    .UseMemoryPack();

builder.Services.AddCatgaServices();
```

## 生成结果长什么样

源生成器会产出类似下面的代码：

```csharp
public static class CatgaUnifiedRegistrations
{
    public static IServiceCollection AddCatgaServices(this IServiceCollection services)
    {
        services.AddScoped<IRequestHandler<CreateOrder, OrderResult>, CreateOrderHandler>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<OrderRepository>();

        return services;
    }
}
```

关键点：

- 这是你应用项目里生成出来的方法，不是 Catga 核心包手写的固定实现
- 目标是避免运行时扫描
- 默认注册行为仍然要尊重 DI 生命周期

## 自动发现范围

当前会自动发现并生成注册代码的典型类型包括：

- `IRequestHandler<TRequest, TResponse>`
- `IRequestHandler<TRequest>`
- `IEventHandler<TEvent>`
- `IPipelineBehavior<TRequest, TResponse>`
- `[CatgaService]` 标记的服务

更完整说明见 `src/Catga.SourceGenerator/README.md`。

## 生命周期

### 默认

未显式声明时，自动注册通常按 `Scoped` 处理。

### 显式指定

```csharp
[CatgaLifetime(ServiceLifetime.Singleton)]
public sealed class CacheService : ICacheService
{
}

[CatgaLifetime(ServiceLifetime.Transient)]
public sealed class TemporaryService : ITemporaryService
{
}
```

如果某个自动注册类型的生命周期和它依赖的 Catga 入口服务冲突，分析器会尽量在编译期直接报出来。

## 什么时候不要自动注册

以下场景更适合手动注册：

- 条件注册
- 工厂注册
- 需要不同实现按环境切换
- 某个服务必须精确控制实例化逻辑

可以用 `[CatgaIgnore]` 或直接不走自动注册路径。

## 如何看生成代码

```bash
cat obj/Debug/net10.0/generated/Catga.SourceGenerator/Catga.SourceGenerator.UnifiedRegistrationGenerator/CatgaUnifiedRegistrations.g.cs
```

如果目标框架不是 `net10.0`，把路径里的 TFM 换成你的实际输出目录。

## 常见问题

### Q: `AddGeneratedServices()` 还能用吗？

A: 旧名字可能还保留兼容入口，但当前文档和新代码应统一迁到 `AddCatgaServices()`。

### Q: 自动注册会不会扫描运行时程序集？

A: 不会。当前主路径是编译时生成注册代码，运行时直接调用生成方法。

### Q: 为什么服务没有注册进去？

先检查：

1. 源生成器是否已接入
2. 类型是否满足自动发现条件
3. 是否真的调用了 `AddCatgaServices()`
4. 生成文件是否出现在 `obj/.../generated/...`

## 相关文档

- [源生成器指南](./source-generator.md)
- [分析器完整指南](./analyzers.md)
- [配置入口](../articles/configuration.md)
